#!/bin/bash
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh" "${1:?}"
guard_seconds=180
expected=${2:?}; [[ $expected =~ ^[a-f0-9]{64}$ ]] || exit 64
exec {lockfd}> "$task/stage.lock"; flock -n "$lockfd" || exit 73
record_shell "$task/logs/install-shell.tsv"
[[ $(timeout 2s cat "$task/archive-sha256") == "$expected" ]] || exit 65
printf '%s  %s\n' "$expected" "$task/upload.tar" | timeout 60s sha256sum -c --quiet -
if [[ -f $task/logs/install-complete.tsv ]]; then
  (cd "$task" && timeout 60s sha256sum -c --quiet files.sha256)
  printf 'installed_verified_no_repeat\n'; exit 0
fi
timeout 60s tar -tvf "$task/upload.tar" > "$task/logs/archive-members.txt"
rows=0; bytes=0
while read -r mode owner size date time name; do
  guard && ((rows<1200)) || exit 124
  [[ $name == ./* && $name =~ ^[A-Za-z0-9_./-]+$ && $name != *..* ]] || exit 65
  if [[ $mode == d* ]]; then
    [[ $name == ./ || $name == ./bundle/ || $name == ./regression/ || $name == ./regression_input/ || $name == ./data/ || $name == ./data/repair-evidence/ || $name == ./package/ || $name == ./package/tokenizer/ || $name == ./provenance/ || $name == ./provenance/data-sources/ || $name == ./kernels/ || $name == ./remote/ ]] || exit 65
  else
    [[ $mode == -* && $size =~ ^[0-9]+$ ]] || exit 65
    [[ $name == ./bundle/* || $name == ./regression/* || $name == ./regression_input/* || $name == ./data/* || $name == ./package/* || $name == ./provenance/* || $name == ./kernels/* || $name == ./remote/* || $name == ./README.md || $name == ./files.sha256 || $name == ./package-identity.json ]] || exit 65
    bytes=$((bytes+size)); ((bytes<=2621440000)) || exit 65
  fi
  rows=$((rows+1))
done < "$task/logs/archive-members.txt"
((rows>20)) || exit 65
for directory in bundle regression regression_input data package provenance kernels; do guard; [[ ! -e $task/$directory ]] || exit 73; done
timeout 60s tar -xf "$task/upload.tar" -C "$task" --no-same-owner --no-same-permissions
(cd "$task" && timeout 60s sha256sum -c --quiet files.sha256)
printf '%s  %s\n' e8e74ce0bbf1a72da5fcdf4dcad9d302513dc5dc42cb548bfd21d2569621aeb6 "$task/data/experiment-manifest.json" 55f3226946831c4b9cbd7f6ee10d1194764860c8343a9386caa40c678ce24ec0 "$task/data/derivation-ledger.json" b8fb9cecc0912f034e0fea184e8aa78871d11533093dccf4f5aad74646007551 "$task/package/model.json" | timeout 5s sha256sum -c --quiet -
chmod 700 "$task/bundle/Sezika.ExperimentTrainer" "$task/regression/RegressionCheck"
for binary in "$task/bundle/Sezika.ExperimentTrainer" "$task/bundle/libcoreclr.so" "$task/bundle/libSystem.Native.so" "$task/regression/RegressionCheck"; do
  guard; timeout 5s ldd "$binary" > "$task/logs/$(basename "$binary").ldd.txt"
  if grep -q 'not found' "$task/logs/$(basename "$binary").ldd.txt"; then cat "$task/logs/$(basename "$binary").ldd.txt"; exit 69; fi
done
printf 'installed_utc\t%(%FT%TZ)T\narchive_sha256\t%s\nmembers\t%s\nbytes\t%s\n' -1 "$expected" "$rows" "$bytes" > "$task/logs/install-complete.tsv"
printf 'installed_hash_and_dependencies_verified\n'
