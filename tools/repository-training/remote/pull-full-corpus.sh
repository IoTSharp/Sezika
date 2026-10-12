#!/bin/bash
# Run beneath the task's bounded Linux guardian. Never pass credentials in argv.
# pull <fresh-root> <final-published-commit> <trusted-413-files.tsv> <seconds>
# verify-only <dataset-root> <trusted-files.tsv> <tsv-sha> <count> <bytes> <fresh-receipts> <seconds>
# verify-only is an offline content check; it never produces a Git/pull acceptance.
set -euo pipefail
umask 077
export LC_ALL=C GIT_TERMINAL_PROMPT=0 GIT_LFS_SKIP_SMUDGE=1
unset GIT_TRACE GIT_TRACE_PACKET GIT_TRACE_CURL GIT_CURL_VERBOSE GIT_TRACE2 GIT_TRACE2_EVENT GIT_TRACE2_PERF
unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_OBJECT_DIRECTORY GIT_ALTERNATE_OBJECT_DIRECTORIES GIT_COMMON_DIR GIT_NAMESPACE

original_payload_commit=368385973bcbd5d2c1ef6b719911d349b8b2337c
published_commit=
original_commit=d19515802d31310b4050226b51ab85c60f130d42
repository=https://moark.com/maikebing/public-decision-data
prefix=derived/full-corpus-20261012-dataset-v1
owned_sha=8227c2442ab6dfbca3f64998af021414edc0a7b8066fd4b69caa6c20fcce7d78
production_tsv_sha=1bc7753c856b283d269dc7972f182c5135862543afa2bb1f2f0740b127647f40
maximum_files=8192
maximum_calls=33000
begin=$SECONDS
calls=0
mode=${1-}
case $mode in
  pull)
    [[ $# == 5 ]] || exit 64
    root=$2; published_commit=$3; trusted=$4; maximum=$5
    [[ $published_commit =~ ^[a-f0-9]{40}$ && $published_commit != "$original_payload_commit" ]] || { echo 'require final published commit containing training-inputs/lengths.json' >&2; exit 64; }
    dataset=$root/dataset; receipts=$root
    trusted_sha=$production_tsv_sha; expected_count=413; expected_bytes=2623103071
    ;;
  verify-only)
    [[ $# == 8 ]] || exit 64
    dataset=$2; trusted=$3; trusted_sha=$4; expected_count=$5; expected_bytes=$6; receipts=$7; maximum=$8
    root=$receipts
    ;;
  *) echo 'usage: pull-full-corpus.sh pull root final-commit trusted-413-files.tsv seconds; or verify-only dataset tsv sha count bytes fresh-receipts seconds' >&2; exit 64 ;;
esac
[[ $maximum =~ ^[1-9][0-9]{0,4}$ ]] && ((maximum<=14400)) || exit 64
[[ $expected_count =~ ^[1-9][0-9]{0,3}$ ]] && ((expected_count<=maximum_files)) || exit 64
[[ $expected_bytes =~ ^[0-9]{1,12}$ ]] && ((expected_bytes<=137438953472)) || exit 64
[[ $trusted_sha =~ ^[a-f0-9]{64}$ ]] || exit 64
for dependency in timeout realpath sha256sum stat sort cmp git df tail date cat sleep mkdir; do
  ((SECONDS-begin<maximum)) || exit 124
  command -v "$dependency" >/dev/null || { echo "missing dependency: $dependency" >&2; exit 69; }
done
safe_absolute() { [[ $1 =~ ^/[A-Za-z0-9_./-]+$ && $1 != / && $1 != *..* && $1 != *//* ]]; }
safe_absolute "$root" && safe_absolute "$dataset" && safe_absolute "$trusted" || exit 64
[[ -f $trusted && ! -L $trusted && $(realpath -e -- "$trusted") == "$trusted" ]] || exit 65
[[ ! -e $receipts && ! -L $receipts ]] || { echo 'receipt/task root exists; preserve and inspect it' >&2; exit 73; }
parent=${receipts%/*}
[[ -d $parent && $(realpath -e -- "$parent") == "$parent" ]] || exit 65
if [[ $mode == verify-only ]]; then
  [[ -d $dataset && ! -L $dataset && $(realpath -e -- "$dataset") == "$dataset" ]] || exit 65
fi
mkdir -- "$receipts"
printf 'sezika-pinned-corpus-pull-v1\n' > "$receipts/OWNED_BY_SEZIKA_PINNED_CORPUS_PULL"
cancel=${SEZIKA_PULL_CANCEL_FILE:-$receipts/CANCEL}
safe_absolute "$cancel" || exit 64
printf 'pid=%s parent_pid=%s start_utc=%s\n' "$$" "$PPID" "$(date -u +%FT%TZ)" > "$receipts/processes.tsv"
printf 'script_argv=' >> "$receipts/processes.tsv"
printf '%q ' "$0" "$@" >> "$receipts/processes.tsv"
printf '\n' >> "$receipts/processes.tsv"
[[ -r /proc/$$/stat ]] && cat /proc/$$/stat >> "$receipts/processes.tsv"
active_pid=0; active_start=; active_parent=; active_group=; active_argv_sha256=
cleanup_identity_status=NOT_NEEDED

identity() {
  local raw tail
  [[ -r /proc/$1/stat ]] || return 1
  IFS= read -r raw < "/proc/$1/stat" || return 1
  tail=${raw##*) }
  local -a fields
  read -r -a fields <<< "$tail"
  [[ ${#fields[@]} -ge 20 ]] || return 1
  # The fields after comm start at state (3); starttime is field 22.
  id_state=${fields[0]}; id_parent=${fields[1]}; id_group=${fields[2]}; id_start=${fields[19]}
}
argv_identity() {
  local result
  [[ -r /proc/$1/cmdline ]] || return 1
  result=$(timeout --signal=TERM --kill-after=1s 1s sha256sum -- "/proc/$1/cmdline") || return 1
  id_argv_sha256=${result%% *}
  [[ $id_argv_sha256 =~ ^[a-f0-9]{64}$ && $id_argv_sha256 != e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 ]] || return 1
}
# The kill authority is the exact chain timeout -> this task -> its guardian
# parent. Hash ancestors' complete NUL-delimited argv without logging secrets.
identity "$$" && [[ $id_state != Z && $id_parent == "$PPID" ]] || exit 70
task_pid=$$; task_start=$id_start; task_parent=$id_parent
argv_identity "$task_pid" || exit 70
task_argv_sha256=$id_argv_sha256
identity "$task_parent" && [[ $id_state != Z ]] || exit 70
guardian_pid=$task_parent; guardian_start=$id_start; guardian_parent=$id_parent
argv_identity "$guardian_pid" || exit 70
guardian_argv_sha256=$id_argv_sha256
printf 'task_pid=%s parent_pid=%s start_ticks=%s argv_sha256=%s\n' "$task_pid" "$task_parent" "$task_start" "$task_argv_sha256" >> "$receipts/processes.tsv"
printf 'guardian_pid=%s parent_pid=%s start_ticks=%s argv_sha256=%s\n' "$guardian_pid" "$guardian_parent" "$guardian_start" "$guardian_argv_sha256" >> "$receipts/processes.tsv"
same_task_chain() {
  identity "$task_pid" && [[ $id_state != Z && $id_start == "$task_start" && $id_parent == "$task_parent" ]] || return 1
  argv_identity "$task_pid" && [[ $id_argv_sha256 == "$task_argv_sha256" ]] || return 1
  identity "$guardian_pid" && [[ $id_state != Z && $id_start == "$guardian_start" && $id_parent == "$guardian_parent" ]] || return 1
  argv_identity "$guardian_pid" && [[ $id_argv_sha256 == "$guardian_argv_sha256" ]] || return 1
}
same_active() {
  [[ -n $active_argv_sha256 ]] && same_task_chain || return 1
  identity "$active_pid" && [[ $id_state != Z && $id_start == "$active_start" && $id_parent == "$active_parent" && $id_parent == "$task_pid" && $id_group == "$active_group" && $id_group == "$active_pid" ]] || return 1
  argv_identity "$active_pid" && [[ $id_argv_sha256 == "$active_argv_sha256" ]] || return 1
  # Re-read start/state after argv hashing to reject exit or PID reuse races.
  identity "$active_pid" && [[ $id_state != Z && $id_start == "$active_start" && $id_parent == "$active_parent" && $id_group == "$active_group" && $id_group == "$active_pid" ]]
}
stop_active() {
  ((active_pid>0)) || return 0
  if same_active; then
    printf 'cleanup_signal=TERM pid=%s argv_sha256=%s parent_chain=verified\n' "$active_pid" "$active_argv_sha256" >> "$receipts/processes.tsv"
    kill -TERM -- "-$active_pid" 2>/dev/null || :
    sleep 1
    if same_active; then
      printf 'cleanup_signal=KILL pid=%s argv_sha256=%s parent_chain=verified\n' "$active_pid" "$active_argv_sha256" >> "$receipts/processes.tsv"
      kill -KILL -- "-$active_pid" 2>/dev/null || :
    fi
  fi
  # Unknown/changed live identities are handed to the outer guardian; never
  # wait indefinitely or signal a PID whose full ownership cannot be proved.
  if identity "$active_pid" && [[ $id_state != Z ]]; then
    cleanup_identity_status=REFUSED_live_identity_or_cleanup_pending
    printf 'cleanup_identity=%s pid=%s action=outer_guardian_required\n' "$cleanup_identity_status" "$active_pid" >> "$receipts/processes.tsv"
    active_pid=0
    return 0
  fi
  cleanup_identity_status=PASS_observed_exited
  wait "$active_pid" 2>/dev/null || :
  active_pid=0
}
on_exit() {
  local code=$?
  trap - EXIT INT TERM HUP
  stop_active
  printf 'exit=%s elapsed_seconds=%s calls=%s pull_mode=%s cleanup_identity=%s\n' "$code" "$((SECONDS-begin))" "$calls" "$mode" "$cleanup_identity_status" > "$receipts/exit.txt"
  exit "$code"
}
trap on_exit EXIT
trap 'exit 130' INT
trap 'exit 143' TERM HUP
check() {
  ((SECONDS-begin<maximum && calls<maximum_calls)) && [[ ! -e $cancel && ! -L $cancel ]] || {
    echo 'wall-clock/call bound or cancellation reached' >&2; exit 124;
  }
}
run() {
  local label=$1 output=$2
  shift 2
  check; calls=$((calls+1))
  local remaining=$((maximum-(SECONDS-begin))) code=0
  printf 'progress=%s calls=%s elapsed_seconds=%s remaining_seconds=%s\n' "$label" "$calls" "$((SECONDS-begin))" "$remaining"
  printf 'call=%s label=%s argv=' "$calls" "$label" >> "$receipts/processes.tsv"
  printf '%q ' "$@" >> "$receipts/processes.tsv"
  printf '\n' >> "$receipts/processes.tsv"
  # Raw network diagnostics are suppressed: signed LFS URLs can contain secrets.
  # GNU timeout owns a new process group and bounds its complete ordinary tree.
  timeout --signal=TERM --kill-after=5s "${remaining}s" "$@" > "$output" 2>/dev/null &
  active_pid=$!
  if identity "$active_pid"; then
    active_start=$id_start; active_parent=$id_parent; active_group=$id_group
    # timeout switches to its own group immediately after launch; allow one tick.
    if [[ $active_group != "$active_pid" ]]; then
      sleep 0.05
      if identity "$active_pid"; then active_group=$id_group; fi
    fi
    active_argv_sha256=
    if [[ $id_state != Z ]] && argv_identity "$active_pid"; then active_argv_sha256=$id_argv_sha256; fi
    printf 'pid=%s parent_pid=%s start_ticks=%s group=%s argv_sha256=%s\n' "$active_pid" "$active_parent" "$active_start" "$active_group" "$active_argv_sha256" >> "$receipts/processes.tsv"
  else
    active_start=; active_parent=; active_group=; active_argv_sha256=
    printf 'pid=%s completed_before_observation=true\n' "$active_pid" >> "$receipts/processes.tsv"
  fi
  # At most 20*(maximum+6) ticks, additionally limited by the global deadline.
  for ((tick=0; tick<20*(maximum+6); tick++)); do
    if ! identity "$active_pid" || [[ $id_state == Z ]]; then break; fi
    if [[ -e $cancel || -L $cancel ]] || ((SECONDS-begin>=maximum)); then
      stop_active; return 124
    fi
    sleep 0.05
    if ((tick>0 && tick%600==0)); then printf 'working=%s elapsed_seconds=%s\n' "$label" "$((SECONDS-begin))"; fi
    # The fixed 20 Hz tick count is also bounded by remaining wall-clock time.
    if ((tick>=20*remaining)); then stop_active; return 124; fi
  done
  wait "$active_pid" || code=$?
  active_pid=0
  printf 'call=%s label=%s exit=%s\n' "$calls" "$label" "$code" >> "$receipts/processes.tsv"
  ((code==0)) || { echo "$label failed (exit $code); original receipts retained" >&2; return "$code"; }
}
git_run() {
  local label=$1 output=$2; shift 2
  run "$label" "$output" git -c core.autocrlf=false -c core.hooksPath=/dev/null -c core.fsmonitor=false \
    -c filter.lfs.process='git-lfs filter-process' -c filter.lfs.smudge='git-lfs smudge -- %f' \
    -c filter.lfs.clean='git-lfs clean -- %f' -c filter.lfs.required=true \
    -c lfs.storage="$dataset/.git/lfs" -c lfs.standalonetransferagent= -c lfs.basictransfersonly=true -c lfs.concurrenttransfers=1 \
    -c lfs.transfer.maxretries=0 -c lfs.dialtimeout=15 -c lfs.activitytimeout=90 \
    -c http.lowSpeedLimit=1024 -c http.lowSpeedTime=60 "$@"
}
run manifest-sha "$receipts/manifest-sha.txt" sha256sum -- "$trusted"
read -r actual_sha _ < "$receipts/manifest-sha.txt"
[[ $actual_sha == "$trusted_sha" ]] || { echo 'trusted TSV SHA mismatch' >&2; exit 65; }
[[ $(stat -c %s -- "$trusted") -le 2097152 ]] || exit 65
declare -A seen=()
count=0; total=0
while IFS=$'\t' read -r sha bytes relative extra; do
  check; count=$((count+1)); ((count<=maximum_files)) || exit 65
  [[ -z $extra && $sha =~ ^[a-f0-9]{64}$ && $bytes =~ ^(0|[1-9][0-9]{0,11})$ ]] || exit 65
  [[ $relative == "$prefix/"* && $relative =~ ^[A-Za-z0-9_./-]+$ && $relative != *..* && $relative != *//* && $relative != */ ]] || exit 65
  [[ ! ${seen[$relative]+yes} ]] || { echo 'duplicate TSV path' >&2; exit 65; }
  seen[$relative]=$sha
  total=$((total+bytes)); ((total<=137438953472)) || exit 65
  printf '%s\n' "$relative" >> "$receipts/expected-paths.txt"
done < "$trusted"
[[ $count == "$expected_count" && $total == "$expected_bytes" ]] || { echo 'TSV count/total mismatch' >&2; exit 65; }
if [[ $mode == pull ]]; then
  # The original 410-file payload is immutable; the final commit adds three
  # small SHA-bound training inputs beside the corpus, including this receipt.
  length_relative=$prefix/training-inputs/lengths.json
  length_sha=b79247245feb6644f179dd248fcf321d4719e010d3004fcc73c575b0258a4a73
  [[ ${seen[$length_relative]} == "$length_sha" ]] || exit 65
  run disk "$receipts/disk.txt" df -B1 --output=avail "$parent"
  available=$(tail -n 1 "$receipts/disk.txt"); available=${available//[[:space:]]/}
  [[ $available =~ ^[0-9]+$ ]] && ((available>=2*expected_bytes+1073741824)) || { echo 'need content + independent LFS cache + 1 GiB free' >&2; exit 75; }
  git_run lfs-version "$receipts/lfs-version.txt" lfs version
  git_run init /dev/null init --template= "$dataset"
  git_run remote /dev/null -C "$dataset" remote add origin "$repository"
  # Fetch only the exact published commit; no branch fallback or implicit latest.
  git_run fetch /dev/null -C "$dataset" fetch --no-tags --depth=1 --filter=blob:none origin "$published_commit"
  git_run lfsconfig-tree "$receipts/lfsconfig-tree.txt" -C "$dataset" ls-tree --full-tree "$published_commit" -- .lfsconfig
  [[ ! -s $receipts/lfsconfig-tree.txt ]] || { echo 'unexpected .lfsconfig in pinned commit' >&2; exit 65; }
  git_run sparse-init /dev/null -C "$dataset" sparse-checkout init --no-cone
  git_run sparse-set /dev/null -C "$dataset" sparse-checkout set --no-cone "/$prefix/"
  git_run checkout /dev/null -C "$dataset" checkout --detach "$published_commit"
  git_run head "$receipts/head.txt" -C "$dataset" rev-parse HEAD
  read -r actual_commit < "$receipts/head.txt"
  [[ $actual_commit == "$published_commit" ]] || exit 65
  # Do not let a data-supplied LFS endpoint or custom transfer agent run.
  [[ ! -e $dataset/.lfsconfig && ! -L $dataset/.lfsconfig ]] || { echo 'unexpected data-supplied .lfsconfig' >&2; exit 65; }
  git_run tree "$receipts/tree.txt" -C "$dataset" ls-tree -r --full-tree "$published_commit" -- "$prefix/"
  tree_count=0
  while IFS=$'\t' read -r identity_fields tree_path; do
    check; tree_count=$((tree_count+1)); ((tree_count<=maximum_files)) || exit 65
    [[ $identity_fields =~ ^100644\ blob\ [a-f0-9]{40}$ && $tree_path =~ ^[A-Za-z0-9_./-]+$ && $tree_path == "$prefix/"* && $tree_path != *..* && $tree_path != *//* ]] || exit 65
    [[ ${seen[$tree_path]+yes} ]] || exit 65
    printf '%s\n' "$tree_path" >> "$receipts/actual-paths.txt"
  done < "$receipts/tree.txt"
  [[ $tree_count == "$expected_count" ]] || exit 65
  run sort-expected "$receipts/expected-sorted.txt" sort "$receipts/expected-paths.txt"
  run sort-tree "$receipts/actual-sorted.txt" sort "$receipts/actual-paths.txt"
  run compare-tree /dev/null cmp "$receipts/expected-sorted.txt" "$receipts/actual-sorted.txt"
  git_run lfs-pull /dev/null -C "$dataset" lfs pull --include="$prefix/**" --exclude=
fi
count=0; total=0
while IFS=$'\t' read -r sha bytes relative extra; do
  check; count=$((count+1)); ((count<=maximum_files)) || exit 65
  asset=$dataset/$relative
  [[ -f $asset && ! -L $asset && $(realpath -e -- "$asset") == "$asset" ]] || { echo 'missing/linked asset' >&2; exit 65; }
  [[ $(stat -c %s -- "$asset") == "$bytes" ]] || { echo "asset size mismatch: $relative" >&2; exit 65; }
  run "sha-$count" "$receipts/last-sha.txt" sha256sum -- "$asset"
  read -r actual_sha _ < "$receipts/last-sha.txt"
  [[ $actual_sha == "$sha" && $(stat -c %s -- "$asset") == "$bytes" ]] || { echo "asset SHA/size mismatch: $relative" >&2; exit 65; }
  total=$((total+bytes))
  printf '%s\t%s\t%s\n' "$sha" "$bytes" "$relative" >> "$receipts/verified-files.tsv"
  if ((count%25==0 || count==expected_count)); then printf 'verified_files=%s/%s verified_bytes=%s/%s\n' "$count" "$expected_count" "$total" "$expected_bytes"; fi
done < "$trusted"
[[ $count == "$expected_count" && $total == "$expected_bytes" ]] || exit 65
if [[ $mode == pull ]]; then
  length_path=$dataset/$length_relative
  [[ -f $length_path && ! -L $length_path && $(realpath -e -- "$length_path") == "$length_path" && $(stat -c %s -- "$length_path") == 1688 ]] || exit 65
  run lengths-sha "$receipts/lengths-sha.txt" sha256sum -- "$length_path"
  read -r actual_sha _ < "$receipts/lengths-sha.txt"
  [[ $actual_sha == "$length_sha" ]] || { echo 'training lengths receipt SHA mismatch' >&2; exit 65; }
  # These fixed identities supplement complete TSV verification; they never edit provenance.
  [[ ${seen[$prefix/corpus/manifest.json]} == 9fdc4e15499c9b08f42d4afea3800e53af1f0ade22b4458600e323f196ab7269 ]]
  [[ ${seen[$prefix/corpus/corpus-bindings.json]} == 6312798f19081dd29f01d627a446cdf500a4136a55f6750d46aa85d68554ac16 ]]
  [[ ${seen[$prefix/corpus/source-scope-policy.json]} == 57dfc53a758ad32d7ebcc0cb3b426a80e7da40e6f24be01d1bf7bb13b31655ed ]]
  [[ ${seen[$prefix/corpus/length-scan-complete.json]} == 2c29156eabe1c651473172f5cd03adf8d1d4437cf1bedbbeaaf1d3b10d199e8f ]]
  git_run status "$receipts/owned-status.txt" -C "$dataset" status --porcelain --untracked-files=all -- "$prefix/"
  [[ ! -s $receipts/owned-status.txt ]] || { echo 'owned checkout changed' >&2; exit 65; }
  status=PASS_pinned_published_content; content_only=false; pull_status=PASS
else
  status=PASS_offline_content_only; content_only=true; pull_status=NOT_RUN
fi
cat > "$receipts/published-dataset-binding.json" <<JSON
{"SchemaVersion":1,"Status":"$status","Mode":"$mode","ContentOnly":$content_only,"PullStatus":"$pull_status","TrainingReady":false,"TrainingStatus":"NOT_RUN","Repository":"$repository","VisibilityContract":"private","AuthenticationSource":"caller-protected-git-helper-or-memory","PublishedDatasetCommit":"$published_commit","OriginalDatasetCommit":"$original_commit","OriginalPayloadCommit":"$original_payload_commit","CorpusRoot":"$dataset/$prefix/corpus","LengthReportPath":"$dataset/$prefix/training-inputs/lengths.json","OwnedManifestSha256":"$owned_sha","FilesTsvSha256":"$trusted_sha","Files":$count,"Bytes":$total,"CorpusLengthScanSha256":"2c29156eabe1c651473172f5cd03adf8d1d4437cf1bedbbeaaf1d3b10d199e8f","LengthReportContentStatus":"$pull_status","LengthReportSha256":"b79247245feb6644f179dd248fcf321d4719e010d3004fcc73c575b0258a4a73"}
JSON
printf 'status=%s training=NOT_RUN receipts=%s\n' "$status" "$receipts"
