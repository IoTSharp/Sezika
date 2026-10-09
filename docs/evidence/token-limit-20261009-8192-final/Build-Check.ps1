#requires -Version 7.0
$ErrorActionPreference='Stop'
$task=$PSScriptRoot
$clock=[Diagnostics.Stopwatch]::StartNew()
$runner=Join-Path $task 'Invoke-BoundedProcess.ps1'
$temp=Join-Path $task 'compiler-temp'
[IO.Directory]::CreateDirectory($temp)|Out-Null
function Check { if($clock.Elapsed.TotalSeconds -ge 1100 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'build/check deadline or cancellation'} }
function Run([string]$Label,[string[]]$Arguments,[int]$Seconds=180){
 Check
 try { & $runner -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList $Arguments -TimeoutSeconds $Seconds -LogName $Label -ProcessQueryTimeoutSeconds 2 -CancelFile (Join-Path $task 'cancel.request') -Environment @{TEMP=$temp;TMP=$temp} | Out-Host } catch {Write-Output ('Guardian retained: '+$_.Exception.Message)}
 $receipts=@(Get-ChildItem -LiteralPath (Join-Path $task 'process-logs') -Filter ('*'+$Label+'.result.json') -File|Select-Object -First 2)
 if($receipts.Count -ne 1){throw 'single receipt required'}
 $r=[IO.File]::ReadAllText($receipts[0].FullName)|ConvertFrom-Json
 if($null -eq $r.ExitCode -or $r.ExitCode -ne 0){Get-Content -LiteralPath $r.StderrPath -Tail 20;Get-Content -LiteralPath $r.StdoutPath -Tail 20;throw 'real child exit failed'}
 Get-Content -LiteralPath $r.StdoutPath -Tail 5
}
$projects=@('tools/Sezika.ExperimentTrainer/Sezika.ExperimentTrainer.csproj','tests/Sezika.Tests/Sezika.Tests.csproj','src/Sezika.Cli/Sezika.Cli.csproj','.artifacts/token-limit-20261009-8192/boundary-check/BoundaryCheck.csproj','.artifacts/token-limit-20261009-8192/regression-check/RegressionCheck.csproj')
try{
 for($i=0;$i -lt 5;$i++){Check; Run ('build-'+$i) @('build',(Join-Path 'D:\source\Sezika' $projects[$i]),'-c','Release','--disable-build-servers','-m:1','-nr:false','-p:UseSharedCompilation=false')}
 Run 'core-continuation' @('D:\source\Sezika\tests\Sezika.Tests\bin\Release\net10.0\Sezika.Tests.dll','--continuation-contract') 50
 Run 'boundary-cpu' @((Join-Path $task 'boundary-check/bin/Release/net10.0/BoundaryCheck.dll'),'cpu',(Join-Path $task 'cpu-boundary')) 130
 foreach($maximum in @(1,215)){
  Check
  $label='lengths8192-'+$maximum
  Run $label @('D:\source\Sezika\tools\Sezika.ExperimentTrainer\bin\Release\net10.0\Sezika.ExperimentTrainer.dll','lengths-expanded','--data','D:\source\Sezika\.artifacts\repaired-training-20261009\data','--manifest-sha256','E8E74CE0BBF1A72DA5FCDF4DCAD9D302513DC5DC42CB548BFD21D2569621AEB6','--tokenizer','D:\source\Sezika\.artifacts\models\mmbert-independent\converted\tokenizer\tokenizer.json','--max-records',$maximum.ToString(),'--max-tokens','8192') 130
  $receipt=Get-ChildItem -LiteralPath (Join-Path $task 'process-logs') -Filter ('*'+$label+'.result.json') -File|Select-Object -First 1
  $r=[IO.File]::ReadAllText($receipt.FullName)|ConvertFrom-Json
  [IO.File]::Copy($r.StdoutPath,(Join-Path $task ($label+'.json')),$false)
 }
}finally{
 $entries=@(Get-ChildItem -LiteralPath $temp -Force|Select-Object -First 65)
 [IO.File]::WriteAllText((Join-Path $task 'compiler-temp-inventory.json'),(@{path=$temp;entries=$entries|Select-Object FullName,Length,PSIsContainer;cleanup='retained exact task objects; no shared TEMP changes'}|ConvertTo-Json -Depth 4))
}
