#requires -Version 7.0
param([switch]$Probe)
$ErrorActionPreference='Stop'
$auditTaskRoot='D:\source\Sezika\.artifacts\repaired-training-20261009'
$auditPermanentRoot='D:\source\Sezika\docs\evidence\teacher-repair-20261009-final'
$auditPrefix='docs/evidence/teacher-repair-20261009-final/'
$auditClock=[Diagnostics.Stopwatch]::StartNew()
function Check-Audit {if($auditClock.Elapsed.TotalSeconds -ge 35 -or (Test-Path -LiteralPath (Join-Path $auditTaskRoot 'audit-cancel.request'))){throw 'Git-byte audit35s/cancel'}}
function Assert-Audit([bool]$condition,[string]$message){Check-Audit;if(-not $condition){throw $message}}
$auditReader=Get-CimInstance Win32_Process -Filter "ProcessId=$PID" -Property ProcessId,CreationDate,CommandLine,ParentProcessId -OperationTimeoutSec 5
$auditStart=[Diagnostics.ProcessStartInfo]::new()
$auditStart.FileName='C:\Program Files\Git\cmd\git.exe';$auditStart.UseShellExecute=$false;$auditStart.CreateNoWindow=$true
$auditStart.RedirectStandardOutput=$true;$auditStart.RedirectStandardError=$true
$auditArguments=@('-C','D:\source\Sezika','ls-tree','-r','a804cc9','--','docs/evidence/teacher-repair-20261009-final')
foreach($auditArgument in $auditArguments){Check-Audit;$auditStart.ArgumentList.Add($auditArgument)}
$auditProcess=[Diagnostics.Process]::new();$auditProcess.StartInfo=$auditStart;$auditChild=$null
try {
    Assert-Audit ($auditProcess.Start()) 'Git ls-tree start'
    $auditChild=[ordered]@{pid=$auditProcess.Id;started=$auditProcess.StartTime.ToUniversalTime().ToString('o');executable=$auditStart.FileName;arguments=$auditArguments;parent_pid=$PID;observed_command_line=$null}
    $auditObserved=Get-CimInstance Win32_Process -Filter ('ProcessId='+$auditProcess.Id) -Property ProcessId,CreationDate,CommandLine,ParentProcessId -OperationTimeoutSec 5
    if($null -ne $auditObserved){$auditChild.observed_command_line=$auditObserved.CommandLine;Assert-Audit ($auditObserved.ParentProcessId -eq $PID -and [Math]::Abs(($auditObserved.CreationDate.ToUniversalTime()-$auditProcess.StartTime.ToUniversalTime()).Ticks) -le 10) 'Git child identity'}
    $auditOut=$auditProcess.StandardOutput.ReadToEndAsync();$auditErr=$auditProcess.StandardError.ReadToEndAsync()
    Assert-Audit ($auditProcess.WaitForExit(10000)) 'Git ls-tree10s timeout'
    Assert-Audit ($auditProcess.ExitCode -eq 0 -and $auditOut.IsCompleted -and $auditErr.IsCompleted) 'Git ls-tree exit/streams'
    $auditLines=@($auditOut.Result -split "`n"|Where-Object {$_ -ne ''})
    Assert-Audit ($auditLines.Count -eq 333) 'Git permanent object333'
    $auditLimit=if($Probe){1}else{333};$auditChecked=0;$auditCheckedBytes=0L;$auditRows=[Collections.Generic.List[object]]::new()
    foreach($auditLine in @($auditLines|Select-Object -First $auditLimit)){
        Check-Audit;Assert-Audit ($auditLine -match '^100644 blob ([0-9a-f]{40})\t(.+)$') 'Git blob entry'
        $auditExpected=$Matches[1];$auditPath=$Matches[2]
        Assert-Audit ($auditPath.StartsWith($auditPrefix,[StringComparison]::Ordinal)) 'Git path prefix'
        $auditRelative=$auditPath.Substring($auditPrefix.Length)
        $auditFullPath=[IO.Path]::GetFullPath((Join-Path $auditPermanentRoot $auditRelative))
        Assert-Audit ($auditFullPath.StartsWith($auditPermanentRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'Local path boundary'
        $auditBytes=[IO.File]::ReadAllBytes($auditFullPath);Assert-Audit ($auditBytes.Length -le 4MB) 'Git file4MiB cap'
        $auditHash=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA1)
        try{$auditHash.AppendData([Text.Encoding]::ASCII.GetBytes(('blob '+$auditBytes.Length+[char]0)));$auditHash.AppendData($auditBytes);$auditActual=[Convert]::ToHexString($auditHash.GetHashAndReset()).ToLowerInvariant()}finally{$auditHash.Dispose()}
        Assert-Audit ($auditActual -ceq $auditExpected) ('Git/local byte mismatch '+$auditRelative)
        $auditChecked++;$auditCheckedBytes+=$auditBytes.Length;$auditRows.Add([pscustomobject]@{path=$auditRelative;git_blob=$auditExpected;bytes=$auditBytes.Length})
        if($auditChecked -eq 1 -or $auditChecked -eq 333){Write-Output ('Git/local bytes checked '+$auditChecked+'/'+$auditLimit)}
    }
    $auditReport=[ordered]@{utc=[DateTimeOffset]::UtcNow.ToString('o');status='passed';probe=$Probe.IsPresent;commit='a804cc9';checked=$auditChecked;bytes=$auditCheckedBytes;seconds=$auditClock.Elapsed.TotalSeconds;reader=$auditReader|Select-Object ProcessId,CreationDate,CommandLine,ParentProcessId;git_child=$auditChild;git_exit=$auditProcess.ExitCode;git_stderr=$auditErr.Result;files=@($auditRows);limitation='Git child may exit before CIM argv sample; retained process handle records exact launch arguments, PID/start/parent and actual exit. No repository mutation or filters.';cleanup='Retained git process exited; no external child or disposable temp created.'}
    $auditOutput=Join-Path $auditTaskRoot $(if($Probe){'audit-git-bytes-probe.json'}else{'audit-git-bytes.json'})
    Assert-Audit (-not (Test-Path -LiteralPath $auditOutput)) 'Preserve prior Git audit'
    [IO.File]::WriteAllText($auditOutput,($auditReport|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
    Write-Output ('Git byte audit evidence='+$auditOutput)
} finally {
    if($null -ne $auditChild -and -not $auditProcess.HasExited){
        $auditCurrent=Get-CimInstance Win32_Process -Filter ('ProcessId='+$auditProcess.Id) -Property ProcessId,CreationDate,CommandLine,ParentProcessId -OperationTimeoutSec 5
        if($null -ne $auditCurrent -and $auditCurrent.ParentProcessId -eq $PID -and [Math]::Abs(($auditCurrent.CreationDate.ToUniversalTime()-$auditProcess.StartTime.ToUniversalTime()).Ticks) -le 10 -and $auditCurrent.CommandLine.Contains('ls-tree') -and $auditCurrent.CommandLine.Contains('a804cc9')){$auditProcess.Kill($true);[void]$auditProcess.WaitForExit(3000)}else{Write-Output 'Git cleanup ownership incomplete; preserved process and reported limitation'}
    }
    $auditProcess.Dispose()
}
