#requires -Version 7.0
$ErrorActionPreference='Stop'
$task='D:\source\Sezika\.artifacts\repaired-training-20261009'
$logs=Join-Path $task 'process-logs'
$clock=[Diagnostics.Stopwatch]::StartNew()
foreach($maximum in @(1,215)){
 if($clock.Elapsed.TotalSeconds -ge 270 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'Length audit cancelled/deadline'}
 $label='v3-complete-lengths-'+$maximum
 $runnerError=$null
 try{
  & (Join-Path $task 'Invoke-BoundedProcess.ps1') -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @('D:\source\Sezika\tools\Sezika.ExperimentTrainer\bin\Release\net10.0\Sezika.ExperimentTrainer.dll','lengths-expanded','--data',(Join-Path $task 'data'),'--manifest-sha256','E8E74CE0BBF1A72DA5FCDF4DCAD9D302513DC5DC42CB548BFD21D2569621AEB6','--tokenizer','D:\source\Sezika\.artifacts\models\mmbert-independent\converted\tokenizer\tokenizer.json','--max-records',$maximum.ToString()) -TimeoutSeconds 130 -ProcessQueryTimeoutSeconds 2 -LogName $label -LogDirectory $logs | Out-Null
 }catch{$runnerError=$_.Exception.Message}
 $receipts=@(Get-ChildItem -LiteralPath $logs -File -Filter ('*'+$label+'.result.json')|Select-Object -First 2)
 if($receipts.Count -ne 1){throw 'Expected exactly one length receipt'}
 $receipt=Get-Content -Raw -LiteralPath $receipts[0].FullName|ConvertFrom-Json
 $report=Get-Content -Raw -LiteralPath $receipt.StdoutPath|ConvertFrom-Json
 if($report.contract -cne 'sezika/teacher-labelled-expanded-rendered-token-lengths/v3' -or $report.data_records -ne 215 -or $report.attempted_records -ne $maximum -or $report.unattempted_records -ne (215-$maximum) -or ($report.accepted_records+$report.failed_records) -ne $maximum -or $receipt.ExitCode -ne $(if($report.failed_records -gt 0){1}else{0})){throw 'Length report/actual process exit/denominator differs'}
 [IO.File]::Copy($receipt.StdoutPath,(Join-Path $task ($label+'.json')),$false)
 [IO.File]::WriteAllText((Join-Path $task ($label+'.summary.json')),(@{receipt=$receipts[0].Name;status=$report.status;attempted=$report.attempted_records;accepted=$report.accepted_records;failed=$report.failed_records;unattempted=$report.unattempted_records;maximum_successful_tokens=$report.maximum_successful_tokens;failed_rows=@($report.rows|Where-Object {-not $_.accepted});process_exit=$receipt.ExitCode;guardian_status=$receipt.Status;guardian_error=$runnerError}|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
 Write-Output "strict1024 attempted=$maximum accepted=$($report.accepted_records) failed=$($report.failed_records) unattempted=$($report.unattempted_records) maximum_successful=$($report.maximum_successful_tokens) process_exit=$($receipt.ExitCode)"
 $report.rows|Where-Object {-not $_.accepted}|Select-Object record_id,split,type,failure_code
}
