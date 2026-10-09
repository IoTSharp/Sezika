#requires -Version 7.0
$ErrorActionPreference='Stop'
$task=$PSScriptRoot
$clock=[Diagnostics.Stopwatch]::StartNew()
function Check { if($clock.Elapsed.TotalSeconds -ge 75 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'closure deadline/cancellation'} }
$receipts=@(Get-ChildItem -LiteralPath (Join-Path $task 'process-logs') -File -Filter '*.result.json' | Select-Object -First 193)
if($receipts.Count -gt 192){throw 'receipt cap192'}
$records=[Collections.Generic.List[object]]::new()
$ids=[Collections.Generic.HashSet[int]]::new()
foreach($file in $receipts){
 Check
 $r=Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json
 foreach($p in @($r)+@($r.Processes)+@($r.Launcher)){
  Check
  if($null -eq $p -or $null -eq $p.Pid){continue}
  if($records.Count -ge 1536){throw 'identity cap1536'}
  $records.Add([pscustomobject]@{source=$file.Name;pid=[int]$p.Pid;start=$p.Started;argv=$p.CommandLine;parent=$p.ParentPid})
  [void]$ids.Add([int]$p.Pid)
 }
}
foreach($extra in @($PID)){[void]$ids.Add($extra)}
if($ids.Count -gt 640){throw 'PID cap640'}
$all=@($ids)
$actual=@{}
for($i=0;$i -lt $all.Count -and $i -lt 640;$i+=16){
 Check
 $batch=@($all | Select-Object -Skip $i -First 16)
 $filter=(@($batch | ForEach-Object {"ProcessId=$_ OR ParentProcessId=$_"})) -join ' OR '
 $snapshot=@(Get-CimInstance Win32_Process -Filter $filter -Property ProcessId,CreationDate,CommandLine,ParentProcessId -OperationTimeoutSec 2 | Select-Object -First 193)
 if($snapshot.Count -gt 192){throw 'snapshot cap128'}
 foreach($p in $snapshot){$actual[[int]$p.ProcessId]=$p | Select-Object ProcessId,CreationDate,CommandLine,ParentProcessId}
 Write-Output "closure batch=$i/$($all.Count) observed=$($actual.Count)"
}
$checks=@(foreach($r in $records){
 Check
 $p=$actual[$r.pid]
 $state='absent'
 if($r.pid -eq $PID){$state='current_inspection_process'}
 elseif($r.pid -eq $actual[$PID].ParentProcessId){$state='current_inspection_launcher_check_after_exit'}
 elseif($null -ne $p){
  if($r.start -and [Math]::Abs((([DateTime]$r.start).ToUniversalTime()-$p.CreationDate.ToUniversalTime()).Ticks) -gt 10){$state='reused'}
  elseif($r.argv -and $r.argv -ceq $p.CommandLine -and $r.parent -eq $p.ParentProcessId){$state='matching_live'}
  else{$state='present_unknown_identity'}
 }
 [pscustomobject]@{registration=$r;state=$state;actual=$p}
})
$children=@(foreach($p in $actual.Values){
 Check
 if($ids.Contains([int]$p.ProcessId)){continue}
 $parents=@($checks | Where-Object {$_.registration.pid -eq $p.ParentProcessId})
 $relation='unregistered_child_parent_identity_not_proven'
 if(@($parents | Where-Object state -eq matching_live).Count -gt 0){$relation='child_of_matching_registered_parent_needs_review'}
 elseif($parents.Count -gt 0 -and @($parents | Where-Object state -ne reused).Count -eq 0){$relation='child_of_reused_pid_not_task_owned'}
 elseif($p.ParentProcessId -eq $PID){$relation='child_of_current_inspection_process'}
 elseif($p.ParentProcessId -eq $actual[$PID].ParentProcessId){$relation='child_of_current_inspection_launcher_check_after_exit'}
 [pscustomobject]@{actual=$p;relation=$relation;parent_observations=$parents}
})
$self=$actual[$PID]
$chain=[Collections.Generic.List[object]]::new()
$next=$self.ParentProcessId
for($depth=0;$depth -lt 8 -and $next -gt 0;$depth++){
 Check
 $p=Get-CimInstance Win32_Process -Filter "ProcessId=$next" -Property ProcessId,CreationDate,CommandLine,ParentProcessId -OperationTimeoutSec 2 | Select-Object ProcessId,CreationDate,CommandLine,ParentProcessId
 if($null -eq $p){break}
 $chain.Add($p);$next=$p.ParentProcessId
}
$live=@($checks | Where-Object state -eq matching_live)
$unknown=@($checks | Where-Object state -eq present_unknown_identity)
$unproven=@($children | Where-Object {$_.relation -in @('unregistered_child_parent_identity_not_proven','child_of_matching_registered_parent_needs_review')})
$result=@{utc=[DateTime]::UtcNow.ToString('o');receipt_count=$receipts.Count;registered_identity_count=$records.Count;unique_pid_count=$ids.Count;self=$self;parent_chain=$chain;matching_live=$live.Count;unknown_current=$unknown.Count;unregistered_children=$children;unproven_child_count=$unproven.Count;actual_snapshot=@($actual.Values);checks=$checks;scope='registered PIDs and all observed direct children, no signals';limitation='current inspection/guardian processes must be checked after exit; missing historical argv is not reconstructed'}
[IO.File]::WriteAllText((Join-Path $task 'local-closure-postcommit.json'),($result | ConvertTo-Json -Depth 9),[Text.UTF8Encoding]::new($false))
Write-Output "matching=$($live.Count) unknown=$($unknown.Count) unproven_children=$($unproven.Count)"
if($live.Count -gt 0 -or $unknown.Count -gt 0 -or $unproven.Count -gt 0){throw 'closure identities require review'}
