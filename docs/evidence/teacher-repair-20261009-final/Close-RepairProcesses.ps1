#requires -Version 7.0
param([switch]$Final16)
$ErrorActionPreference='Stop';$root=$PSScriptRoot;$watch=[Diagnostics.Stopwatch]::StartNew()
$output=Join-Path $root $(if($Final16){'final16-windows-closure.json'}else{'process-closure.json'})
if(Test-Path -LiteralPath $output){throw 'Closure already exists; retain it'}
function Check {if($watch.Elapsed.TotalSeconds -ge 180){throw 'Closure180s bound'}}
$reader=Get-CimInstance Win32_Process -Filter "ProcessId=$PID" -Property ProcessId,CreationDate,CommandLine,ParentProcessId -OperationTimeoutSec 5
$known=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
function Remember($Item,[string]$Source) {
  Check
  if($null -eq $Item -or -not $Item.Pid -or -not $Item.Started){return}
  if($known.Count -ge 2048){throw 'Closure identity cap2048'}
  $start=[DateTimeOffset]::Parse([string]$Item.Started)
  $key=([string]$Item.Pid)+':'+$start.UtcTicks
  if(-not $known.ContainsKey($key)){$known.Add($key,[pscustomobject]@{Pid=[int]$Item.Pid;Started=$start.ToString('o');CommandLine=$Item.CommandLine;ParentPid=$Item.ParentPid;source=$Source;state='unqueried';historical_argv_observed=(-not [string]::IsNullOrWhiteSpace([string]$Item.CommandLine))})}
}
$files=@(Get-ChildItem -LiteralPath "$root/api-owned-logs" -Filter '*.result.json' -File|Select-Object -First 81)
if($files.Count -gt 80){throw 'Receipt file cap80'}
foreach($file in $files){
  Check;if($file.Length -gt 4MB){throw 'Receipt byte cap4MiB'}
  $result=Get-Content -Raw -LiteralPath $file.FullName|ConvertFrom-Json -DateKind String
  if(@($result.Processes).Count -gt 128){throw 'Per receipt process cap128'}
  foreach($item in @($result.Processes)){Remember $item $file.Name}
  Remember $result.Launcher ($file.Name+':launcher')
  Remember $result ($file.Name+':retained_handle')
  if($result.IdentityPath){
    $identityPath=[IO.Path]::GetFullPath([string]$result.IdentityPath)
    if(-not $identityPath.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Identity path outside task'}
    $identity=Get-Content -Raw -LiteralPath $identityPath|ConvertFrom-Json -DateKind String
    Remember $identity.Launcher ($file.Name+':guardian')
  }
}
if(Test-Path -LiteralPath "$root/direct-remaining-wave.json"){
  $wave=Get-Content -Raw -LiteralPath "$root/direct-remaining-wave.json"|ConvertFrom-Json -DateKind String
  foreach($item in @($wave.batches)){Remember $item 'direct-wave-guardian'}
  Remember ([pscustomobject]@{Pid=$wave.identity.ProcessId;Started=$wave.identity.CreationDate;CommandLine=$wave.identity.CommandLine;ParentPid=$wave.identity.ParentProcessId}) 'direct-wave-controller'
}
$ids=@($known.Values.Pid|Sort-Object -Unique);$found=@{};$queryErrors=[Collections.Generic.List[string]]::new()
for($offset=0;$offset -lt $ids.Count -and $offset -lt 2048;$offset+=32){
  Check;$last=[Math]::Min($offset+31,$ids.Count-1);$part=@($ids[$offset..$last]);$filter=(@($part|ForEach-Object {'ProcessId='+$_}) -join ' OR ')
  try {
    $rows=@(Get-CimInstance Win32_Process -Filter $filter -Property ProcessId,CreationDate,CommandLine,ParentProcessId -OperationTimeoutSec 5|Select-Object -First 33)
    if($rows.Count -gt 32){throw 'Narrow PID result cap'}
    foreach($id in $part){$found[[int]$id]=$null}
    foreach($row in $rows){$found[[int]$row.ProcessId]=$row}
  }catch{$queryErrors.Add($_.Exception.GetType().Name)}
  Write-Output "Exact closure queried $([Math]::Min($offset+32,$ids.Count))/$($ids.Count) PIDs"
}
$live=0;$unknown=0;$missingArgv=0
foreach($item in $known.Values){
  Check
  if(-not $item.historical_argv_observed){$missingArgv++}
  if(-not $found.ContainsKey($item.Pid)){$item.state='query_unknown';$unknown++;continue}
  $actual=$found[$item.Pid]
  if($null -eq $actual){$item.state='absent';continue}
  if([Math]::Abs(($actual.CreationDate.ToUniversalTime()-[DateTimeOffset]::Parse($item.Started).UtcDateTime).Ticks) -gt 10){$item.state='pid_reused';continue}
  if(-not $item.historical_argv_observed -or $actual.CommandLine -cne $item.CommandLine -or ($null -ne $item.ParentPid -and $actual.ParentProcessId -ne $item.ParentPid)){$item.state='matching_start_identity_incomplete';$unknown++;continue}
  $item.state='matching_owned_live';$live++
}
$report=@{utc=[DateTimeOffset]::UtcNow.ToString('o');scope='recorded task identities only; read-only, no signals';receipts=$files.Count;identity_records=$known.Count;unique_pids=$ids.Count;matching_live=$live;unknown=$unknown;historical_records_without_observed_argv=$missingArgv;query_errors=@($queryErrors);reader=($reader|Select-Object ProcessId,CreationDate,CommandLine,ParentProcessId);records=@($known.Values);limitation='Current absence does not reconstruct missing historical argv or unobserved short-lived descendants; retained-handle exits and bounded commands are preserved separately.'}
[IO.File]::WriteAllText($output,($report|ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
Write-Output "Closure matching_live=$live unknown=$unknown historical_missing_argv=$missingArgv"
if($live -gt 0 -or $unknown -gt 0){exit 1}
