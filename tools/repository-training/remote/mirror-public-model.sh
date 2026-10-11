#!/bin/bash
# Run only under the owned Linux guardian. This tool mirrors an existing public
# model target from explicitly authorized authenticated private model assets;
# it does not create repositories or change visibility.
set -euo pipefail
umask 077
[[ $# == 11 ]] || { printf 'usage: mirror-public-model.sh root run-id verified-source-checkout source-commit expected-target-master deadline-utc credential-helper validation-json trained-dir maximum-seconds target-url\n' >&2; exit 64; }
root=$1; run_id=$2; source=$3; source_commit=$4; target_base=$5; lease=$6; helper=$7; validation=$8; trained=$9; maximum=${10}; target_url=${11}
source_url=https://moark.com/maikebing/Sezika-mmBERT-8192-experimental
[[ $root =~ ^/[A-Za-z0-9_./-]+/sezika-repository-[a-z0-9-]+$ && $root != *..* && $root == */sezika-repository-"$run_id" && $run_id =~ ^[a-z0-9][a-z0-9-]{2,63}$ ]] || exit 64
[[ $source_commit =~ ^[a-f0-9]{40}$ && $target_base =~ ^[a-f0-9]{40}$ && $target_url == https://moark.com/IoTSharp/Sezika-Decision-Base ]] || exit 64
[[ $maximum =~ ^[0-9]+$ ]] && ((maximum>=60 && maximum<=1800)) || exit 64
[[ $lease =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$ && -n ${SEZIKA_LINUX_TASK_TOKEN-} ]] || exit 64
for path in "$root" "$source" "$helper" "$validation" "$trained"; do
  [[ $path =~ ^/[A-Za-z0-9_./-]+$ && $path != *..* && ! -L $path && $(timeout 5s realpath -- "$path") == "$path" ]] || exit 65
done
[[ -f $root/OWNED_BY_SEZIKA_REPOSITORY_TASK && -d $source && -d $trained && -f $validation && $source == "$root"/* && $trained == "$root"/* && $validation == "$root"/* ]] || exit 65
credential_dir="/dev/shm/sezika-repository-$run_id"
[[ $helper == "$credential_dir/helper.sh" && -f $helper && ! -L $helper && $(timeout 3s stat -f -c %T -- /dev/shm) == tmpfs ]] || exit 65
for executable in git python3 timeout date realpath stat find rm rmdir; do command -v "$executable" >/dev/null || exit 69; done
here=$(timeout 5s realpath -- "${BASH_SOURCE[0]}"); verifier=${here%/*}/verify-publication.py
[[ -f $verifier && ! -L $verifier ]] || exit 65
deadline=$(timeout 5s date -u -d "$lease" +%s); begin=$SECONDS; phase=setup
work="$root/org-model-mirror"; target="$work/checkout"; readback="$work/anonymous-readback"
[[ ! -e $work && ! -L $work ]] || { echo 'Mirror task already exists; inspect preserved commit/ref/receipts before recovery.' >&2; exit 73; }
mkdir -- "$work"; mkdir -- "$work/processes"
printf '0\n' > "$work/git-call-count.txt"
export GIT_TERMINAL_PROMPT=0 GIT_LFS_SKIP_SMUDGE=1 GIT_CONFIG_NOSYSTEM=1 GIT_CONFIG_SYSTEM=/dev/null GIT_CONFIG_GLOBAL=/dev/null
unset GIT_TRACE GIT_TRACE_CURL GIT_CURL_VERBOSE GIT_ASKPASS SSH_ASKPASS SSH_AUTH_SOCK GIT_CONFIG_COUNT GIT_CONFIG_PARAMETERS GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_COMMON_DIR GIT_OBJECT_DIRECTORY GIT_ALTERNATE_OBJECT_DIRECTORIES GIT_SSH_COMMAND GIT_SSH GIT_PROXY_COMMAND HTTP_PROXY HTTPS_PROXY ALL_PROXY NO_PROXY http_proxy https_proxy all_proxy no_proxy
check() {
  local now; now=$(timeout 3s date -u +%s)
  IFS= read -r calls < "$work/git-call-count.txt"
  [[ $calls =~ ^[0-9]+$ ]] || exit 65
  ((SECONDS-begin<maximum && calls<60 && now<deadline-30)) && [[ ! -e $root/CANCEL ]] || exit 124
  remaining=$((maximum-(SECONDS-begin))); if ((deadline-now-30<remaining)); then remaining=$((deadline-now-30)); fi
}
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
  printf 'status=%s\nphase=%s\ngit_calls=%s\nelapsed_seconds=%s\ncredential_cleanup_exit=%s\nsource_model_commit=%s\nsource_visibility=private\ntarget_visibility=public\ntarget_before_commit=%s\nprivate_dataset_included=false\n' "$code" "$phase" "$calls" "$((SECONDS-begin))" "$cleanup_code" "$source_commit" "$target_base" > "$work/mirror-result.txt"
  if ((cleanup_code)); then echo 'Exact RAM credential cleanup failed; verify ownership before recovery.' >&2; ((code==0)) && code=74; fi
  if ((code==0)); then printf 'PASS_public_model_base18_and_complete_run_pushed_and_anonymous_full_bytes_SHA_verified\n'; fi
  trap - EXIT
  exit "$code"
}
trap finish EXIT
run_git() {
  check; calls=$((calls+1)); printf '%s\n' "$calls" > "$work/git-call-count.txt"
  local label=$1 auth=$2 cwd=$3; shift 3
  local cap=600 code=0 credential=(); ((remaining<cap)) && cap=$remaining
  [[ $auth == authenticated ]] && credential=(-c "credential.helper=!/bin/bash $helper" -c credential.useHttpPath=true)
  printf 'phase=%s git_call=%s/60 budget_seconds=%s\n' "$label" "$calls" "$cap" >&2
  local log="$work/processes/$(printf '%02d' "$calls")-$label"
  timeout --signal=TERM --kill-after=5s "${cap}s" git -C "$cwd" \
    -c credential.helper= -c core.askPass= -c http.extraHeader= -c core.autocrlf=false \
    -c lfs.standalonetransferagent= -c lfs.basictransfersonly=true -c lfs.concurrenttransfers=1 \
    -c lfs.transfer.maxretries=0 -c lfs.dialtimeout=15 -c lfs.activitytimeout=90 \
    -c http.lowSpeedLimit=1024 -c http.lowSpeedTime=45 "${credential[@]}" "$@" > "$log.stdout.log" 2> "$log.stderr.log" || code=$?
  printf 'exit=%s\n' "$code" > "$log.result.txt"
  if ((code)); then printf 'Mirror Git failed label=%s exit=%s; no force, hook bypass, repository creation or automatic write retry.\n' "$label" "$code" >&2; return "$code"; fi
  cat -- "$log.stdout.log"
}
contract() {
  check; local cap=300; ((remaining<cap)) && cap=$remaining
  timeout --signal=TERM --kill-after=5s "${cap}s" python3 "$verifier" "$1" "$root" "$run_id" model "$2" "$source" "$trained" "$validation" "$source_commit" "$cap"
}
load_paths() {
  local path_file=$1 value count=0
  loaded_paths=()
  while IFS= read -r value; do
    check; count=$((count+1)); ((count<=1001)) || exit 65
    [[ $value =~ ^[A-Za-z0-9_./-]+$ && $value != *..* ]] || exit 65
    loaded_paths+=("$value")
  done < "$path_file"
  ((count>=1)) || exit 65
}
phase=source-private-model-authenticated-proof
[[ $(run_git source-head anonymous "$source" rev-parse HEAD) == "$source_commit" ]] || exit 65
[[ $(run_git source-origin anonymous "$source" remote get-url origin) == "$source_url" ]] || exit 65
[[ -z $(run_git source-clean anonymous "$source" status --porcelain=v1 --untracked-files=all) ]] || exit 65
[[ $(run_git source-remote authenticated "$source" ls-remote --heads origin refs/heads/master) == "$source_commit"$'\trefs/heads/master' ]] || exit 65
phase=target-existing-public-proof
[[ $(run_git target-remote-before anonymous "$work" ls-remote --heads "$target_url" refs/heads/master) == "$target_base"$'\trefs/heads/master' ]] || exit 65
run_git target-clone anonymous "$work" clone --depth 1 --branch master -- "$target_url" "$target"
[[ $(run_git target-head anonymous "$target" rev-parse HEAD) == "$target_base" ]] || exit 65
[[ $(run_git target-origin anonymous "$target" remote get-url origin) == "$target_url" ]] || exit 65
[[ -z $(run_git target-clean anonymous "$target" status --porcelain=v1 --untracked-files=all) ]] || exit 65
run_git target-lfs-hook anonymous "$target" lfs install --local >/dev/null
run_git target-before-paths anonymous "$target" ls-files > "$work/target-before.paths"
printf '%s\n' "$target_base" > "$work/target-before-commit.txt"
phase=prepare-complete-public-model
contract mirror-prepare "$target"
load_paths "$work/stage.paths"
run_git mirror-stage anonymous "$target" add -- "${loaded_paths[@]}" >/dev/null
run_git mirror-staged-paths anonymous "$target" diff --cached --name-only > "$work/staged.paths"
timeout 10s python3 - "$work/stage.paths" "$work/staged.paths" "$run_id" <<'PY'
import pathlib,sys
a=pathlib.Path(sys.argv[1]).read_text().splitlines(); b=pathlib.Path(sys.argv[2]).read_text().splitlines(); prefix='runs/'+sys.argv[3]+'/'
required={p for p in a if p.startswith(prefix)}|{'.gitattributes'}
if not 2<=len(a)<=1001 or len(a)!=len(set(a)) or len(b)!=len(set(b)) or not required<=set(b)<=set(a):raise ValueError('Mirror index missing full new run/attributes or contains foreign paths')
PY
contract mirror-verify "$target"
load_paths "$work/whitespace.paths"
run_git mirror-staged-check anonymous "$target" -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol diff --cached --check -- "${loaded_paths[@]}" >/dev/null
run_git mirror-lfs-objects anonymous "$target" lfs fsck --objects >/dev/null
run_git mirror-commit anonymous "$target" -c user.name=maikebing -c user.email=maikebing@users.noreply.gitee.com commit -m "model: mirror complete public training run $run_id" >/dev/null
commit=$(run_git mirror-commit-id anonymous "$target" rev-parse HEAD)
[[ $commit =~ ^[a-f0-9]{40}$ ]] || exit 65
printf '%s\n' "$commit" > "$work/publication-target-commit.txt"
phase=ordinary-lfs-upload
# Explicit owned OIDs include the original encoder/tokenizer even if an old
# target pointer is unchanged. Ordinary basic upload/verify keeps pre-push intact.
object_count=0
while IFS= read -r oid; do
  check; object_count=$((object_count+1)); ((object_count<=20)) || exit 65
  [[ $oid =~ ^[a-f0-9]{64}$ ]] || exit 65
  run_git "mirror-lfs-object-$object_count" authenticated "$target" lfs push --object-id origin "$oid" >/dev/null
done < "$work/lfs-object-ids.paths"
((object_count>=1)) || exit 65
phase=ordinary-git-push
[[ $(run_git target-remote-before-push anonymous "$target" ls-remote --heads origin refs/heads/master) == "$target_base"$'\trefs/heads/master' ]] || exit 65
run_git mirror-push authenticated "$target" push --porcelain origin "$commit:refs/heads/master"
[[ $(run_git target-remote-after anonymous "$target" ls-remote --heads origin refs/heads/master) == "$commit"$'\trefs/heads/master' ]] || exit 65
printf '%s\n' "$commit" > "$work/target-after-commit.txt"
phase=verified-owned-target-lfs-cache-reclamation
contract mirror-prune-lfs "$target"
phase=independent-anonymous-full-readback
run_git readback-clone anonymous "$work" clone --depth 1 --branch master -- "$target_url" "$readback"
[[ $(run_git readback-head anonymous "$readback" rev-parse HEAD) == "$commit" ]] || exit 65
run_git readback-lfs-hook anonymous "$readback" lfs install --local >/dev/null
run_git readback-lfs-full anonymous "$readback" lfs pull
run_git readback-lfs-objects anonymous "$readback" lfs fsck --objects >/dev/null
contract mirror-readback "$readback"
phase=complete
