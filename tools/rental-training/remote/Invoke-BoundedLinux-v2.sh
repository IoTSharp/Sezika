#!/bin/bash
# Cooperative payloads retain SEZIKA_LINUX_TASK_TOKEN. Very short intermediate
# processes can exit before a snapshot; this is not an arbitrary-process sandbox.
# The shared monotonic deadline bounds cooperative checks, not blocking kernel I/O.
set -u
umask 077
phase=setup; limit_ms=0; now_ms=0; cleanup_budget_exhausted=0; work_budget_exhausted=0
clock_ms() {
  local up fraction whole
  IFS=' ' read -r up _ < /proc/uptime || return 1
  whole=${up%.*}; fraction=${up#*.}; fraction=${fraction:0:2}
  [[ $whole =~ ^[0-9]+$ && $fraction =~ ^[0-9]{2}$ ]] || return 1
  now_ms=$((10#$whole*1000 + 10#$fraction*10))
}
budget_ok() {
  clock_ms || return 1
  ((limit_ms == 0 || now_ms < limit_ms)) && return 0
  if [[ $phase == cleanup || $phase == final ]]; then cleanup_budget_exhausted=1; else work_budget_exhausted=1; fi
  return 1
}
stamp_utc() { TZ=UTC printf -v utc_stamp '%(%FT%TZ)T' -1; }


stat_fields() {
  local raw tail
  budget_ok || return 1
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
  budget_ok || return 2
  [[ ! -d /proc/$pid ]] && return 1
  return 2
}

if [[ ${1-} == --worker ]]; then
  run=$2; token=$3; shift 3
  [[ $1 == -- ]] || exit 64; shift
  export SEZIKA_LINUX_TASK_TOKEN=$token
  stat_fields $$ || exit 65
  printf '%s\t%s\t%s\t%s\n' "$$" "$tick" "$sid" "$pgid" > "$run/ready"
  # A stopped worker needs no polling helper; parent verifies T before CONT.
  kill -s STOP -- "$$" || exit 66
  [[ -f $run/go ]] || exit 66
  exec "$@"
fi

[[ $# -ge 8 && $1 == /* && $2 =~ ^[0-9]+$ && $3 =~ ^[0-9]+$ && $4 =~ ^[0-9]+$ && $7 == -- ]] || exit 64
run=$1; budget=$2; cleanup_seconds=$3; cancel_after=$4; cancel_mode=$5; fixture=$6; shift 7
((budget >= 1 && budget <= 1520 && cleanup_seconds >= 1 && cleanup_seconds <= 8 && cancel_after <= budget)) || exit 64
[[ $cancel_mode == file || $cancel_mode == signal ]] || exit 64
[[ $fixture == none || $fixture == expired-cleanup || $fixture == truncate-final || $fixture == identity-seams ]] || exit 64
if [[ $fixture != none ]]; then [[ $run == */.artifacts/linux-runner-hardening-20261008/* ]] || exit 64; fi
clock_ms || exit 64
begin_ms=$now_ms; work_deadline_ms=$((begin_ms+budget*1000)); limit_ms=$work_deadline_ms
[[ -d $run && ! -L $run && ! -e $run/identity.tsv && ! -e $run/result.tsv ]] || exit 64
[[ ! -e $run/ready && ! -e $run/go && ! -e $run/cancel.request ]] || exit 64
token="sezika-${BASHPID}-${RANDOM}-${RANDOM}"
root_pid=$$; begin=$SECONDS; status=failed; payload_exit=unknown
cleanup_done=0; cleanup_bad=0; ownership_bad=0; launcher=0; launcher_tick=''; cleanup_deferred_proofs=0
timer_pid=0; timer_tick=""; timerfd=0
control_pid=0; control_tick=""; control_argv=""; control_chain=""; control_unaffected=0; control_owner_closed=0
btime=0; boot_begin_ms=$now_ms; boot_deadline_ms=$((boot_begin_ms+3000))
exec {bootfd}< /proc/stat
for ((boot_line=0; boot_line<1024; boot_line++)); do
  budget_ok || exit 64
  ((now_ms<boot_deadline_ms)) || exit 64
  IFS=' ' read -r key value rest <&$bootfd || break
  if [[ $key == btime && $value =~ ^[0-9]+$ ]]; then btime=$value; break; fi
done
exec {bootfd}<&-
((btime > 0)) || exit 64
declare -A ticks=() parents=() groups=() sessions=() commands=() depths=() observed=()
printf 'pid\tstart_tick\tstart_utc_seconds_precision\tobserved_utc\tppid\tpgid\tsid\tdepth_unknown\targv_shell_escaped\tparent_chain\n' > "$run/identity.tsv"
printf 'event\tutc\tpid\tstart_tick\tdetail\n' > "$run/audit.tsv"
audit() { budget_ok || return 1; stamp_utc; printf '%s\t%s\t%s\t%s\t%q\n' "$1" "$utc_stamp" "${2-0}" "${3-0}" "${4-}" >> "$run/audit.tsv"; }

has_token() {
  local envfd entry i token_begin_ms token_deadline_ms
  budget_ok || return 2
  token_begin_ms=$now_ms; token_deadline_ms=$((token_begin_ms+1000))
  ((limit_ms==0 || token_deadline_ms<=limit_ms)) || token_deadline_ms=$limit_ms
  budget_ok && ((now_ms<token_deadline_ms)) || return 2
  exec {envfd}< "/proc/$1/environ" 2>/dev/null || return 2
  for ((i=0; i<256; i++)); do
    budget_ok && ((now_ms<token_deadline_ms)) || { exec {envfd}<&-; return 2; }
    entry=''
    IFS= read -r -d '' -n 8192 entry <&$envfd || break
    ((${#entry}<8192)) || { exec {envfd}<&-; return 2; }
    if [[ $entry == "SEZIKA_LINUX_TASK_TOKEN=$token" ]]; then budget_ok && ((now_ms<token_deadline_ms)) || { exec {envfd}<&-; return 2; }; exec {envfd}<&-; return 0; fi
  done
  exec {envfd}<&-
  budget_ok && ((i<256 && now_ms<token_deadline_ms)) || return 2
  return 1
}

record_owned() {
  local pid=$1 first ownership_mode=${2-token} token_state argvfd arg argv='' count=0 chain='' parent=$ppid cpid next n record_begin_ms argv_deadline_ms chain_deadline_ms
  budget_ok || return 1
  record_begin_ms=$now_ms; argv_deadline_ms=$((record_begin_ms+1000)); chain_deadline_ms=$((record_begin_ms+2000))
  ((limit_ms==0 || argv_deadline_ms<=limit_ms)) || argv_deadline_ms=$limit_ms
  ((limit_ms==0 || chain_deadline_ms<=limit_ms)) || chain_deadline_ms=$limit_ms
  budget_ok && ((now_ms<argv_deadline_ms)) || return 1
  stat_fields "$pid" || return 1
  first=$tick
  budget_ok && ((now_ms<argv_deadline_ms)) || return 1
  has_token "$pid"; token_state=$?
  if [[ $ownership_mode == absent ]]; then ((token_state==1)) || return 1; else ((token_state==0)) || return 1; fi
  budget_ok && ((now_ms<argv_deadline_ms)) || return 1
  stat_fields "$pid" || return 1
  [[ $tick == "$first" ]] || return 1
  [[ -v ticks[$pid] ]] || { ((${#ticks[@]} < 4096)) || { ownership_bad=1; return 1; }; }
  budget_ok && ((now_ms<argv_deadline_ms)) || return 1
  exec {argvfd}< "/proc/$pid/cmdline" 2>/dev/null || return 1
  for ((count=0; count<256; count++)); do
    budget_ok || { exec {argvfd}<&-; return 1; }
    ((now_ms<argv_deadline_ms)) || { ownership_bad=1; exec {argvfd}<&-; return 1; }
    arg=''; IFS= read -r -d '' arg <&$argvfd || break
    printf -v arg '%q' "$arg"; argv+="$arg "
    ((${#argv} <= 262144)) || { ownership_bad=1; exec {argvfd}<&-; return 1; }
  done
  exec {argvfd}<&-
  budget_ok || return 1
  ((count < 256 && now_ms<argv_deadline_ms)) || { ownership_bad=1; return 1; }
  budget_ok && ((now_ms<chain_deadline_ms)) || return 1
  stat_fields "$pid" || return 1
  [[ $tick == "$first" ]] || return 1
  local save_ppid=$ppid save_pgid=$pgid save_sid=$sid save_tick=$tick save_state=$state
  cpid=$save_ppid
  for ((n=0; n<32; n++)); do
    budget_ok || return 1
    ((now_ms<chain_deadline_ms)) || { ownership_bad=1; return 1; }
    [[ $cpid =~ ^[0-9]+$ && $cpid -gt 0 ]] || break
    stat_fields "$cpid" || { ownership_bad=1; return 1; }
    chain+="$cpid:$tick:$ppid:$pgid:$sid;"; next=$ppid
    [[ $next != "$cpid" ]] || { ownership_bad=1; return 1; }
    cpid=$next
  done
  budget_ok || return 1
  ((n < 32 && now_ms<chain_deadline_ms)) || { ownership_bad=1; return 1; }
  budget_ok && ((now_ms<chain_deadline_ms)) || return 1
  current_argv=$argv; current_chain=$chain
  local changed=1
  if [[ ${ticks[$pid]-} == "$save_tick" && ${parents[$pid]-} == "$save_ppid" && ${groups[$pid]-} == "$save_pgid" && ${sessions[$pid]-} == "$save_sid" && ${commands[$pid]-} == "$argv" ]]; then changed=0; fi
  ticks[$pid]=$save_tick; parents[$pid]=$save_ppid; groups[$pid]=$save_pgid; sessions[$pid]=$save_sid
  commands[$pid]=$argv; depths[$pid]=-1; observed[$pid]=1
  if ((changed)); then stamp_utc; printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' "$pid" "$save_tick" 'see_start_tick_and_boot_epoch' "$utc_stamp" "$save_ppid" "$save_pgid" "$save_sid" "${depths[$pid]}" "$argv" "$chain" >> "$run/identity.tsv"; fi
  tick=$save_tick; ppid=$save_ppid; pgid=$save_pgid; sid=$save_sid; state=$save_state
}

snapshot() {
  local scan=$SECONDS pid item count=0
  observed=()
  budget_ok || return 1
  # /proc process inventory only; at most 4096 items and 4 wall-clock seconds.
  for item in /proc/[0-9]*; do
    budget_ok || return 1
    ((count++))
    if ((count > 4096 || SECONDS-scan >= 4)); then ownership_bad=1; audit scan_limit 0 0 "$count"; return 1; fi
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
  if ((result == 0)) && [[ $phase == cleanup && ( $reason == ownership_unreadable || $reason == identity_refresh_failed ) ]]; then
    audit cleanup_deferred_proof "$pid" "$expected" "reason=$reason requested_signal=$sig liveness=0 action=not_signalled retry=existing_cleanup" || { cleanup_bad=1; return 1; }
    cleanup_deferred_proofs=$((cleanup_deferred_proofs+1))
    return 0
  fi
  cleanup_bad=1
  audit "$reason" "$pid" "$expected" "$sig liveness=$result"
  return 1
}

signal_owned() {
  local pid=$1 sig=$2 expected=${ticks[$1]-} before_argv before_chain
  budget_ok || return 1
  [[ -n $expected ]] || return 0
  identity_liveness "$pid" "$expected" || { signal_failure "$pid" "$expected" stat_unreadable "$sig"; return $?; }
  has_token "$pid" || { signal_failure "$pid" "$expected" ownership_unreadable "$sig"; return $?; }
  record_owned "$pid" || { signal_failure "$pid" "$expected" identity_refresh_failed "$sig"; return $?; }
  before_argv=$current_argv; before_chain=$current_chain
  record_owned "$pid" || { signal_failure "$pid" "$expected" identity_refresh_failed "$sig"; return $?; }
  [[ $current_argv == "$before_argv" && $current_chain == "$before_chain" ]] || { cleanup_bad=1; return 1; }
  identity_liveness "$pid" "$expected" || { signal_failure "$pid" "$expected" stat_unreadable "$sig"; return $?; }
  budget_ok || return 1
  [[ $ppid == "${parents[$pid]}" && $pgid == "${groups[$pid]}" && $sid == "${sessions[$pid]}" ]] || return 0
  # Refresh full argv/parent chain; token and repeated start tick reject reused PIDs.
  audit signal "$pid" "$expected" "$sig ppid=$ppid pgid=$pgid sid=$sid" || return 1
  budget_ok || return 1
  kill -s "$sig" -- "$pid" 2>/dev/null || { signal_failure "$pid" "$expected" signal_failed "$sig"; return $?; }
}


forget_control() {
  unset 'ticks[$control_pid]' 'parents[$control_pid]' 'groups[$control_pid]' 'sessions[$control_pid]' 'commands[$control_pid]' 'depths[$control_pid]' 'observed[$control_pid]'
}
close_control() {
  ((control_pid>0)) || return 0
  budget_ok || return 1
  identity_liveness "$control_pid" "$control_tick" || return 1
  local before_ppid=${control_ppid-} before_pgid=${control_pgid-} before_sid=${control_sid-}
  record_owned "$control_pid" absent || return 1
  [[ $current_argv == "$control_argv" && $current_chain == "$control_chain" ]] || { forget_control; return 1; }
  identity_liveness "$control_pid" "$control_tick" || return 1
  [[ $ppid == "$before_ppid" && $pgid == "$before_pgid" && $sid == "$before_sid" ]] || { forget_control; return 1; }
  forget_control
  control_unaffected=1
  audit control_owner_signal "$control_pid" "$control_tick" 'non-token control; complete own argv/parent chain; KILL' || return 1
  budget_ok || return 1
  # Only the separate, complete owner proof above permits this control cleanup.
  kill -s KILL -- "$control_pid" || return 1
  pause_owned 50 || return 1
  identity_liveness "$control_pid" "$control_tick"; local state_after=$?
  ((state_after==1)) || return 1
  wait "$control_pid" 2>/dev/null || true
  control_owner_closed=1
}

# One absolute cleanup deadline; reserve 200ms for bounded result serialization.
pause_owned() {
  local milliseconds=$1 rest duration rc; pause_reason=none; pause_read_rc=not_read
  budget_ok || { pause_reason=clock_failed; ((limit_ms>0 && now_ms>=limit_ms)) && pause_reason=deadline; return 1; }
  rest=$((limit_ms-now_ms)); ((rest>0)) || { pause_reason=deadline; return 1; }
  ((milliseconds<=rest)) || milliseconds=$rest
  ((milliseconds>0 && milliseconds<=250)) || { pause_reason=invalid_duration; return 1; }
  printf -v duration '0.%03d' "$milliseconds"
  # A single registered cooperative timer holds this pipe open. Bash read -t
  # supplies the wall-clock backoff without wait, new sleep helpers, or spinning.
  IFS= read -r -N 1 -t "$duration" -u "$timerfd" timer_value; rc=$?; pause_read_rc=$rc
  ((rc>128)) || { pause_reason=read_return; return 1; }
  budget_ok || { pause_reason=clock_failed; ((limit_ms>0 && now_ms>=limit_ms)) && pause_reason=deadline; return 1; }
}
close_timer() {
  ((timer_pid>0)) || return 0
  budget_ok || return 1
  signal_owned "$timer_pid" KILL || return 1
  # A single bounded pipe read allows exit delivery. Reap only after the
  # exact start-tick identity is absent/reused/Z, never a live child wait.
  IFS= read -r -N 1 -t 0.05 -u "$timerfd" timer_value || true
  budget_ok || return 1
  identity_liveness "$timer_pid" "$timer_tick"; local timer_state=$?
  ((timer_state==1)) || return 1
  wait "$timer_pid" 2>/dev/null || true
  exec {timerfd}<&-
  timer_pid=0
}

cleanup() {
  ((cleanup_done == 0)) || return
  cleanup_done=1; trap '' TERM INT HUP
  clock_ms || { cleanup_bad=1; now_ms=$work_deadline_ms; }
  cleanup_start_ms=$now_ms; cleanup_deadline_ms=$((now_ms+cleanup_seconds*1000))
  limit_ms=$((cleanup_deadline_ms-200)); phase=cleanup
  if [[ $fixture == expired-cleanup ]]; then
    close_timer || cleanup_bad=1
    clock_ms || true; limit_ms=$now_ms
  fi
  local pass pid result all_done=1
  final_check_complete=0; verified_live_count=0; unknown_count=0
  unchecked_count=${#ticks[@]}; final_checked=0
  for ((pass=0; pass<6 && now_ms<limit_ms; pass++)); do
    budget_ok || break
    snapshot || { cleanup_bad=1; break; }
    local left=0 signals=0
    for pid in "${!observed[@]}"; do
      [[ $pid != "$timer_pid" ]] || continue
      ((signals++)); ((signals<=4096)) || { cleanup_bad=1; break; }
      budget_ok || { cleanup_bad=1; break; }
      identity_liveness "$pid" "${ticks[$pid]}"; result=$?
      ((result!=1)) || continue
      if ((result==2)); then cleanup_bad=1; continue; fi
      ((left++))
      if ((pass==0)); then signal_owned "$pid" STOP || cleanup_bad=1; fi
      if ((pass<2)); then
        signal_owned "$pid" TERM || cleanup_bad=1
        signal_owned "$pid" CONT || cleanup_bad=1
      else signal_owned "$pid" KILL || cleanup_bad=1; fi
    done
    ((left>0)) || break
    pause_owned 200 || { cleanup_bad=1; break; }
  done
  close_control || cleanup_bad=1
  close_timer || cleanup_bad=1
  phase=final
  # No new inventory after expiry. Its completeness is distinct from zero live.
  final_inventory_complete=0
  if budget_ok && snapshot; then final_inventory_complete=1; else cleanup_bad=1; fi
  unchecked_count=${#ticks[@]}; final_checked=0
  local final_limit=4096
  [[ $fixture != truncate-final ]] || final_limit=0
  for pid in "${!ticks[@]}"; do
    if ((final_checked>=final_limit)) || ! budget_ok; then all_done=0; cleanup_bad=1; break; fi
    ((final_checked++)); ((unchecked_count--))
    identity_liveness "$pid" "${ticks[$pid]}"; result=$?
    if ((result==0)); then ((verified_live_count++))
    elif ((result==2)); then ((unknown_count++)); cleanup_bad=1; fi
  done
  if [[ $fixture == identity-seams ]]; then
    # Pure controlled seam: no invented PID, /proc mutation, or signal.
    identity_liveness "$root_pid" "${supervisor_tick}:wrong"; seam_mismatch=$?
    seam_unreadable_expected=''
    identity_liveness "$root_pid" "$seam_unreadable_expected"; seam_unreadable=$?
    [[ $seam_mismatch == 1 && $seam_unreadable == 2 ]] || cleanup_bad=1
    budget_ok || { cleanup_bad=1; all_done=0; }
  fi
  ((all_done==1 && final_inventory_complete==1 && unchecked_count==0 && unknown_count==0 && ownership_bad==0 && cleanup_budget_exhausted==0)) && final_check_complete=1
  ((final_check_complete==1 && verified_live_count==0 && ownership_bad==0)) || cleanup_bad=1
  if [[ $launcher -gt 0 ]] && budget_ok; then
    identity_liveness "$launcher" "$launcher_tick"; result=$?
    if ((result==1)); then wait "$launcher" 2>/dev/null || true; else cleanup_bad=1; fi
  else cleanup_bad=1; fi
  # Serialization consumes the reserved part of the SAME deadline, not a new 8s.
  limit_ms=$cleanup_deadline_ms
  if ! budget_ok; then cleanup_bad=1; cleanup_budget_exhausted=1; fi
  clock_ms || true
  printf 'status\t%s\npayload_exit\t%s\nelapsed_ms\t%s\ncleanup_elapsed_ms\t%s\ncleanup_deadline_ms\t%s\ncleanup_incomplete\t%s\nfinal_check_complete\t%s\nverified_live_count\t%s\nunknown_count\t%s\nunchecked_count\t%s\ncleanup_budget_exhausted\t%s\ntracked_identities\t%s\ntoken\t%s\nsupervisor_pid\t%s\nfixture\t%s\nclock_resolution_ms\t10\nclock_tolerance_ms\t20\nlimitation\tcooperative_checks_only_blocking_kernel_io_and_short_intermediates_not_guaranteed\n' "$status" "$payload_exit" "$((now_ms-begin_ms))" "$((now_ms-cleanup_start_ms))" "$cleanup_deadline_ms" "$cleanup_bad" "$final_check_complete" "$verified_live_count" "$unknown_count" "$unchecked_count" "$cleanup_budget_exhausted" "${#ticks[@]}" "$token" "$$" "$fixture" > "$run/result.tsv"
  printf 'control_unaffected\t%s\ncontrol_owner_closed\t%s\nseam_mismatch\t%s\nseam_unreadable\t%s\n' "$control_unaffected" "$control_owner_closed" "${seam_mismatch-na}" "${seam_unreadable-na}" >> "$run/result.tsv"
  printf 'work_pause_reason\t%s\nwork_pause_read_rc\t%s\nwork_pause_failed_ms\t%s\n' "${work_pause_reason-na}" "${work_pause_read_rc-na}" "${work_pause_failed_ms-na}" >> "$run/result.tsv"
  printf 'cleanup_deferred_proofs\t%s\n' "$cleanup_deferred_proofs" >> "$run/result.tsv"
  budget_ok || { cleanup_bad=1; cleanup_budget_exhausted=1; printf 'serialization_budget_exhausted\t1\n' >> "$run/result.tsv"; }
}

on_signal() { status=cancelled_signal; audit signal_trap "$$" "${supervisor_tick-unknown}" "signal=$1 delivered=1 syscall_rc=not_returned elapsed_ms=$((now_ms-begin_ms))" || true; exit 125; }
trap cleanup EXIT
trap 'on_signal TERM' TERM; trap 'on_signal INT' INT; trap 'on_signal HUP' HUP
stat_fields "$$" || exit 126
supervisor_tick=$tick; supervisor_ppid=$ppid; supervisor_sid=$sid; supervisor_pgid=$pgid
supervisor_command=''; supervisor_chain=''; self_begin=$SECONDS
exec {selffd}< "/proc/$$/cmdline"
for ((self_arg=0; self_arg<256 && SECONDS-self_begin<1; self_arg++)); do
  budget_ok || { ownership_bad=1; break; }
  self_value=''; IFS= read -r -d '' self_value <&$selffd || break
  printf -v self_value '%q' "$self_value"; supervisor_command+="$self_value "
  ((${#supervisor_command} <= 262144)) || { ownership_bad=1; break; }
done
exec {selffd}<&-
((self_arg<256 && SECONDS-self_begin<1)) || ownership_bad=1
self_parent=$supervisor_ppid
for ((self_depth=0; self_depth<32 && SECONDS-self_begin<2; self_depth++)); do
  budget_ok || { ownership_bad=1; break; }
  [[ $self_parent =~ ^[0-9]+$ && $self_parent -gt 0 ]] || break
  stat_fields "$self_parent" || break
  supervisor_chain+="$self_parent:$tick:$ppid:$pgid:$sid;"; self_next=$ppid
  [[ $self_next != "$self_parent" ]] || break
  self_parent=$self_next
done
stamp_utc
printf 'pid\tstart_tick\tstart_utc_seconds_precision\tobserved_utc\tppid\tpgid\tsid\targv_shell_escaped\tparent_chain\n%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' "$$" "$supervisor_tick" "boot_epoch=$btime;start_tick=$supervisor_tick" "$utc_stamp" "$supervisor_ppid" "$supervisor_pgid" "$supervisor_sid" "$supervisor_command" "$supervisor_chain" > "$run/supervisor.tsv"
((self_depth<32 && SECONDS-self_begin<2)) || ownership_bad=1
((ownership_bad==0)) || exit 126
audit started "$$" "$supervisor_tick" "budget=$budget cancel_after=$cancel_after ppid=$supervisor_ppid pgid=$supervisor_pgid sid=$supervisor_sid"
# One timer, always individually recorded; no per-poll helper process.
exec {timerfd}< <( export SEZIKA_LINUX_TASK_TOKEN=$token; exec /bin/sleep "$((budget+cleanup_seconds+3))" )
timer_pid=$!
timer_begin_ms=$now_ms
if stat_fields "$timer_pid"; then timer_tick=$tick; audit timer_pending "$timer_pid" "$timer_tick" 'direct process-substitution child; expected sleep argv; token/argv/parent chain pending'; fi
timer_registered=0
for ((timer_try=0; timer_try<10 && now_ms-timer_begin_ms<1000; timer_try++)); do
  budget_ok || break
  if stat_fields "$timer_pid" && record_owned "$timer_pid"; then timer_tick=${ticks[$timer_pid]}; timer_registered=1; break; fi
  IFS= read -r -N 1 -t 0.02 -u "$timerfd" timer_value; timer_read=$?
  ((timer_read>128)) || break
done
((timer_registered==1)) || { status=timer_identity_failed; exit 126; }
if [[ $fixture == identity-seams ]]; then
  # Same argv as the case's owned child, but deliberately no task token.
  ( unset SEZIKA_LINUX_TASK_TOKEN; exec /bin/sleep 10 ) &
  control_pid=$!
  control_begin_ms=$now_ms
  for ((control_try=0; control_try<10 && now_ms-control_begin_ms<1000; control_try++)); do
    budget_ok || break
    if stat_fields "$control_pid" && record_owned "$control_pid" absent && [[ $current_argv == /bin/sleep\ 10\  ]]; then
      control_tick=${ticks[$control_pid]}; control_argv=$current_argv; control_chain=$current_chain; control_ppid=${parents[$control_pid]}; control_pgid=${groups[$control_pid]}; control_sid=${sessions[$control_pid]}
      forget_control
      break
    fi
    forget_control
    pause_owned 100 || break
  done
  [[ -n $control_tick ]] || { status=control_identity_failed; exit 126; }
fi
SEZIKA_LINUX_TASK_TOKEN=$token /usr/bin/setsid /bin/bash "$0" --worker "$run" "$token" -- "$@" > "$run/payload.stdout.log" 2> "$run/payload.stderr.log" &
launcher=$!
if stat_fields "$launcher"; then launcher_tick=$tick; fi
ready_stopped=0
for ((ready_try=0; ready_try<30 && SECONDS-begin<3; ready_try++)); do
  budget_ok || break
  if [[ -s $run/ready ]] && stat_fields "$launcher" && [[ $state == T ]]; then ready_stopped=1; break; fi
  pause_owned 100 || break
done
[[ -s $run/ready && $ready_stopped == 1 ]] || { status=readiness_failed; exit 126; }
IFS=$'\t' read -r leader leader_tick leader_sid leader_group < "$run/ready"
[[ $leader =~ ^[0-9]+$ ]] || exit 126
stat_fields "$leader" || { status=readiness_failed; exit 126; }
[[ $tick == "$leader_tick" && $sid == "$leader_sid" && $pgid == "$leader_group" && $state == T ]] || exit 126
record_owned "$leader" || { status=ownership_failed; exit 126; }
: > "$run/go"
signal_owned "$leader" CONT || { status=ownership_failed; exit 126; }
phase=work
# Human-reviewed: 4*budget+16 iterations AND budget seconds, 250ms backoff.
for ((attempt=0; attempt<4*budget+16; attempt++)); do
  budget_ok || { if ((work_budget_exhausted)); then status=timed_out; exit 124; else status=clock_failed; exit 127; fi; }
  snapshot || { if ((work_budget_exhausted)); then status=timed_out; exit 124; else status=inventory_failed; exit 127; fi; }
  [[ ! -f $run/cancel.request ]] || { status=cancelled_file; exit 125; }
  if ((cancel_after > 0 && now_ms-begin_ms >= cancel_after*1000)); then
    audit cancel_requested "$$" "$supervisor_tick" "mode=$cancel_mode elapsed_ms=$((now_ms-begin_ms)) begin_ms=$begin_ms now_ms=$now_ms deadline_ms=$work_deadline_ms" || true
    if [[ $cancel_mode == signal ]]; then audit self_signal_attempt "$$" "$supervisor_tick" 'actual_builtin=kill signal=TERM syscall_rc=pending' || true; builtin kill -s TERM -- "$$"; cancel_signal_rc=$?; audit self_signal_result "$$" "$supervisor_tick" "rc=$cancel_signal_rc" || true; ((cancel_signal_rc==0)) || { status=cancel_signal_failed; exit 127; }; else : > "$run/cancel.request"; status=cancelled_file; exit 125; fi
  fi
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
  pause_owned 250 || { work_pause_reason=$pause_reason; work_pause_read_rc=$pause_read_rc; work_pause_failed_ms=$now_ms; audit pause_failed "$$" "$supervisor_tick" "reason=$work_pause_reason rc=$work_pause_read_rc begin_ms=$begin_ms now_ms=$now_ms deadline_ms=$work_deadline_ms" || true; if [[ $work_pause_reason == deadline ]] && ((work_budget_exhausted)); then status=timed_out; exit 124; else status=backoff_failed; exit 127; fi; }
done
if ! budget_ok && ((work_budget_exhausted)); then status=timed_out; exit 124; fi; status=work_loop_bound_reached; exit 127
