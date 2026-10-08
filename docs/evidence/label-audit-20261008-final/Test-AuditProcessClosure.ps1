#requires -Version 7.0
$ErrorActionPreference='Stop'
$root='D:\source\Sezika\.artifacts\label-audit-20261008'
$watch=[Diagnostics.Stopwatch]::StartNew()
$identities=[Collections.Generic.List[object]]::new()
$nodeCount=0
function Check-ClosureBudget {
    if ($watch.Elapsed.TotalSeconds -ge 45 -or [IO.File]::Exists((Join-Path $root 'cancel.request'))) { throw 'Closure deadline/cancellation.' }
}
function Read-IdentityNodes($Node,[int]$Depth,[string]$From) {
    Check-ClosureBudget
    $script:nodeCount++
    if ($script:nodeCount -gt 12000 -or $Depth -gt 20) { throw 'Closure JSON traversal bound.' }
    if ($null -eq $Node -or $Node -is [string] -or $Node -is [ValueType]) { return }
    if ($Node -is [array]) { if($Node.Count -gt 512){throw 'Identity array bound'};foreach($part in $Node){Read-IdentityNodes $part ($Depth+1) $From};return }
    $names=@($Node.PSObject.Properties.Name)
    if ($names.Count -gt 128) { throw 'Identity property bound.' }
    $taskPid=if($names -contains 'Pid'){$Node.Pid}elseif($names -contains 'ProcessId'){$Node.ProcessId}else{$null}
    $started=if($names -contains 'Started'){$Node.Started}elseif($names -contains 'CreationDate'){$Node.CreationDate}else{$null}
    if ($null -ne $taskPid -and $null -ne $started -and [int]$taskPid -ne $PID) {
        $argv=$Node.CommandLine
        $parent=if($names -contains 'ParentPid'){$Node.ParentPid}else{$Node.ParentProcessId}
        # Parent identities outside this task are never claimed or terminated.
        if (-not $argv -or ([string]$argv).Contains('label-audit-20261008',[StringComparison]::Ordinal)) {
            $identities.Add([ordered]@{Pid=[int]$taskPid;Started=[DateTimeOffset]::Parse([string]$started).ToUniversalTime().ToString('o');CommandLine=$argv;ParentPid=$parent;From=$From})
        }
    }
    foreach($property in $Node.PSObject.Properties){Read-IdentityNodes $property.Value ($Depth+1) $From}
}
$files=@(Get-ChildItem -LiteralPath $root -File -Depth 2 | Where-Object Name -Match '(identity|readers?|writer|result)\.json$' | Select-Object -First 201)
if($files.Count -gt 200){throw 'Identity file count bound.'}
foreach($file in $files){Check-ClosureBudget;$json=Get-Content -LiteralPath $file.FullName -Raw|ConvertFrom-Json -Depth 40 -DateKind String;Read-IdentityNodes $json 0 $file.FullName}
$readerLines=@(Get-Content -LiteralPath (Join-Path $root 'root-readers.jsonl') -TotalCount 129)
if($readerLines.Count -gt 128){throw 'Root reader bound.'}
foreach($line in $readerLines){Check-ClosureBudget;Read-IdentityNodes ($line|ConvertFrom-Json -DateKind String) 0 'root-readers.jsonl'}
$native=@(Get-CimInstance Win32_Process -Property ProcessId,CreationDate,CommandLine,ParentProcessId -OperationTimeoutSec 5 | Select-Object -First 16385)
if($native.Count -gt 16384){throw 'OS snapshot bound.'}
$byPid=@{};foreach($item in $native){Check-ClosureBudget;$byPid[[int]$item.ProcessId]=$item}
$activeAncestors=[Collections.Generic.HashSet[int]]::new()
$ancestor=$byPid[$PID]
for($i=0;$i -lt 8 -and $null -ne $ancestor;$i++){Check-ClosureBudget;if(-not $activeAncestors.Add([int]$ancestor.ProcessId)){break};$ancestor=$byPid[[int]$ancestor.ParentProcessId]}
$checks=[Collections.Generic.List[object]]::new();$matching=0;$unknown=0;$absent=0;$reused=0
if($identities.Count -gt 2048){throw 'Identity count bound.'}
foreach($identity in $identities){
    Check-ClosureBudget
    if($activeAncestors.Contains($identity.Pid)){continue}
    $actual=$byPid[$identity.Pid];$state='absent'
    if($null -eq $actual){$absent++}
    elseif([DateTimeOffset]::Parse($identity.Started).UtcDateTime -ne $actual.CreationDate.ToUniversalTime()){$state='pid_reused';$reused++}
    elseif($identity.CommandLine -and $identity.CommandLine -ceq $actual.CommandLine -and [int]$identity.ParentPid -eq $actual.ParentProcessId){$state='matching_live';$matching++}
    else{$state='unknown_live_identity';$unknown++}
    $checks.Add([ordered]@{identity=$identity;state=$state})
}
$self=Get-CimInstance Win32_Process -Filter "ProcessId=$PID" -Property ProcessId,CreationDate,CommandLine,ParentProcessId -OperationTimeoutSec 5
$result=[ordered]@{status=if($matching -eq 0 -and $unknown -eq 0){'registered_processes_closed'}else{'attention_required'};checked_records=$checks.Count;unique_pids=@($checks.identity.Pid|Sort-Object -Unique).Count;matching_live=$matching;unknown_live=$unknown;absent=$absent;pid_reused=$reused;identity_files=$files.Count;root_readers=$readerLines.Count;elapsed_seconds=$watch.Elapsed.TotalSeconds;checker_identity=($self|Select-Object ProcessId,CreationDate,CommandLine,ParentProcessId);excluded_current_ancestors=@($activeAncestors);checks=@($checks);cleanup_commands_executed=$false;retained_owned_empty_directories=@('D:\source\Sezika\general-zh-choice-01','D:\source\Sezika\delivery');cleanup_limitation='Automatic approval rejected archive/remove command: blocked by policy; no bypass attempted.'}
[IO.File]::WriteAllText((Join-Path $root 'process-closure.json'),($result|ConvertTo-Json -Depth 30),[Text.UTF8Encoding]::new($false))
[pscustomobject]$result|Select-Object status,checked_records,unique_pids,matching_live,unknown_live,absent,pid_reused,elapsed_seconds|ConvertTo-Json -Compress
if($matching -gt 0 -or $unknown -gt 0){throw 'Registered task process closure requires attention.'}
