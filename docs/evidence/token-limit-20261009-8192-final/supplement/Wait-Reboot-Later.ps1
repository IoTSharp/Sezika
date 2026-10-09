#requires -Version 7.0
$ErrorActionPreference='Stop'
$task=$PSScriptRoot
$clock=[Diagnostics.Stopwatch]::StartNew()
$observations=[Collections.Generic.List[object]]::new()
$status='not_yet_observable'
try{
 for($attempt=1;$attempt -le 8 -and $clock.Elapsed.TotalSeconds -lt 240;$attempt++){
  if([IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'cancelled'}
  $phase='reboot-observe-later-'+$attempt.ToString('00')
  $target=Join-Path $task ('remote/'+$phase+'.sh')
  if(Test-Path -LiteralPath $target){throw 'existing observation retained'}
  [IO.File]::Copy((Join-Path $task 'remote/reboot-observe.sh'),$target,$false)
  Write-Output "later_observation=$attempt/8 elapsed=$([Math]::Round($clock.Elapsed.TotalSeconds,1))"
  $remaining=[Math]::Floor(240-$clock.Elapsed.TotalSeconds)
  if($remaining -lt 20){break}
  try {& (Join-Path $task 'Run-Remote.ps1') -Phase $phase -Seconds ([Math]::Min(45,$remaining)) | Out-Host}catch{Write-Output ('retained: '+$_.Exception.Message)}
  $files=@(Get-ChildItem -LiteralPath (Join-Path $task 'process-logs') -File -Filter ('*-remote-'+$phase+'.result.json') | Select-Object -First 2)
  if($files.Count -ne 1){throw 'exact receipt required'}
  $r=Get-Content -Raw -LiteralPath $files[0].FullName | ConvertFrom-Json
  $output=[IO.File]::ReadAllText($r.StdoutPath)
  $observations.Add(@{attempt=$attempt;receipt=$files[0].FullName;actual_exit=$r.ExitCode;changed_boot=$output.Contains('boot_id_after=');gpu_recovered=$output.Contains('gpu_observation_exit=0')})
  if($r.ExitCode -eq 0 -and $output.Contains('gpu_observation_exit=0')){$status='gpu_recovered_after_changed_boot';break}
  if($clock.Elapsed.TotalSeconds -lt 220){Start-Sleep -Seconds 15}
 }
}finally{[IO.File]::WriteAllText((Join-Path $task 'reboot-observations-later.json'),(@{utc=[DateTime]::UtcNow.ToString('o');status=$status;observations=$observations;max_attempts=8;wall_seconds=240;reason='additional bounded observation during local evidence closure; no additional reboot request'} | ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))}
Write-Output "later_gpu_observation_status=$status"
