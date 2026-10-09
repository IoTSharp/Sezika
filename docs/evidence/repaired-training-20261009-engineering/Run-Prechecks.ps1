#requires -Version 7.0
$ErrorActionPreference='Stop'
$task='D:\source\Sezika\.artifacts\repaired-training-20261009'
$trainer='D:\source\Sezika\tools\Sezika.ExperimentTrainer\bin\Release\net10.0\Sezika.ExperimentTrainer.dll'
$runner=Join-Path $task 'Invoke-BoundedProcess.ps1'
$logs=Join-Path $task 'process-logs'
$clock=[Diagnostics.Stopwatch]::StartNew()
function Check {if($clock.Elapsed.TotalSeconds -ge 340 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'Prechecks cancelled/deadline'}}
foreach($maximum in @(1,215)){
 Check
 $label='v3-lengths-'+$maximum
 if(-not (Get-ChildItem -LiteralPath $logs -File -Filter ('*'+$label+'.result.json') | Select-Object -First 1)){
  & $runner -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @($trainer,'lengths-expanded','--data',(Join-Path $task 'data'),'--manifest-sha256','E8E74CE0BBF1A72DA5FCDF4DCAD9D302513DC5DC42CB548BFD21D2569621AEB6','--tokenizer','D:\source\Sezika\.artifacts\models\mmbert-independent\converted\tokenizer\tokenizer.json','--max-records',$maximum.ToString()) -TimeoutSeconds 130 -ProcessQueryTimeoutSeconds 2 -LogName $label -LogDirectory $logs | Out-Null
 }
 Check
 $receipt=Get-ChildItem -LiteralPath $logs -File -Filter ('*'+$label+'.result.json') | Select-Object -First 2
 if(@($receipt).Count -ne 1){throw 'Expected single new length receipt'}
 $result=Get-Content -Raw -LiteralPath $receipt.FullName|ConvertFrom-Json
 if($result.ExitCode -ne 0){throw 'Length process failed'}
 $report=Get-Content -Raw -LiteralPath $result.StdoutPath|ConvertFrom-Json
 if($report.contract -cne 'sezika/teacher-labelled-expanded-rendered-token-lengths/v3' -or $report.measurement.completed_records -ne $maximum -or $report.measurement.data_records -ne 215){throw 'Length actual denominator differs'}
 [IO.File]::Copy($result.StdoutPath,(Join-Path $task ($label+'.json')),$false)
 Write-Output "strict1024 actual=$maximum total=215 max_tokens=$($report.measurement.maximum_measured_tokens) elapsed=$($report.measurement.elapsed_seconds)"
}
Check
$legacy='D:\source\Sezika\.artifacts\teacher-expansion-20261008\input-fixture'
$hash=(Get-FileHash -LiteralPath (Join-Path $legacy 'experiment-manifest.json')).Hash
& $runner -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @($trainer,'check-expanded-input','--data',$legacy,'--manifest-sha256',$hash,'--fixture-only','true','--max-checks','16') -TimeoutSeconds 45 -ProcessQueryTimeoutSeconds 2 -LogName 'legacy-v2-fixture16' -LogDirectory $logs
Check
$legacy='D:\source\Sezika\.artifacts\real-gpu-experiment-20261008\data'
$hash=(Get-FileHash -LiteralPath (Join-Path $legacy 'experiment-manifest.json')).Hash
$directory=Join-Path $task 'legacy-v1-check'
[void][IO.Directory]::CreateDirectory($directory)
& $runner -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @($trainer,'check','--data',$legacy,'--manifest-sha256',$hash,'--max-checks','8','--artifact-directory',$directory) -TimeoutSeconds 45 -ProcessQueryTimeoutSeconds 2 -LogName 'legacy-v1-check8' -LogDirectory $logs
