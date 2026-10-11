#!/bin/bash
# Run beneath Invoke-BoundedLinux-v2.sh; this payload never receives credentials.
set -euo pipefail
umask 077
[[ $# == 6 ]] || { echo 'usage: clone-inputs.sh root run-id source-commit data-commit model-commit seconds' >&2; exit 64; }
root=$1; run_id=$2; source_commit=$3; data_commit=$4; model_commit=$5; maximum=$6
[[ $root =~ ^/[A-Za-z0-9_./-]+$ && $root != *..* && $root != / && $root == */sezika-repository-* ]] || exit 64
[[ $run_id =~ ^[a-z0-9][a-z0-9-]{2,63}$ && $root == */sezika-repository-"$run_id" ]] || exit 64
for revision in "$source_commit" "$data_commit" "$model_commit"; do [[ $revision =~ ^[a-f0-9]{40}$ ]] || exit 64; done
[[ $maximum =~ ^[0-9]+$ ]] && ((maximum>=60 && maximum<=14400)) || exit 64
[[ ! -e $root && ! -L $root ]] || { echo 'task root exists; inspect receipts, never reset or overwrite' >&2; exit 73; }
for dependency in git timeout realpath df sha256sum; do command -v "$dependency" >/dev/null || exit 69; done
git lfs version
parent=${root%/*}
[[ -d $parent && $(realpath -- "$parent") == "$parent" ]] || exit 65
available=$(timeout 5s df -B1 --output=avail "$parent" | tail -n 1); available=${available//[[:space:]]/}
[[ $available =~ ^[0-9]+$ ]] && ((available>=12884901888)) || { echo 'need at least 12 GiB free for pinned inputs, build and outputs' >&2; exit 75; }
mkdir -- "$root"
touch "$root/OWNED_BY_SEZIKA_REPOSITORY_TASK"
begin=$SECONDS; calls=0
export GIT_TERMINAL_PROMPT=0 GIT_LFS_SKIP_SMUDGE=1
unset GIT_TRACE GIT_TRACE_CURL GIT_CURL_VERBOSE
check() { ((SECONDS-begin<maximum && calls<24)) && [[ ! -e $root/CANCEL ]] || exit 124; }
run_git() {
  check; calls=$((calls+1)); local remaining=$((maximum-(SECONDS-begin)))
  printf 'progress=%s git_call=%s/%s remaining_seconds=%s\n' "${1-}" "$calls" 24 "$remaining"
  timeout --signal=TERM --kill-after=5s "${remaining}s" git \
    -c lfs.standalonetransferagent= -c lfs.basictransfersonly=true \
    -c lfs.concurrenttransfers=1 -c lfs.transfer.maxretries=0 \
    -c lfs.dialtimeout=15 -c lfs.activitytimeout=90 \
    -c http.lowSpeedLimit=1024 -c http.lowSpeedTime=60 "$@"
}
# This exception belongs only to the pinned original data commit. Preserve the
# failed standard fsck; objects, every asset, and both original Git blobs must
# pass separately. No attributes, raw bytes, or pre-push hooks are rewritten.
dataset_pointer_status=NOT_RUN
verify_dataset_fsck() {
  local code=0 remaining
  run_git -C "$root/dataset" lfs fsck > "$root/dataset-lfs-fsck.stdout.log" 2> "$root/dataset-lfs-fsck.stderr.log" || code=$?
  printf 'standard_fsck_exit=%s\n' "$code" > "$root/dataset-lfs-fsck.exit.txt"
  if ((code==0)); then dataset_pointer_status=PASS; return; fi
  [[ $code == 1 && $data_commit == b8ea13b81aaaca722baf6a3c0f12e2217fc4353e ]] || return "$code"
  command -v python3 >/dev/null || return 69
  check; remaining=$((maximum-(SECONDS-begin)))
  # The same checker is run twice: reject unknown diagnostics before asking
  # LFS to inspect objects, then verify the complete immutable content.
  cat > "$root/verify-known-dataset-fsck.py" <<'PY'
import hashlib, json, pathlib, re, subprocess, sys, time

COMMIT = 'b8ea13b81aaaca722baf6a3c0f12e2217fc4353e'
MANIFEST_SHA = 'fef8c839922ab676b9552a5fa7e0690345037e76a3f1a8ce81683a9aadd66438'
RIGHTS_SHA = '9428702a8ad92133c3173ff1200ee3f21e461216eabfaa5eadad3b55ea84eed4'
KNOWN = {
    'raw/upstream/clinc150/data_full.json': (2495390, '36923c3705a59e08fe9c3883d8bc2dd966ef93e22cb78ac41171782a698d56e0'),
    'raw/hf/PolyAI/banking77/dataset_infos.json': (5892, '402fed10f4894f8989dffeca0a81572ca488e6488fac3f18a33d9fd32422acc7'),
}

class IntegrityError(RuntimeError):
    pass

class Verification:
    def __init__(self, root, seconds):
        self.root = pathlib.Path(root)
        self.begin = time.monotonic()
        self.seconds = seconds
        self.git_calls = 0
        if not self.root.is_absolute() or self.root.resolve() != self.root or not self.root.is_dir():
            raise IntegrityError('Unsafe task root')

    def check(self):
        if time.monotonic() - self.begin >= self.seconds or (self.root / 'CANCEL').exists():
            raise IntegrityError('Verification deadline/cancel')

    def file(self, base, relative):
        self.check()
        p = pathlib.PurePosixPath(relative)
        if not relative or p.is_absolute() or '..' in p.parts or str(p) != relative or '\\' in relative or len(p.parts) > 32:
            raise IntegrityError('Unsafe asset path')
        path = base / relative
        if path.is_symlink() or path.resolve() != path or not path.is_file():
            raise IntegrityError('Asset missing/linked: ' + relative)
        return path

    def digest(self, path, expected_bytes=None):
        self.check()
        size = path.stat().st_size
        if size > 4 * 1024**3 or (expected_bytes is not None and size != expected_bytes):
            raise IntegrityError('Asset byte bound/mismatch: ' + str(path))
        result = hashlib.sha256()
        with path.open('rb') as stream:
            for block in range(1025):
                self.check()
                data = stream.read(4 * 1024**2)
                if not data:
                    if path.stat().st_size != size:
                        raise IntegrityError('Asset changed while hashing')
                    return result.hexdigest()
                result.update(data)
        raise IntegrityError('Hash block bound1025')

    def classify(self, commit):
        if commit != COMMIT:
            raise IntegrityError('Exception commit mismatch')
        if self.file(self.root, 'dataset-lfs-fsck.exit.txt').read_text(encoding='ascii').strip() != 'standard_fsck_exit=1':
            raise IntegrityError('Original failed fsck receipt absent/mismatched')
        expected = {
            'pointer: unexpectedGitObject: "' + path + '" (treeish ' + COMMIT + ') should have been a pointer but was not'
            for path in KNOWN
        }
        diagnostics = []
        for name in ('dataset-lfs-fsck.stdout.log', 'dataset-lfs-fsck.stderr.log'):
            path = self.file(self.root, name)
            if path.stat().st_size > 65536:
                raise IntegrityError('Fsck diagnostic byte bound64KiB')
            lines = path.read_text(encoding='utf-8').splitlines()
            if len(lines) > 16:
                raise IntegrityError('Fsck diagnostic line bound16')
            for line in lines:
                self.check()
                if not line or re.fullmatch(r'progress=-C git_call=[0-9]+/24 remaining_seconds=[0-9]+', line):
                    continue
                diagnostics.append(line)
        if len(diagnostics) != 2 or set(diagnostics) != expected:
            raise IntegrityError('Unknown or repeated pointer diagnostic; original failure retained')

    def git(self, *arguments, output_limit=4 * 1024**2):
        self.check()
        self.git_calls += 1
        if self.git_calls > 8:
            raise IntegrityError('Git inspection call bound8')
        remaining = max(1, min(10, int(self.seconds - (time.monotonic() - self.begin))))
        result = subprocess.run(['timeout', '--kill-after=2s', str(remaining) + 's', 'git', '-C', str(self.root / 'dataset'), *arguments],
                                stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=remaining + 3, check=True)
        if len(result.stdout) > output_limit or len(result.stderr) > 65536:
            raise IntegrityError('Git inspection output bound')
        return result.stdout

    def assets(self, base, items, count, total_bytes):
        if not isinstance(items, list) or len(items) != count or count > 275:
            raise IntegrityError('Asset count mismatch/bound275')
        seen = set()
        total = 0
        for index, item in enumerate(items):
            self.check()
            relative, size, sha = item['path'], item['bytes'], item['sha256']
            if relative in seen or type(size) is not int or size < 0 or not re.fullmatch(r'[a-f0-9]{64}', sha):
                raise IntegrityError('Invalid/duplicate asset')
            seen.add(relative)
            path = self.file(base, relative)
            if self.digest(path, size) != sha:
                raise IntegrityError('Asset SHA mismatch: ' + relative)
            total += size
            if total > 2 * 1024**3:
                raise IntegrityError('Asset total byte bound2GiB')
            if (index + 1) % 50 == 0:
                print('input_sha_progress=' + str(index + 1) + '/' + str(count), flush=True)
        if total != total_bytes:
            raise IntegrityError('Asset total bytes mismatch')
        return total

    def verify(self, commit):
        self.classify(commit)
        if self.file(self.root, 'dataset-lfs-objects.exit.txt').read_text(encoding='ascii').strip() != 'objects_fsck_exit=0':
            raise IntegrityError('Separate LFS objects check absent/failed')
        if self.git('rev-parse', 'HEAD').strip().decode('ascii') != COMMIT:
            raise IntegrityError('Actual checkout commit mismatch')
        base = self.root / 'dataset'
        manifest = self.file(base, 'ASSET_MANIFEST.json')
        if manifest.stat().st_size > 1024**2 or self.digest(manifest) != MANIFEST_SHA or self.digest(self.file(base, 'RIGHTS_MANIFEST.json')) != RIGHTS_SHA:
            raise IntegrityError('Pinned manifest/rights SHA mismatch')
        items = json.loads(manifest.read_bytes())
        total = self.assets(base, items, 275, 1076726933)
        for relative, (size, sha) in KNOWN.items():
            self.check()
            entry = COMMIT + ':' + relative
            if self.git('cat-file', '-t', entry).strip() != b'blob' or self.git('cat-file', '-s', entry).strip() != str(size).encode('ascii'):
                raise IntegrityError('Original Git blob identity/size mismatch: ' + relative)
            original = self.git('cat-file', 'blob', entry, output_limit=size)
            if len(original) != size or hashlib.sha256(original).hexdigest() != sha or self.digest(self.file(base, relative), size) != sha:
                raise IntegrityError('Original Git blob/worktree SHA mismatch: ' + relative)
        receipt = {
            'status': 'PASS_content_admission_only', 'dataset_commit': COMMIT,
            'standard_fsck_exit': 1, 'pointer_status': 'FAIL_known_attribute_mismatch',
            'lfs_objects_status': 'PASS_separate_fsck', 'assets': 275, 'bytes': total,
            'asset_manifest_sha256': MANIFEST_SHA, 'rights_manifest_sha256': RIGHTS_SHA,
            'original_git_blobs': list(KNOWN), 'elapsed_seconds': time.monotonic() - self.begin,
            'formal_training_admitted': False,
        }
        with (self.root / 'dataset-known-lfs-reconciliation.json').open('x', encoding='utf-8') as stream:
            json.dump(receipt, stream, indent=2)
        print(json.dumps(receipt), flush=True)

def main():
    if len(sys.argv) != 5 or sys.argv[1] not in ('classify', 'verify') or not sys.argv[4].isdigit() or not 1 <= int(sys.argv[4]) <= 180:
        raise IntegrityError('Arguments mode root commit seconds required; bound180s')
    checker = Verification(sys.argv[2], int(sys.argv[4]))
    if sys.argv[1] == 'classify':
        checker.classify(sys.argv[3])
    else:
        checker.verify(sys.argv[3])

if __name__ == '__main__':
    main()
PY
  local verify_seconds=$((remaining<180 ? remaining : 180))
  timeout --signal=TERM --kill-after=5s "${verify_seconds}s" python3 "$root/verify-known-dataset-fsck.py" classify "$root" "$data_commit" "$verify_seconds"
  local objects_code=0
  run_git -C "$root/dataset" lfs fsck --objects > "$root/dataset-lfs-objects.stdout.log" 2> "$root/dataset-lfs-objects.stderr.log" || objects_code=$?
  printf 'objects_fsck_exit=%s\n' "$objects_code" > "$root/dataset-lfs-objects.exit.txt"
  ((objects_code==0)) || return "$objects_code"
  check; remaining=$((maximum-(SECONDS-begin))); verify_seconds=$((remaining<180 ? remaining : 180))
  timeout --signal=TERM --kill-after=5s "${verify_seconds}s" python3 "$root/verify-known-dataset-fsck.py" verify "$root" "$data_commit" "$verify_seconds"
  dataset_pointer_status=FAIL_known_attribute_mismatch
}
names=(source dataset model)
urls=(https://github.com/IoTSharp/Sezika.git https://moark.com/maikebing/public-decision-data https://moark.com/maikebing/Sezika-mmBERT-8192-experimental)
revisions=("$source_commit" "$data_commit" "$model_commit")
for ((i=0; i<3; i++)); do
  check
  run_git clone --no-checkout -- "${urls[i]}" "$root/${names[i]}"
  run_git -C "$root/${names[i]}" checkout --detach "${revisions[i]}"
  [[ $(run_git -C "$root/${names[i]}" rev-parse HEAD | tail -n 1) == "${revisions[i]}" ]] || exit 65
  if ((i>0)); then
    run_git -C "$root/${names[i]}" lfs pull
    if ((i==1)); then verify_dataset_fsck; else run_git -C "$root/${names[i]}" lfs fsck; fi
  fi
  printf '%s\t%s\t%s\n' "${names[i]}" "${urls[i]}" "${revisions[i]}" >> "$root/input-revisions.tsv"
done
check
printf 'status=CLONED_refs_and_LFS_objects_verified_pointer_status_explicit_content_manifests_still_required\ndataset_pointer_status=%s\ncalls=%s\nelapsed_seconds=%s\n' "$dataset_pointer_status" "$calls" "$((SECONDS-begin))" | tee "$root/clone-result.txt"
