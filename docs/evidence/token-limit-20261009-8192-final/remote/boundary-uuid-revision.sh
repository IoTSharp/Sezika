#!/bin/bash
set -euo pipefail
umask 077
task=/root/sezika-experiments/20261009-repaired-training-8192
test "$(hostname)" = digisyn-gpu01
test "$(timeout 2s cat "$task/task-identity")" = sezika-token-limit-20261009-8192
test ! -e "$task/.artifacts/stage-boundary-tiny2" && test ! -e "$task/logs/boundary-uuid-patch"
printf '%s  %s\n' 808ae565985659a49c409e91f72bc6b1ce61ee103e63625c67a5cda9f2b003f7 "$task/boundary-uuid-patch.tar" 86a13954fa2834820e8c5df337d2a7ccaed9a6b44a4e324fb52d5032c067665a "$task/remote/phase-common.sh" b4fb379e4201ff8684b041f83e31dd5baa220044951d4b1384a575b399bb2ce9 "$task/boundary/BoundaryCheck.dll" 66711466c1403e166ee6ba56f28cda7c2e2c33915b0320432d96995e76879655 "$task/boundary/BoundaryCheck.pdb" | timeout 3s sha256sum -c --quiet -
(( $(stat -c %s "$task/boundary-uuid-patch.tar") <= 1048576 ))
timeout 2s tar -tf "$task/boundary-uuid-patch.tar" > "$task/logs/boundary-uuid-patch-inventory.txt"
count=0; begin=$SECONDS
while IFS= read -r name; do
 ((count<5 && SECONDS-begin<5)) || exit 124
 [[ $name == ./ || $name == ./BoundaryCheck.dll || $name == ./BoundaryCheck.pdb || $name == ./phase-common.sh || $name == ./files.sha256 ]] || exit 65
 count=$((count+1))
done < "$task/logs/boundary-uuid-patch-inventory.txt"
mkdir "$task/logs/boundary-uuid-patch" "$task/logs/boundary-before-uuid-fix"
timeout 2s tar -xf "$task/boundary-uuid-patch.tar" -C "$task/logs/boundary-uuid-patch"
(cd "$task/logs/boundary-uuid-patch" && timeout 2s sha256sum -c --quiet files.sha256)
cp -- "$task/boundary/BoundaryCheck.dll" "$task/boundary/BoundaryCheck.pdb" "$task/boundary/files.sha256" "$task/logs/boundary-before-uuid-fix/"
cp -- "$task/remote/phase-common.sh" "$task/logs/phase-common-before-uuid-revision.sh"
cp -- "$task/remote/files.sha256" "$task/logs/remote-files-before-uuid-revision.sha256"
cp -- "$task/logs/boundary-uuid-patch/BoundaryCheck.dll" "$task/logs/boundary-uuid-patch/BoundaryCheck.pdb" "$task/boundary/"
cp -- "$task/logs/boundary-uuid-patch/phase-common.sh" "$task/remote/phase-common.sh"
sed -i 's/^b4fb379e4201ff8684b041f83e31dd5baa220044951d4b1384a575b399bb2ce9  BoundaryCheck.dll/cf47f63182ffd76ee2330f01cf3782d8f53fb7be59027d9de7f19172c64b4869  BoundaryCheck.dll/;s/^66711466c1403e166ee6ba56f28cda7c2e2c33915b0320432d96995e76879655  BoundaryCheck.pdb/78a669cc3148152aae4949684e75933fe6bd9a4214d30061fa34155f3566156a  BoundaryCheck.pdb/' "$task/boundary/files.sha256"
sed -i 's/^86a13954fa2834820e8c5df337d2a7ccaed9a6b44a4e324fb52d5032c067665a  phase-common.sh/79d8d21089c76cc4ec626366eb2a73202c01513ff1ac1991d0dcaf4ee530e981  phase-common.sh/' "$task/remote/files.sha256"
(cd "$task/boundary" && timeout 5s sha256sum -c --quiet files.sha256)
(cd "$task/remote" && timeout 5s sha256sum -c --quiet files.sha256)
timeout 2s bash -n "$task/remote/phase-common.sh"
printf 'uuid_format_fix_applied_original_failure_preserved_no_encoder_forward_yet=true\n'
