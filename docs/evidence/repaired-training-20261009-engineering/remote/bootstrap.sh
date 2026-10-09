#!/bin/bash
set -euo pipefail
umask 077
task=/root/sezika-experiments/20261009-repaired-training-v3
old=/root/sezika-experiments/20261008-real-head-v1
begin=$SECONDS
test "$(hostname)" = digisyn-gpu01
test -d /root/sezika-experiments && test ! -L /root/sezika-experiments
test ! -L "$task" && test ! -L "$old"
test "$(timeout 2s cat "$old/task-identity")" = sezika-real-gpu-head-experiment-20261008-v1
if [[ ! -e $task ]]; then
  mkdir "$task"
  printf '%s\n' sezika-repaired-training-20261009 > "$task/task-identity"
  printf '%s\n' 01a11f25-335a-7571-a735-7171ace8c3b9 > "$task/thread-identity"
  mkdir "$task/logs" "$task/output" "$task/.artifacts"
  timeout 3s ps -p "$$" -p "$PPID" -o pid=,ppid=,lstart=,args= > "$task/logs/bootstrap-create.tsv"
  printf 'new_task_created_upload_required\n'
  exit 0
fi
test "$(timeout 2s cat "$task/task-identity")" = sezika-repaired-training-20261009
test "$(timeout 2s cat "$task/thread-identity")" = 01a11f25-335a-7571-a735-7171ace8c3b9
test ! -e "$task/cancel.request"
test ! -L "$task/stage.lock"
exec {lockfd}> "$task/stage.lock"
/usr/bin/flock -n "$lockfd" || exit 73
if [[ -e $task/logs/install-complete.tsv ]]; then
  timeout 2s cat "$task/logs/install-complete.tsv"
  printf 'already_installed_no_repeat\n'
  exit 73
fi
test -f "$task/upload.tar" && test ! -L "$task/upload.tar"
test -f "$task/upload.sha256" && test ! -L "$task/upload.sha256"
(( $(stat -c %s "$task/upload.tar") <= 268435456 ))
cd "$task"
timeout --signal=TERM --kill-after=1s 15s sha256sum -c --quiet upload.sha256
test ! -e "$task/upload-inventory.txt"
timeout --signal=TERM --kill-after=1s 8s tar -tvf upload.tar > upload-inventory.txt
rows=0; list_begin=$SECONDS
while read -r mode owner size date time name; do
  ((rows<700 && SECONDS-list_begin<10 && SECONDS-begin<65)) || exit 124
  test ! -e "$task/cancel.request"
  [[ $name != /* && $name != *..* && $name != *\\* && $name != *' '* ]]
  if [[ $mode == d* ]]; then [[ $name == ./ || $name == ./bundle/ || $name == ./capacity/ || $name == ./data/ || $name == ./data/repair-evidence/ || $name == ./remote/ || $name == ./regression/ || $name == ./regression_input/ ]]
  else
    [[ $mode == -* && $size =~ ^[0-9]+$ ]]
    [[ $name == ./bundle/* || $name == ./capacity/* || $name == ./data/* || $name == ./remote/* || $name == ./regression/* || $name == ./regression_input/* ]]
    leaf=${name#./}; leaf=${leaf#*/}; [[ -n $leaf && ( $leaf != */* || ( $name == ./data/repair-evidence/* && ${leaf#*/} != */* ) ) ]]
  fi
  rows=$((rows+1))
done < upload-inventory.txt
((rows>10))
for directory in bundle capacity data data/repair-evidence remote regression regression_input; do
  ((SECONDS-begin<65)) || exit 124
  test ! -e "$task/$directory" && test ! -L "$task/$directory"
done
timeout --signal=TERM --kill-after=1s 15s tar -xf upload.tar
for directory in bundle capacity data data/repair-evidence remote regression regression_input; do
  ((SECONDS-begin<65)) || exit 124
  test -d "$task/$directory" && test ! -L "$task/$directory"
  (cd "$task/$directory" && timeout 5s sha256sum -c --quiet files.sha256)
done
printf '%s  %s\n' 32167e8cf7396ac6d1f8f54a8d664dfbfaaef71fccba871433b0fdf73e24f2f1 "$task/remote/Invoke-BoundedLinux.sh" | timeout 2s sha256sum -c --quiet -
chmod 700 "$task/bundle/Sezika.ExperimentTrainer" "$task/capacity/CapacityCheck" "$task/regression/RegressionCheck"
count=0; syntax_begin=$SECONDS
for name in Invoke-BoundedLinux.sh bootstrap.sh runner-probe.sh phase-common.sh capacity.sh train.sh validate.sh regress.sh final-evidence.sh; do
  ((count<9 && SECONDS-syntax_begin<12)) || exit 124
  test ! -e "$task/cancel.request"
  timeout 1s bash -n "$task/remote/$name"
  count=$((count+1))
done
printf 'status\tinstalled\nfiles_checked\t%s\nrunner_sha256\t%s\nelapsed_seconds\t%s\n' "$rows" 32167e8cf7396ac6d1f8f54a8d664dfbfaaef71fccba871433b0fdf73e24f2f1 "$((SECONDS-begin))" > "$task/logs/install-complete.tsv"
printf 'bundle_capacity_data_installed_no_training\n'
