#!/bin/bash
set -euo pipefail
umask 077
task=/root/sezika-experiments/20261009-repaired-training-8192
test "$(hostname)" = digisyn-gpu01
test "$(timeout 2s cat "$task/task-identity")" = sezika-token-limit-20261009-8192
test ! -e "$task/.artifacts/stage-train" && test ! -e "$task/logs/publication-patch"
printf '%s  %s\n' d2240bd6b7253b856af483a127bf16c0112dc7a62d332275e767bda958739acf "$task/publication-patch.tar" | timeout 3s sha256sum -c --quiet -
(( $(stat -c %s "$task/publication-patch.tar") <= 16777216 ))
timeout 2s tar -tf "$task/publication-patch.tar" > "$task/logs/publication-patch-inventory.txt"
count=0; begin=$SECONDS
while IFS= read -r name; do
 ((count<80 && SECONDS-begin<5)) || exit 124
 [[ $name == ./ || $name == ./files.sha256 || $name == ./bundle/ || $name == ./capacity/ || $name == ./regression/ || $name == ./remote/ || $name =~ ^\./(bundle|capacity|regression|remote)/[A-Za-z0-9_.-]+$ ]] || exit 65
 count=$((count+1))
done < "$task/logs/publication-patch-inventory.txt"
mkdir "$task/logs/publication-patch" "$task/logs/publication-before-fix"
timeout 2s tar -xf "$task/publication-patch.tar" -C "$task/logs/publication-patch"
(cd "$task/logs/publication-patch" && timeout 3s sha256sum -c --quiet files.sha256)
begin=$SECONDS; count=0
for group in bundle capacity regression remote; do
 ((SECONDS-begin<20)) || exit 124
 (cd "$task/$group" && timeout 5s sha256sum -c --quiet files.sha256)
 mkdir "$task/logs/publication-before-fix/$group"
 cp -- "$task/$group/files.sha256" "$task/logs/publication-before-fix/$group/files.sha256"
done
while read -r hash relative; do
 ((count<64 && SECONDS-begin<20)) || exit 124
 [[ $hash =~ ^[0-9a-f]{64}$ && $relative =~ ^(bundle|capacity|regression|remote)/[A-Za-z0-9_.-]+$ ]] || exit 65
 group=${relative%%/*}; name=${relative#*/}
 if [[ -f $task/$relative ]]; then cp -- "$task/$relative" "$task/logs/publication-before-fix/$relative"; fi
 cp -- "$task/logs/publication-patch/$relative" "$task/$relative"
 timeout 2s awk -v n="$name" '{v=$2; sub(/\r$/,"",v); if(v!=n)print}' "$task/$group/files.sha256" > "$task/$group/files.sha256.new"
 printf '%s  %s\n' "$hash" "$name" >> "$task/$group/files.sha256.new"
 mv -- "$task/$group/files.sha256.new" "$task/$group/files.sha256"
 count=$((count+1))
done < "$task/logs/publication-patch/files.sha256"
for group in bundle capacity regression remote; do
 ((SECONDS-begin<25)) || exit 124
 (cd "$task/$group" && timeout 5s sha256sum -c --quiet files.sha256)
done
chmod 700 "$task/bundle/Sezika.Tests"
timeout 2s bash -n "$task/remote/phase-common.sh"
printf 'publication_fix_files\t%s\nformal_training_started\tfalse\noriginal_failure_preserved\ttrue\n' "$count" > "$task/logs/publication-fix.tsv"
printf 'publication_fix_verified_no_formal_training_yet=true\n'
