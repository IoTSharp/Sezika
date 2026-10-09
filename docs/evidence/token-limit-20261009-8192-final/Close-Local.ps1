$ErrorActionPreference='Stop';$task=$PSScriptRoot;$clock=[Diagnostics.Stopwatch]::StartNew()
$receipts=@(Get-ChildItem -LiteralPath (Join-Path $task 'process-logs') -File -Filter '*.result.json'|Select-Object -First 129)
if($receipts.Count -gt 128){throw 'receipt cap'}
$records=[Collections.Generic.List[object]]::new();$ids=[Collections.Generic.HashSet[int]]::new()
foreach($file in $receipts){
 if($clock.Elapsed.TotalSeconds -ge 30){throw 'registration timeout'}
 $r=Get-Content -Raw -LiteralPath $file.FullName|ConvertFrom-Json
 foreach($p in @($r)+@($r.Processes)+@($r.Launcher)){
  if($null -eq $p -or $null -eq $p.Pid){continue}
  if($records.Count -ge 512){throw 'identity cap'}
  $records.Add([pscustomobject]@{source=$file.Name;pid=[int]$p.Pid;start=$p.Started;argv=$p.CommandLine;parent=$p.ParentPid;historical_status=$r.Status;historical_unknown=$r.UnknownCount})
  [void]$ids.Add([int]$p.Pid)
 }
}
[void]$ids.Add(74388);[void]$ids.Add(73880)
$all=@($ids);$actual=@{}
for($i=0;$i -lt $all.Count -and $i -lt 256;$i+=16){
 if($clock.Elapsed.TotalSeconds -ge 60){throw 'process snapshot deadline'}
 $batch=@($all|Select-Object -Skip $i -First 16)
 $filter=(@($batch|ForEach-Object {"ProcessId=$_ OR ParentProcessId=$_"})) -join ' OR '
 $snapshot=@(Get-CimInstance Win32_Process -Filter $filter -Property ProcessId,CreationDate,CommandLine,ParentProcessId -OperationTimeoutSec 2|Select-Object -First 129)
 if($snapshot.Count -gt 128){throw 'snapshot cap'}
 foreach($p in $snapshot){$actual[[int]$p.ProcessId]=$p|Select-Object ProcessId,CreationDate,CommandLine,ParentProcessId}
}
$checks=@(foreach($r in $records){
 if($clock.Elapsed.TotalSeconds -ge 70){throw 'comparison timeout'}
 $p=$actual[$r.pid];$state='absent'
 if($r.pid -eq $PID){$state='current_closure_launcher_check_after_exit'}
 elseif($null -ne $p){
  if($r.start -and [Math]::Abs((([DateTime]$r.start).ToUniversalTime()-$p.CreationDate.ToUniversalTime()).Ticks) -gt 10){$state='reused'}
  elseif($r.argv -and $r.argv -ceq $p.CommandLine -and $r.parent -eq $p.ParentProcessId){$state='matching_live'}
  else{$state='present_unknown_identity'}
 }
 [pscustomobject]@{source=$r.source;pid=$r.pid;expected_start=$r.start;state=$state;actual=$p;historical_status=$r.historical_status;historical_unknown=$r.historical_unknown}
})
$live=@($checks|Where-Object state -eq matching_live);$unknown=@($checks|Where-Object state -eq present_unknown_identity)
[IO.File]::WriteAllText((Join-Path $task 'local-process-closure.json'),(@{utc=[DateTime]::UtcNow.ToString('o');receipts=$receipts.Count;registered_records=$records.Count;unique_pids=$all.Count;matching_live=$live.Count;unknown_current=$unknown.Count;checks=$checks;scope='only registered PIDs and their direct children; read only; no signals';limitation='later absent does not repair short-lived historical missing argv; current closure launcher checked separately after exit'}|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
Write-Output "receipts=$($receipts.Count) identities=$($records.Count) unique=$($all.Count) matching=$($live.Count) unknown=$($unknown.Count)"
if($live.Count -gt 0 -or $unknown.Count -gt 0){throw 'task identity closure needs review'}
