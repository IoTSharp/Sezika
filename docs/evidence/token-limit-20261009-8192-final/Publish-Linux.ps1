#requires -Version 7.0
$ErrorActionPreference='Stop'
$task='D:\source\Sezika\.artifacts\token-limit-20261009-8192'
$clock=[Diagnostics.Stopwatch]::StartNew()
function Check {if($clock.Elapsed.TotalSeconds -ge 760 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'Linux publish cancelled/deadline'}}
$names=@('Directory.Build.props','tools/Sezika.ExperimentTrainer/Sezika.ExperimentTrainer.csproj','tools/Sezika.ExperimentTrainer/Program.cs','tools/Sezika.ExperimentTrainer/ExperimentData.cs','tools/Sezika.ExperimentTrainer/RepairedDataContract.cs','tools/Sezika.ExperimentTrainer/ExperimentReport.cs','tools/Sezika.ExperimentTrainer/ExpansionChecks.cs','tools/Sezika.ExperimentTrainer/ExpansionEvaluation.cs','src/Sezika/ModernBert.cs','src/Sezika/IndependentModelContract.cs','src/Sezika.Cuda/CudaModernBertEncoder.cs','src/Sezika/IndependentDecisionRuntime.cs','src/Sezika/DiagnosticDecisionRuntime.cs','src/Sezika/QuantizedOps.cs','src/Sezika/IndependentMarkerHeadTraining.cs','src/Sezika/IndependentMarkerHeadAsset.cs','src/Sezika.Cuda/IndependentCudaMarkerHeadTrainer.cs','src/Sezika.Cuda/IndependentCudaMarkerHeadEvaluator.cs')
$files=@(foreach($name in $names){Check;$path=Join-Path 'D:\source\Sezika' $name;@{path=$path;sha256=(Get-FileHash -LiteralPath $path).Hash}})
foreach($kind in @('capacity-check','regression-check','boundary-check')){Check;foreach($name in @('Program.cs',$(if($kind -eq 'capacity-check'){'CapacityCheck.csproj'}elseif($kind -eq 'regression-check'){'RegressionCheck.csproj'}else{'BoundaryCheck.csproj'}))){$path=Join-Path $task ($kind+'/'+$name);$files+=@{path=$path;sha256=(Get-FileHash -LiteralPath $path).Hash}}}
$freeze=Join-Path $task 'source-freeze.json'
if(Test-Path -LiteralPath $freeze){throw 'Source freeze already exists; preserve and inspect'}
[IO.File]::WriteAllText($freeze,(@{utc=[DateTime]::UtcNow.ToString('o');thread='01a11f25-335a-7571-a735-7171ace8c3b9';runtime='10.0.12';rid='linux-x64';native_aot=$false;files=$files}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
$temp=Join-Path $task 'linux-compiler-temp'
if(Test-Path -LiteralPath $temp){throw 'Fresh exclusive Linux compiler temp required'}
[void][IO.Directory]::CreateDirectory($temp)
try{
 $projects=@(@{project='D:\source\Sezika\tools\Sezika.ExperimentTrainer\Sezika.ExperimentTrainer.csproj';output='publish-linux'},@{project=(Join-Path $task 'capacity-check/CapacityCheck.csproj');output='capacity-linux'},@{project=(Join-Path $task 'regression-check/RegressionCheck.csproj');output='regression-linux'},@{project=(Join-Path $task 'boundary-check/BoundaryCheck.csproj');output='boundary-linux'})
 foreach($item in $projects){
  Check
  $output=Join-Path $task $item.output
  if(Test-Path -LiteralPath $output){throw 'Fresh publish directory required'}
  & (Join-Path $task 'Invoke-BoundedProcess.ps1') -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @('publish',$item.project,'-c','Release','-r','linux-x64','--self-contained','true','--disable-build-servers','-m:1','-nr:false','-p:UseSharedCompilation=false','-p:RuntimeFrameworkVersion=10.0.12','-o',$output) -TimeoutSeconds 180 -ProcessQueryTimeoutSeconds 2 -LogName $item.output -LogDirectory (Join-Path $task 'process-logs') -Environment @{TEMP=$temp;TMP=$temp;DOTNET_CLI_TELEMETRY_OPTOUT='1'}
 }
 foreach($file in $files){Check;if((Get-FileHash -LiteralPath $file.path).Hash -cne $file.sha256){throw 'Frozen source changed during Linux publish'}}
}finally{
 if([IO.Path]::GetFullPath($temp) -cne (Join-Path $task 'linux-compiler-temp')){throw 'Compiler temp ownership differs'}
 $entries=@(Get-ChildItem -LiteralPath $temp -Force|Select-Object -First 32)
 [IO.File]::WriteAllText((Join-Path $task 'linux-temp-inventory.json'),(@{path=$temp;entries=@($entries|Select-Object FullName,Length,PSIsContainer);cleanup='exact task compiler objects retained pending evidence review; no global TEMP deletion'}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
}
