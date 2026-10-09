#requires -Version 7.0
$ErrorActionPreference='Stop'
$task=$PSScriptRoot
$common=[IO.File]::ReadAllText((Join-Path $task 'remote/phase-common.sh')).Replace("`r`n","`n")
$old='[[ ${phase-} == boundary-tiny2 || ${phase-} == boundary-tiny || ${phase-} == boundary-full || ${phase-} == boundary-real || ${phase-} == publication || ${phase-} == capacity2 || ${phase-} == capacity || ${phase-} == train || ${phase-} == validate || ${phase-} == regress ]]'
if(-not $common.Contains($old)){throw 'phase allowlist seam differs'}
$common=$common.Replace($old,'[[ ${phase-} == validate-postreboot || ${phase-} == regress-postreboot ]]').Replace('[[ $phase == validate ]]','[[ $phase == validate-postreboot ]]').Replace('[[ $phase == regress ]]','[[ $phase == regress-postreboot ]]').Replace('$task/.artifacts/stage-validate/result.tsv','$task/.artifacts/stage-validate-postreboot/result.tsv').Replace("`nvalidate)","`nvalidate-postreboot)").Replace("`nregress)","`nregress-postreboot)").Replace('expanded-validation.json','expanded-validation-postreboot.json').Replace('original32-regression.json','original32-regression-postreboot.json')
$start=$common.IndexOf("`ntrain)`n",[StringComparison]::Ordinal)
$end=$common.IndexOf("`nvalidate-postreboot)`n",[StringComparison]::Ordinal)
if($start -lt 0 -or $end -le $start){throw 'remove forbidden train branch seam differs'}
$common=$common.Remove($start,$end-$start)
$marker='stage_begin=$SECONDS'
$guard=@'
stage_begin=$SECONDS
before_boot=$(timeout 2s cat "$task/.artifacts/stage-reboot-approved/boot-id-before.txt")
after_boot=$(timeout 2s cat /proc/sys/kernel/random/boot_id)
test "$before_boot" != "$after_boot"
printf 'approved_reboot_old_boot_id=%s current_boot_id=%s\n' "$before_boot" "$after_boot"
'@
if(-not $common.Contains($marker)){throw 'reboot guard seam differs'}
$common=$common.Replace($marker,$guard)
$bytes=[Text.Encoding]::UTF8.GetBytes($common)
$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
$base64=[Convert]::ToBase64String($bytes)
$deploy=@'
#!/bin/bash
set -euo pipefail
task=/root/sezika-experiments/20261009-repaired-training-8192
test "$(hostname)" = digisyn-gpu01
test "$(timeout 2s cat "$task/task-identity")" = sezika-token-limit-20261009-8192
test ! -e "$task/remote/phase-postreboot.sh" && test ! -L "$task/remote/phase-postreboot.sh"
printf '%s' '@@BASE64@@' | base64 --decode > "$task/remote/phase-postreboot.sh"
printf '%s  %s\n' '@@SHA@@' "$task/remote/phase-postreboot.sh" | timeout 3s sha256sum -c --quiet -
timeout 3s bash -n "$task/remote/phase-postreboot.sh"
echo postreboot_validation_only_helper_installed=true
'@
$deploy=$deploy.Replace('@@BASE64@@',$base64).Replace('@@SHA@@',$hash)
[IO.File]::WriteAllText((Join-Path $task 'remote/phase-postreboot.sh'),$common,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $task 'remote/install-postreboot-validation.sh'),$deploy,[Text.UTF8Encoding]::new($false))
foreach($phase in @('validate-postreboot','regress-postreboot')){
 $script=@'
#!/bin/bash
set -euo pipefail
phase=@@PHASE@@
source /root/sezika-experiments/20261009-repaired-training-8192/remote/phase-postreboot.sh
'@
 [IO.File]::WriteAllText((Join-Path $task ('remote/'+$phase+'.sh')),$script.Replace('@@PHASE@@',$phase),[Text.UTF8Encoding]::new($false))
}
[IO.File]::WriteAllText((Join-Path $task 'postreboot-helper.json'),(@{sha256=$hash;phases=@('validate-postreboot','regress-postreboot');training_branch_removed=$true;new_validation_output='output/expanded-validation-postreboot.json';new_regression_output='output/original32-regression-postreboot.json';old_failure_receipts_preserved=$true;requires_changed_boot_id=$true} | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Write-Output "validation_only_helper_sha256=$hash"
