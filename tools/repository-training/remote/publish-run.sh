#!/bin/bash
# Execute only beneath Invoke-BoundedLinux-v2.sh. Credentials remain in a RAM helper.
set -euo pipefail
umask 077
[[ $# == 11 ]] || { printf 'usage: publish-run.sh root run-id expected-data-master expected-model-master deadline-utc validation-json derived-dir trained-dir credential-helper validation-stage-tsv maximum-seconds\n' >&2; exit 64; }
root=$1; run_id=$2; expected_data=$3; expected_model=$4; lease=$5; validation=$6; derived=$7; trained=$8; helper=$9; validation_stage=${10}; maximum=${11}
[[ $root =~ ^/[A-Za-z0-9_./-]+/sezika-repository-[a-z0-9-]+$ && $root != *..* && $root == */sezika-repository-"$run_id" && $run_id =~ ^[a-z0-9][a-z0-9-]{2,63}$ ]] || exit 64
[[ $expected_data =~ ^[a-f0-9]{40}$ && $expected_model =~ ^[a-f0-9]{40}$ ]] || exit 64
[[ $maximum =~ ^[0-9]+$ ]] && ((maximum>=60 && maximum<=3600)) || exit 64
[[ $lease =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$ && -n ${SEZIKA_LINUX_TASK_TOKEN-} ]] || exit 64
for path in "$root" "$validation" "$derived" "$trained" "$helper" "$validation_stage"; do
  [[ $path =~ ^/[A-Za-z0-9_./-]+$ && $path != *..* && ! -L $path && $(timeout 5s realpath -- "$path") == "$path" ]] || exit 65
done
[[ -f $root/OWNED_BY_SEZIKA_REPOSITORY_TASK && -f $validation && -d $derived && -d $trained && -f $validation_stage && -r $helper ]] || exit 65
[[ $derived == "$root"/* && $trained == "$root"/* && $validation == "$root"/* && $helper == /dev/shm/* ]] || exit 65
credential_dir="/dev/shm/sezika-repository-$run_id"
[[ $helper == "$credential_dir/helper.sh" ]] || exit 65
for executable in git python3 timeout date realpath df sha256sum stat; do command -v "$executable" >/dev/null || exit 69; done
here=$(timeout 5s realpath -- "${BASH_SOURCE[0]}"); verifier=${here%/*}/verify-publication.py
[[ -f $verifier && ! -L $verifier ]] || exit 65
deadline=$(timeout 5s date -u -d "$lease" +%s)
begin=$SECONDS; calls=0; work="$root/publication"; logs="$work/processes"
[[ ! -e $work && ! -L $work && ! -e $root/public-readback ]] || { echo 'Publication/readback root already exists; inspect preserved receipts, never overwrite or retry writes.' >&2; exit 73; }
mkdir -- "$work"; mkdir -- "$logs"
printf '0\n' > "$work/git-call-count.txt"
export GIT_TERMINAL_PROMPT=0 GIT_LFS_SKIP_SMUDGE=1 GIT_CONFIG_NOSYSTEM=1 GIT_CONFIG_SYSTEM=/dev/null GIT_CONFIG_GLOBAL=/dev/null
unset GIT_TRACE GIT_TRACE_CURL GIT_CURL_VERBOSE GIT_ASKPASS SSH_ASKPASS SSH_AUTH_SOCK GIT_CONFIG_COUNT GIT_CONFIG_PARAMETERS GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_COMMON_DIR GIT_OBJECT_DIRECTORY GIT_ALTERNATE_OBJECT_DIRECTORIES GIT_SSH_COMMAND GIT_SSH GIT_PROXY_COMMAND HTTP_PROXY HTTPS_PROXY ALL_PROXY NO_PROXY http_proxy https_proxy all_proxy no_proxy
check() {
  local now; now=$(timeout 3s date -u +%s)
  IFS= read -r calls < "$work/git-call-count.txt"
  [[ $calls =~ ^[0-9]+$ ]] || exit 65
  ((SECONDS-begin<maximum && calls<80 && now<deadline-30)) && [[ ! -e $root/CANCEL ]] || exit 124
  remaining=$((maximum-(SECONDS-begin))); if ((deadline-now-30<remaining)); then remaining=$((deadline-now-30)); fi
  return 0
}
phase=setup
cleanup_credentials() {
  local cleanup_begin=$SECONDS marker="$credential_dir/OWNED_BY_SEZIKA_CREDENTIAL_TASK" names name count=0 path
  [[ -d $credential_dir && ! -L $credential_dir && $(timeout 3s realpath -- "$credential_dir") == "$credential_dir" && -f $marker && ! -L $marker ]] || return 1
  [[ $(timeout 3s stat -c %s -- "$marker") == ${#run_id} && $(timeout 3s cat -- "$marker") == "$run_id" ]] || return 1
  names=$(timeout 3s find "$credential_dir" -mindepth 1 -maxdepth 1 -printf '%f\n') || return 1
  while IFS= read -r name; do
    ((SECONDS-cleanup_begin<15)) || return 1
    count=$((count+1)); ((count<=4)) || return 1
    [[ $name == dataset.token || $name == model.token || $name == helper.sh || $name == OWNED_BY_SEZIKA_CREDENTIAL_TASK ]] || return 1
  done <<< "$names"
  for name in dataset.token model.token helper.sh OWNED_BY_SEZIKA_CREDENTIAL_TASK; do
    ((SECONDS-cleanup_begin<15)) || return 1
    path="$credential_dir/$name"
    if [[ -e $path || -L $path ]]; then [[ -f $path && ! -L $path ]] || return 1; fi
  done
  timeout 3s rm -f -- "$credential_dir/dataset.token" "$credential_dir/model.token" "$credential_dir/helper.sh" "$marker" || return 1
  timeout 3s rmdir -- "$credential_dir" || return 1
}
finish() {
  local code=$? cleanup_code=0
  cleanup_credentials || cleanup_code=$?
  IFS= read -r calls < "$work/git-call-count.txt"
  printf 'status=%s\nphase=%s\ncalls=%s\nelapsed_seconds=%s\ncredential_cleanup_exit=%s\n' "$code" "$phase" "$calls" "$((SECONDS-begin))" "$cleanup_code" > "$work/publish-result.txt"
  if ((cleanup_code)); then echo 'Credential task cleanup failed; preserve and verify exact ownership before recovery.' >&2; ((code==0)) && code=74; fi
  if ((code==0)); then printf 'PASS_dataset_and_model_ref_pushed_and_anonymous_complete_full_content_sha256_credentials_reclaimed\n'; fi
  trap - EXIT
  exit "$code"
}
trap finish EXIT
python_contract() {
  check; local cap=300; ((remaining<cap)) && cap=$remaining
  timeout --signal=TERM --kill-after=5s "${cap}s" python3 "$verifier" "$1" "$root" "$run_id" "$2" "$3" "$derived" "$trained" "$validation" "$4" "$cap"
}
run_git() {
  check; calls=$((calls+1)); printf '%s\n' "$calls" > "$work/git-call-count.txt"; local label=$1 auth=$2 cwd=$3; shift 3
  local cap=600; ((remaining<cap)) && cap=$remaining
  local credential=(); [[ $auth == authenticated ]] && credential=(-c "credential.helper=!/bin/bash $helper" -c credential.useHttpPath=true)
  printf 'phase=%s git_call=%s/80 budget_seconds=%s\n' "$label" "$calls" "$cap" >&2
  local log="$logs/$(printf '%02d' "$calls")-$label"; local code=0
  # Outer guardian records PID/starttick/argv/parent-chain and reclaims only this task.
  timeout --signal=TERM --kill-after=5s "${cap}s" git -C "$cwd" \
    -c credential.helper= -c core.askPass= -c http.extraHeader= -c core.autocrlf=false \
    -c lfs.standalonetransferagent= -c lfs.basictransfersonly=true -c lfs.concurrenttransfers=1 \
    -c lfs.transfer.maxretries=0 -c lfs.dialtimeout=15 -c lfs.activitytimeout=90 \
    -c http.lowSpeedLimit=1024 -c http.lowSpeedTime=45 "${credential[@]}" "$@" > "$log.stdout.log" 2> "$log.stderr.log" || code=$?
  printf 'exit=%s\n' "$code" > "$log.result.txt"
  if ((code)); then printf 'Git failed label=%s exit=%s; preserved receipt, no automatic write retry.\n' "$label" "$code" >&2; return "$code"; fi
  cat -- "$log.stdout.log"
}
stage_pass() {
  local proof=$1 field value; local status= exit_value= final= cleanup= live= unknown= unchecked= budget_exhausted= count=0
  [[ -f $proof && ! -L $proof && $(timeout 3s realpath -- "$proof") == "$proof" ]] || exit 65
  while IFS=$'\t' read -r field value; do
    check; count=$((count+1)); ((count<=64)) || exit 65
    case "$field" in status) status=$value;;payload_exit) exit_value=$value;;final_check_complete) final=$value;;cleanup_incomplete) cleanup=$value;;verified_live_count) live=$value;;unknown_count) unknown=$value;;unchecked_count) unchecked=$value;;cleanup_budget_exhausted) budget_exhausted=$value;; esac
  done < "$proof"
  [[ $status == completed && $exit_value == 0 && $final == 1 && $cleanup == 0 && $live == 0 && $unknown == 0 && $unchecked == 0 && $budget_exhausted == 0 ]] || { echo 'Training/validation guardian proof missing or failed.' >&2; exit 65; }
}
clone() {
  local kind=$1 base=$2 target=$3 url=$4
  run_git "$kind-clone" anonymous "$work" clone --depth 1 --branch master -- "$url" "$target"
  [[ $(run_git "$kind-head" anonymous "$target" rev-parse HEAD) == "$base" ]] || exit 65
  [[ $(run_git "$kind-origin" anonymous "$target" remote get-url origin) == "$url" ]] || exit 65
  [[ -z $(run_git "$kind-index" anonymous "$target" diff --cached --name-only) ]] || exit 65
  run_git "$kind-lfs-hook" anonymous "$target" lfs install --local >/dev/null
}
publish() {
  local kind=$1 base=$2 repo=$3 prefix paths_line path_count=0 commit old_remote new_remote
  phase="$kind-prepare"
  old_remote=$(run_git "$kind-remote-before" anonymous "$repo" ls-remote --heads origin refs/heads/master)
  [[ $old_remote == "$base"$'\trefs/heads/master' ]] || exit 65
  python_contract prepare "$kind" "$repo" "${published_data-$expected_data}"
  python_contract verify-owned "$kind" "$repo" "${published_data-$expected_data}"
  local paths=()
  while IFS= read -r paths_line; do
    check; path_count=$((path_count+1)); ((path_count<=1001)) || exit 65
    [[ $paths_line =~ ^[A-Za-z0-9_./-]+$ && $paths_line != *..* ]] || exit 65
    paths+=("$paths_line")
  done < "$work/stage-$kind.paths"
  ((path_count>=2)) || exit 65
  run_git "$kind-stage" authenticated "$repo" add -- "${paths[@]}" >/dev/null
  run_git "$kind-staged-paths" anonymous "$repo" diff --cached --name-only > "$work/staged-$kind.paths"
  timeout 10s python3 - "$work/stage-$kind.paths" "$work/staged-$kind.paths" <<'PY'
import pathlib,sys
a=pathlib.Path(sys.argv[1]).read_text().splitlines();b=pathlib.Path(sys.argv[2]).read_text().splitlines()
if not 2<=len(a)<=1001 or set(a)!=set(b) or len(a)!=len(set(a)) or len(b)!=len(set(b)):raise ValueError('Index has missing or foreign owned paths')
PY
  if [[ $kind == model ]]; then
    # Upstream notices retain their exact trained bytes; only these reviewed11
    # plain snapshots are exempt. Generated text/JSON still passes whitespace.
    python_contract verify-notices "$kind" "$repo" "${published_data-$expected_data}"
    local whitespace_paths=() metadata_count=0
    while IFS= read -r paths_line; do
      check; metadata_count=$((metadata_count+1)); ((metadata_count<=1001)) || exit 65
      [[ $paths_line =~ ^[A-Za-z0-9_./-]+$ && $paths_line != *..* ]] || exit 65
      whitespace_paths+=("$paths_line")
    done < "$work/whitespace-model.paths"
    ((metadata_count>=2)) || exit 65
    run_git "$kind-staged-check" anonymous "$repo" -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol diff --cached --check -- "${whitespace_paths[@]}" >/dev/null
  else
    run_git "$kind-staged-check" anonymous "$repo" -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol diff --cached --check >/dev/null
  fi
  # Explicit author identity avoids dependence on a disabled global Git config.
  run_git "$kind-commit" anonymous "$repo" -c user.name=maikebing -c user.email=maikebing@users.noreply.gitee.com commit -m "training: save $kind run $run_id" >/dev/null
  commit=$(run_git "$kind-commit-id" anonymous "$repo" rev-parse HEAD)
  [[ $commit =~ ^[a-f0-9]{40}$ ]] || exit 65
  printf '%s\n' "$commit" > "$work/publication-$kind-commit.txt"
  phase="$kind-push"
  run_git "$kind-push" authenticated "$repo" push --porcelain origin "$commit:refs/heads/master"
  new_remote=$(run_git "$kind-remote-after" anonymous "$repo" ls-remote --heads origin refs/heads/master)
  [[ $new_remote == "$commit"$'\trefs/heads/master' ]] || exit 65
  printf 'publication_%s_commit=%s\n' "$kind" "$commit"
}
readback() {
  local kind=$1 commit=$2 url=$3 target="$root/public-readback/$1"
  phase="$kind-anonymous-readback"
  clone "$kind-readback" "$commit" "$target" "$url"
  run_git "$kind-readback-lfs-full" anonymous "$target" lfs pull
  run_git "$kind-readback-lfs-objects" anonymous "$target" lfs fsck --objects >/dev/null
  python_contract readback "$kind" "$target" "$commit"
}
[[ $validation_stage == */result.tsv ]] || exit 65
stage_root=${validation_stage%/*/*}; validation_phase=${validation_stage%/result.tsv}; validation_phase=${validation_phase##*/}
[[ $stage_root == */sezika-staging-"$run_id" && $validation_phase =~ ^validation-[A-Za-z0-9_-]+-stage$ ]] || exit 65
training_phase=${validation_phase/#validation-/train-}
phase=validation-proof; stage_pass "$validation_stage"
phase=training-proof; stage_pass "$stage_root/$training_phase/result.tsv"
python_contract disk-preflight dataset "$work/dataset" "$expected_data"
clone dataset "$expected_data" "$work/dataset" https://moark.com/maikebing/public-decision-data
clone model "$expected_model" "$work/model" https://moark.com/maikebing/Sezika-mmBERT-8192-experimental
mkdir -- "$root/public-readback"
publish dataset "$expected_data" "$work/dataset"
published_data=$(cat -- "$work/publication-dataset-commit.txt")
readback dataset "$published_data" https://moark.com/maikebing/public-decision-data
publish model "$expected_model" "$work/model"
published_model=$(cat -- "$work/publication-model-commit.txt")
readback model "$published_model" https://moark.com/maikebing/Sezika-mmBERT-8192-experimental
phase=complete
