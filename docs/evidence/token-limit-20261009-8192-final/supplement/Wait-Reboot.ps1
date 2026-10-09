#requires -Version 7.0
$ErrorActionPreference='Stop'
$task=$PSScriptRoot
$clock=[Diagnostics.Stopwatch]::StartNew()
$observations=[Collections.Generic.List[object]]::new()
$status='waiting'
try{
 for($attempt=2;$attempt -le 12 -and $clock.Elapsed.TotalSeconds -lt 300;$attempt++){
  if([IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'reboot observation cancelled'}
  $phase='reboot-observe-'+$attempt.ToString('00')
  $target=Join-Path $task ('remote/'+$phase+'.sh')
  if(Test-Path -LiteralPath $target){throw 'existing observation attempt retained; no overwrite'}
  [IO.File]::Copy((Join-Path $task 'remote/reboot-observe.sh'),$target,$false)
  Write-Output "reboot_observation=$attempt/12 elapsed=$([Math]::Round($clock.Elapsed.TotalSeconds,1))"
  $seconds=[Math]::Min(45,[Math]::Floor(300-$clock.Elapsed.TotalSeconds))
  if($seconds -lt 20){break}
  try {& (Join-Path $task 'Run-Remote.ps1') -Phase $phase -Seconds $seconds | Out-Host}catch{Write-Output ('observation retained: '+$_.Exception.Message)}
  $receipts=@(Get-ChildItem -LiteralPath (Join-Path $task 'process-logs') -File -Filter ('*-remote-'+$phase+'.result.json') | Select-Object -First 2)
  if($receipts.Count -ne 1){throw 'exact observation receipt required'}
  $r=Get-Content -Raw -LiteralPath $receipts[0].FullName | ConvertFrom-Json
  $output=[IO.File]::ReadAllText($r.StdoutPath)
  $observations.Add([pscustomobject]@{attempt=$attempt;receipt=$receipts[0].FullName;actual_exit=$r.ExitCode;elapsed=$r.ElapsedSeconds;new_boot_observed=$output.Contains('boot_id_after=');gpu_healthy=$output.Contains('gpu_observation_exit=0')})
  if($r.ExitCode -eq 0 -and $output.Contains('gpu_observation_exit=0')){$status='gpu_recovered_after_changed_boot';break}
  if($clock.Elapsed.TotalSeconds -lt 280){Start-Sleep -Seconds 8}
 }
}finally{
 [IO.File]::WriteAllText((Join-Path $task 'reboot-observations.json'),(@{utc=[DateTime]::UtcNow.ToString('o');status=$status;observations=$observations;max_attempts=12;wall_seconds=300;limitation='cooperative observation budget; final guardian cleanup may take its separate30s; initial pre-loop SSH refusal retained separately'} | ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
}
if($status -cne 'gpu_recovered_after_changed_boot'){throw 'GPU not recovered within bounded post-reboot observation'}
Write-Output 'changed_boot_and_actual_gpu_recovery_verified=true'
