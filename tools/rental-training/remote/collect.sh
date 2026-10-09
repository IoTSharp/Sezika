#!/bin/bash
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh" "${1:?}"
guard_seconds=90
collection=${2:?collection identity required}; [[ $collection =~ ^[a-f0-9]{8}$ ]] || exit 64
archive="results-$collection.tar"
exec {lockfd}> "$task/stage.lock"; flock -n "$lockfd" || exit 73
record_shell "$task/logs/collect-shell.tsv"
if [[ -f $task/$archive ]]; then
  (cd "$task" && timeout 15s sha256sum -c --quiet "$archive.sha256")
  timeout 2s cat "$task/$archive.sha256"; exit 0
fi
cd "$task"
timeout 5s find logs output .artifacts -maxdepth 4 -type f -print > results-files.txt
count=0; total=0
while IFS= read -r name; do
  guard && ((count<1200)) || exit 124
  [[ $name =~ ^[A-Za-z0-9_./-]+$ && $name != *..* && ! -L $name ]] || exit 65
  size=$(timeout 1s stat -c %s "$name"); ((size<=67108864)) || exit 65
  count=$((count+1)); total=$((total+size)); ((total<=134217728)) || exit 65
done < results-files.txt
timeout 10s sha256sum -- task-identity archive-sha256 gpu-uuid package-identity.json files.sha256 > results-input-bindings.sha256
printf '%s\n' task-identity archive-sha256 gpu-uuid package-identity.json files.sha256 results-input-bindings.sha256 >> results-files.txt
timeout 15s tar -cf "$archive" -T results-files.txt
timeout 10s sha256sum "$archive" > "$archive.sha256"
printf 'results_files=%s results_payload_bytes=%s\n' "$count" "$total"
cat "$archive.sha256"
