#!/bin/bash
# Run under the task guardian; package only an already complete owned run directory.
set -euo pipefail
umask 077
[[ $# == 3 ]] || exit 64
root=$1; output=$2; maximum=$3
[[ $root =~ ^/[A-Za-z0-9_./-]+/sezika-repository-[a-z0-9-]+$ && $root != *..* && $output =~ ^[A-Za-z0-9_-]+$ ]] || exit 64
[[ $maximum =~ ^[0-9]+$ ]] && ((maximum>=30 && maximum<=1800)) || exit 64
[[ -f $root/OWNED_BY_SEZIKA_REPOSITORY_TASK && -d $root/$output && ! -L $root/$output && ! -e $root/$output.tar ]] || exit 73
[[ -f $root/$output/package.json && -f $root/$output/resources-reclaimed.txt ]] || exit 65
begin=$SECONDS; count=0
check(){ ((SECONDS-begin<maximum && count<=1000)) && [[ ! -e $root/CANCEL ]] || exit 124; }
exec {lock}> "$root/model-download.lock"
timeout 10s flock -w 8 "$lock" || exit 73
manifest="$root/$output/files.sha256"
[[ ! -e $manifest ]] || exit 73
# Bounded two-level package layout includes at most two complete checkpoints.
while IFS= read -r -d '' path; do
  check; count=$((count+1)); [[ ! -L $path ]] || exit 65
  relative=${path#"$root/$output/"}; [[ $relative =~ ^[A-Za-z0-9_./-]+$ && $relative != *..* ]] || exit 65
  (cd "$root/$output" && timeout --kill-after=2s "$((maximum-(SECONDS-begin)))s" sha256sum -- "$relative") >> "$manifest"
done < <(timeout 10s find "$root/$output" -maxdepth 2 -type f ! -name files.sha256 -print0)
check; ((count>=8 && count<=1000)) || exit 65
# Reject links, deeper files and leftover partial directories before tar.
[[ -z $(timeout 10s find "$root/$output" -maxdepth 3 \( -type l -o -mindepth 3 \) -print -quit) ]] || exit 65
timeout --kill-after=5s "$((maximum-(SECONDS-begin)))s" tar -cf "$root/$output.tar.partial" -C "$root/$output" .
check; mv -T -- "$root/$output.tar.partial" "$root/$output.tar"
(cd "$root" && timeout --kill-after=2s "$((maximum-(SECONDS-begin)))s" sha256sum -- "$output.tar" > "$output.tar.sha256")
printf 'packed_files=%s elapsed_seconds=%s\n' "$count" "$((SECONDS-begin))"
