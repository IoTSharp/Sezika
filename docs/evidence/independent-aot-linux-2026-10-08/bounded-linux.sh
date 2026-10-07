#!/bin/bash
# Cooperative payloads retain SEZIKA_LINUX_TASK_TOKEN and must not daemonize.
set -u
umask 077

stat_fields() {
  local raw tail
  IFS= read -r raw < "/proc/$1/stat" 2>/dev/null || return 1
  tail=${raw##*) }
  read -r -a sf <<< "$tail"
  ((${#sf[@]} >= 20)) || return 1
  ppid=${sf[1]}; pgid=${sf[2]}; sid=${sf[3]}; tick=${sf[19]}; state=${sf[0]}
}

# 0: matching live identity; 1: absent/reused/zombie; 2: identity unreadable.
# A failed stat read alone never proves exit when the /proc directory survives.
identity_liveness() {
  local pid=$1 expected=$2
  if stat_fields "$pid"; then
    [[ -n $expected ]] || return 2
    [[ $tick == "$expected" && $state != Z ]] && return 0
    return 1
  fi
  [[ ! -d /proc/$pid ]] && return 1
  return 2
}

if [[ ${1-} == --worker ]]; then
  run=$2; token=$3; shift 3
  [[ $1 == -- ]] || exit 64; shift
  export SEZIKA_LINUX_TASK_TOKEN=$token
  stat_fields $$ || exit 65
  printf '%s\t%s\t%s\t%s\n' "$$" "$tick" "$sid" "$pgid" > "$run/ready"
  gate_start=$SECONDS
  # Human-reviewed: 40 attempts AND 3 seconds, comparison operators only.
  for ((gate_try=0; gate_try<40 && SECONDS-gate_start<3; gate_try++)); do
    [[ -f $run/go ]] && exec "$@"
    /bin/sleep 0.1
  done
  exit 66
fi

[[ $# -ge 5 && $1 == /* && $2 =~ ^[0-9]+$ && $3 =~ ^[0-9]+$ && $4 == -- ]] || exit 64
run=$1; budget=$2; cancel_after=$3; shift 4
((budget >= 1 && budget <= 300 && cancel_after <= budget)) || exit 64
[[ -d $run && ! -L $run && ! -e $run/identity.tsv && ! -e $run/result.tsv ]] || exit 64
[[ ! -e $run/ready && ! -e $run/go && ! -e $run/cancel.request ]] || exit 64
token="sezika-${BASHPID}-${RANDOM}-${RANDOM}"
root_pid=$$; begin=$SECONDS; status=failed; payload_exit=unknown
cleanup_done=0; cleanup_bad=0; ownership_bad=0; launcher=0; launcher_tick=''
hz=$(/usr/bin/getconf CLK_TCK); btime=0; boot_begin=$SECONDS
[[ $hz =~ ^[0-9]+$ && $hz -gt 0 ]] || exit 64
exec {bootfd}< /proc/stat
for ((boot_line=0; boot_line<1024 && SECONDS-boot_begin<1; boot_line++)); do
  IFS=' ' read -r key value rest <&$bootfd || break
  if [[ $key == btime && $value =~ ^[0-9]+$ ]]; then btime=$value; break; fi
done
exec {bootfd}<&-
((btime > 0)) || exit 64
declare -A ticks=() parents=() groups=() sessions=() commands=() depths=() observed=()
printf 'pid\tstart_tick\tstart_utc_seconds_precision\tobserved_utc\tppid\tpgid\tsid\tdepth_unknown\targv_shell_escaped\tparent_chain\n' > "$run/identity.tsv"
printf 'event\tutc\tpid\tstart_tick\tdetail\n' > "$run/audit.tsv"
audit() { printf '%s\t%s\t%s\t%s\t%q\n' "$1" "$(/usr/bin/date -u +%FT%TZ)" "${2-0}" "${3-0}" "${4-}" >> "$run/audit.tsv"; }

has_token() {
  local envfd entry i estart=$SECONDS
  exec {envfd}< "/proc/$1/environ" 2>/dev/null || return 1
  for ((i=0; i<256 && SECONDS-estart<1; i++)); do
    entry=''
    IFS= read -r -d '' -n 8192 entry <&$envfd || break
    if [[ $entry == "SEZIKA_LINUX_TASK_TOKEN=$token" ]]; then exec {envfd}<&-; return 0; fi
  done
  exec {envfd}<&-
  return 1
}

record_owned() {
  local pid=$1 first=$tick argvfd arg argv='' count=0 ast=$SECONDS chain='' parent=$ppid cpid next n
  has_token "$pid" || return 1
  stat_fields "$pid" || return 1
  [[ $tick == "$first" ]] || return 1
  [[ -v ticks[$pid] ]] || { ((${#ticks[@]} < 1024)) || { ownership_bad=1; return 1; }; }
  exec {argvfd}< "/proc/$pid/cmdline" 2>/dev/null || return 1
  for ((count=0; count<256 && SECONDS-ast<1; count++)); do
    arg=''; IFS= read -r -d '' arg <&$argvfd || break
    printf -v arg '%q' "$arg"; argv+="$arg "
    ((${#argv} <= 262144)) || { ownership_bad=1; break; }
  done
  exec {argvfd}<&-
  ((count < 256 && SECONDS-ast<1)) || ownership_bad=1
  stat_fields "$pid" || return 1
  [[ $tick == "$first" ]] || return 1
  local save_ppid=$ppid save_pgid=$pgid save_sid=$sid save_tick=$tick
  cpid=$save_ppid
  for ((n=0; n<32 && SECONDS-ast<2; n++)); do
    [[ $cpid =~ ^[0-9]+$ && $cpid -gt 0 ]] || break
    stat_fields "$cpid" || break
    chain+="$cpid:$tick:$ppid:$pgid:$sid;"; next=$ppid
    [[ $next == "$cpid" ]] && break
    cpid=$next
  done
  local changed=1
  if [[ ${ticks[$pid]-} == "$save_tick" && ${parents[$pid]-} == "$save_ppid" && ${groups[$pid]-} == "$save_pgid" && ${sessions[$pid]-} == "$save_sid" && ${commands[$pid]-} == "$argv" ]]; then changed=0; fi
  ticks[$pid]=$save_tick; parents[$pid]=$save_ppid; groups[$pid]=$save_pgid; sessions[$pid]=$save_sid
  commands[$pid]=$argv; depths[$pid]=-1; observed[$pid]=1
  if ((changed)); then printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' "$pid" "$save_tick" "$(/usr/bin/date -u -d "@$((btime+save_tick/hz))" +%FT%TZ)" "$(/usr/bin/date -u +%FT%TZ)" "$save_ppid" "$save_pgid" "$save_sid" "${depths[$pid]}" "$argv" "$chain" >> "$run/identity.tsv"; fi
}

snapshot() {
  local scan=$SECONDS pid item count=0
  observed=()
  # /proc process inventory only; at most 4096 items and 2 wall-clock seconds.
  for item in /proc/[0-9]*; do
    ((count++))
    if ((count > 4096 || SECONDS-scan >= 2)); then ownership_bad=1; audit scan_limit 0 0 "$count"; return 1; fi
    pid=${item##*/}; [[ $pid != "$root_pid" ]] || continue
    stat_fields "$pid" || continue
    record_owned "$pid" || true
  done
  return 0
}

signal_failure() {
  local pid=$1 expected=$2 reason=$3 sig=$4 result
  identity_liveness "$pid" "$expected"; result=$?
  if ((result == 1)); then audit signal_unneeded "$pid" "$expected" "$reason after exit/reuse/zombie ($sig)"; return 0; fi
  cleanup_bad=1
  audit "$reason" "$pid" "$expected" "$sig liveness=$result"
  return 1
}

signal_owned() {
  local pid=$1 sig=$2 expected=${ticks[$1]-}
  [[ -n $expected ]] || return 0
  identity_liveness "$pid" "$expected" || { signal_failure "$pid" "$expected" stat_unreadable "$sig"; return $?; }
  has_token "$pid" || { signal_failure "$pid" "$expected" ownership_unreadable "$sig"; return $?; }
  record_owned "$pid" || { signal_failure "$pid" "$expected" identity_refresh_failed "$sig"; return $?; }
  identity_liveness "$pid" "$expected" || { signal_failure "$pid" "$expected" stat_unreadable "$sig"; return $?; }
  [[ $ppid == "${parents[$pid]}" && $pgid == "${groups[$pid]}" && $sid == "${sessions[$pid]}" ]] || return 0
  # Refresh full argv/parent chain; token and repeated start tick reject reused PIDs.
  audit signal "$pid" "$expected" "$sig ppid=$ppid pgid=$pgid sid=$sid"
  kill -s "$sig" -- "$pid" 2>/dev/null || { signal_failure "$pid" "$expected" signal_failed "$sig"; return $?; }
}

cleanup() {
  ((cleanup_done == 0)) || return
  cleanup_done=1
  trap '' TERM INT HUP
  local clean=$SECONDS pass pid left=0
  for ((pass=0; pass<6 && SECONDS-clean<8; pass++)); do
    snapshot || cleanup_bad=1
    left=0
    # Freeze only verified identities, rediscover, then TERM+CONT; later KILL.
    for pid in "${!observed[@]}"; do
      ((SECONDS-clean<8)) || { cleanup_bad=1; break; }
      identity_liveness "$pid" "${ticks[$pid]}" || { signal_failure "$pid" "${ticks[$pid]}" stat_unreadable cleanup_pass; continue; }
      ((left++))
      if ((pass == 0)); then signal_owned "$pid" STOP; fi
      if ((pass < 2)); then signal_owned "$pid" TERM; signal_owned "$pid" CONT; else signal_owned "$pid" KILL; fi
    done
    ((left > 0)) || break
    /bin/sleep 0.2
  done
  snapshot || cleanup_bad=1
  left=0
  for pid in "${!ticks[@]}"; do
    ((SECONDS-clean<8)) || { cleanup_bad=1; break; }
    identity_liveness "$pid" "${ticks[$pid]}" || { signal_failure "$pid" "${ticks[$pid]}" stat_unreadable final_check; continue; }
    ((left++))
    [[ -v observed[$pid] ]] || { cleanup_bad=1; audit known_identity_unreadable "$pid" "$tick" 'Still live but absent from ownership snapshot'; }
  done
  ((left == 0 && ownership_bad == 0)) || cleanup_bad=1
  if [[ $launcher -gt 0 ]]; then
    identity_liveness "$launcher" "$launcher_tick"; launcher_live=$?
    if ((launcher_live == 1)); then wait "$launcher" 2>/dev/null || true; else cleanup_bad=1; audit launcher_still_live_or_unknown "$launcher" "$launcher_tick" "$launcher_live"; fi
  fi
  printf 'status\t%s\npayload_exit\t%s\nelapsed_seconds\t%s\ncleanup_incomplete\t%s\nsurvivors\t%s\ntracked_identities\t%s\ntoken\t%s\nsupervisor_pid\t%s\n' "$status" "$payload_exit" "$((SECONDS-begin))" "$cleanup_bad" "$left" "${#ticks[@]}" "$token" "$$" > "$run/result.tsv"
  audit finished 0 0 "$status cleanup_incomplete=$cleanup_bad survivors=$left"
}
on_signal() { status=cancelled_signal; exit 125; }
trap cleanup EXIT
trap on_signal TERM INT HUP
stat_fields "$$" || exit 126
supervisor_tick=$tick; supervisor_ppid=$ppid; supervisor_sid=$sid; supervisor_pgid=$pgid
supervisor_command=''; supervisor_chain=''; self_begin=$SECONDS
exec {selffd}< "/proc/$$/cmdline"
for ((self_arg=0; self_arg<256 && SECONDS-self_begin<1; self_arg++)); do
  self_value=''; IFS= read -r -d '' self_value <&$selffd || break
  printf -v self_value '%q' "$self_value"; supervisor_command+="$self_value "
  ((${#supervisor_command} <= 262144)) || { ownership_bad=1; break; }
done
exec {selffd}<&-
((self_arg<256 && SECONDS-self_begin<1)) || ownership_bad=1
self_parent=$supervisor_ppid
for ((self_depth=0; self_depth<32 && SECONDS-self_begin<2; self_depth++)); do
  [[ $self_parent =~ ^[0-9]+$ && $self_parent -gt 0 ]] || break
  stat_fields "$self_parent" || break
  supervisor_chain+="$self_parent:$tick:$ppid:$pgid:$sid;"; self_next=$ppid
  [[ $self_next != "$self_parent" ]] || break
  self_parent=$self_next
done
printf 'pid\tstart_tick\tstart_utc_seconds_precision\tobserved_utc\tppid\tpgid\tsid\targv_shell_escaped\tparent_chain\n%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' "$$" "$supervisor_tick" "$(/usr/bin/date -u -d "@$((btime+supervisor_tick/hz))" +%FT%TZ)" "$(/usr/bin/date -u +%FT%TZ)" "$supervisor_ppid" "$supervisor_pgid" "$supervisor_sid" "$supervisor_command" "$supervisor_chain" > "$run/supervisor.tsv"
audit started "$$" "$supervisor_tick" "budget=$budget cancel_after=$cancel_after ppid=$supervisor_ppid pgid=$supervisor_pgid sid=$supervisor_sid"
SEZIKA_LINUX_TASK_TOKEN=$token /usr/bin/setsid /bin/bash "$0" --worker "$run" "$token" -- "$@" > "$run/payload.stdout.log" 2> "$run/payload.stderr.log" &
launcher=$!
if stat_fields "$launcher"; then launcher_tick=$tick; fi
for ((ready_try=0; ready_try<30 && SECONDS-begin<3; ready_try++)); do
  [[ -s $run/ready ]] && break
  /bin/sleep 0.1
done
[[ -s $run/ready ]] || { status=readiness_failed; exit 126; }
IFS=$'\t' read -r leader leader_tick leader_sid leader_group < "$run/ready"
[[ $leader =~ ^[0-9]+$ ]] || exit 126
stat_fields "$leader" || { status=readiness_failed; exit 126; }
[[ $tick == "$leader_tick" && $sid == "$leader_sid" && $pgid == "$leader_group" ]] || exit 126
record_owned "$leader" || { status=ownership_failed; exit 126; }
: > "$run/go"
# Human-reviewed: 4*budget+16 iterations AND budget seconds, 250ms backoff.
for ((attempt=0; attempt<4*budget+16 && SECONDS-begin<budget; attempt++)); do
  snapshot || { status=inventory_failed; exit 127; }
  [[ ! -f $run/cancel.request ]] || { status=cancelled_file; exit 125; }
  if ((cancel_after > 0 && SECONDS-begin >= cancel_after)); then : > "$run/cancel.request"; status=cancelled_fixture; exit 125; fi
  identity_liveness "$leader" "$leader_tick"; leader_live=$?
  if ((leader_live == 2)); then status=runtime_identity_unreadable; exit 127; fi
  if ((leader_live == 1)); then
    identity_liveness "$launcher" "$launcher_tick"; launcher_live=$?
    ((launcher_live == 1)) || { status=launcher_identity_live_or_unknown; exit 127; }
    wait "$launcher"; payload_exit=$?; status=completed
    ((payload_exit == 0)) || status=payload_failed
    cleanup
    ((payload_exit == 0 && cleanup_bad == 0)) && exit 0
    exit 127
  fi
  ((attempt % 20 != 19)) || audit progress "$leader" "$leader_tick" "$((SECONDS-begin))s"
  /bin/sleep 0.25
done
status=timed_out
exit 124
