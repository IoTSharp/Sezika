$ErrorActionPreference='Stop'
$task=$PSScriptRoot;$clock=[Diagnostics.Stopwatch]::StartNew()
function Run([string]$Label,[string[]]$Arguments,[int]$Seconds=180){
 if($clock.Elapsed.TotalSeconds -ge 900 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'fix checks deadline/cancel'}
 try{& (Join-Path $task 'Invoke-BoundedProcess.ps1') -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList $Arguments -TimeoutSeconds $Seconds -LogName $Label -ProcessQueryTimeoutSeconds 2 -CancelFile (Join-Path $task 'cancel.request') -Environment @{TEMP=(Join-Path $task 'linux-compiler-temp');TMP=(Join-Path $task 'linux-compiler-temp')}|Out-Null}catch{Write-Output $_.Exception.Message}
 $r=Get-ChildItem -LiteralPath (Join-Path $task 'process-logs') -File -Filter ('*'+$Label+'.result.json')|Select-Object -First 1|Get-Content -Raw|ConvertFrom-Json
 if($null -eq $r.ExitCode -or $r.ExitCode -ne 0){Get-Content -LiteralPath $r.StdoutPath -Tail 20;Get-Content -LiteralPath $r.StderrPath -Tail 20;throw 'real fix command failed'}
 Write-Output "$Label real_exit=0";Get-Content -LiteralPath $r.StdoutPath -Tail 4
}
foreach($flag in @('--head-asset-publication-tiny','--head-asset-publication','--linux-publication')){
 Run $flag.TrimStart('-') @('D:\source\Sezika\tests\Sezika.Tests\bin\Release\net10.0\Sezika.Tests.dll',$flag) 45
}
$projects=@(
 @{path='D:\source\Sezika\tools\Sezika.ExperimentTrainer\Sezika.ExperimentTrainer.csproj';output='publish-linux-v2'},
 @{path=(Join-Path $task 'capacity-check/CapacityCheck.csproj');output='capacity-linux-v2'},
 @{path=(Join-Path $task 'regression-check/RegressionCheck.csproj');output='regression-linux-v2'},
 @{path='D:\source\Sezika\tests\Sezika.Tests\Sezika.Tests.csproj';output='tests-linux'})
foreach($item in $projects){
 if(Test-Path -LiteralPath (Join-Path $task $item.output)){throw 'publish output must be fresh'}
 Run $item.output @('publish',$item.path,'-c','Release','-r','linux-x64','--self-contained','true','--disable-build-servers','-m:1','-nr:false','-p:UseSharedCompilation=false','-p:RuntimeFrameworkVersion=10.0.12','-o',(Join-Path $task $item.output))
}
