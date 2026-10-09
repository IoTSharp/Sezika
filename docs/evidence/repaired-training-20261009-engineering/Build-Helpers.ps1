#requires -Version 7.0
$ErrorActionPreference='Stop'
$task='D:\source\Sezika\.artifacts\repaired-training-20261009'
$clock=[Diagnostics.Stopwatch]::StartNew()
$temp=Join-Path $task 'helper-compiler-temp'
if(Test-Path -LiteralPath $temp){throw 'Fresh helper compiler temp required'}
[void][IO.Directory]::CreateDirectory($temp)
try{
 foreach($name in @('capacity-check/CapacityCheck.csproj','regression-check/RegressionCheck.csproj')){
  if($clock.Elapsed.TotalSeconds -ge 370 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'Helper build cancelled/deadline'}
  & (Join-Path $task 'Invoke-BoundedProcess.ps1') -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @('build',(Join-Path $task $name),'-c','Release','--disable-build-servers','-m:1','-nr:false','-p:UseSharedCompilation=false') -TimeoutSeconds 180 -ProcessQueryTimeoutSeconds 2 -LogName ($name.Replace('/','-')) -LogDirectory (Join-Path $task 'process-logs') -Environment @{TEMP=$temp;TMP=$temp;DOTNET_CLI_TELEMETRY_OPTOUT='1'}
 }
}finally{
 if([IO.Path]::GetFullPath($temp) -cne (Join-Path $task 'helper-compiler-temp')){throw 'Helper temp ownership differs'}
 $entries=@(Get-ChildItem -LiteralPath $temp -Force|Select-Object -First 25)
 [IO.File]::WriteAllText((Join-Path $task 'helper-temp-inventory.json'),(@{path=$temp;entries=@($entries|Select-Object FullName,Length,PSIsContainer);cleanup='retain exact task-owned objects pending evidence review'}|ConvertTo-Json -Depth 5))
}
