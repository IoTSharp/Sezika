#requires -Version 7.0
param([Parameter(Mandatory)][ValidatePattern('^(repair|review)(0[0-9]|1[0-3])$')][string]$Batch,[int]$Start,[ValidateRange(1,6)][int]$Count,[ValidateSet('plan.json')][string]$SelectionFile='plan.json',[ValidateSet('Direct')][string]$Transport='Direct')
$ErrorActionPreference='Stop';$root=$PSScriptRoot;$clock=[Diagnostics.Stopwatch]::StartNew();$plan=Get-Content -Raw "$root/plan.json"|ConvertFrom-Json
$deadline=[DateTimeOffset]::Parse($plan.api_phase_started_utc).AddSeconds($plan.maximum_phase_seconds)
function Check {if($clock.Elapsed.TotalSeconds -gt 260 -or [DateTimeOffset]::UtcNow -ge $deadline -or (Test-Path "$root/cancel.request")){throw 'Worker260s/phase/cancellation'}}
function Save($Path,$Value){if(Test-Path $Path){throw 'Never overwrite evidence'};[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 65),[Text.UTF8Encoding]::new($false))}
$dir=Join-Path $root $Batch;if(Test-Path $dir){throw 'Batch already exists'};[void][IO.Directory]::CreateDirectory($dir)
$native=Get-CimInstance Win32_Process -Filter "ProcessId=$PID" -Property ProcessId,CreationDate,CommandLine,ParentProcessId -OperationTimeoutSec 5
$identity=@{Pid=$PID;Started=$native.CreationDate.ToUniversalTime().ToString('o');CommandLine=$native.CommandLine;ParentPid=$native.ParentProcessId};Save "$dir/reader.identity.json" $identity
$status='not_started';$failure=$null;$http=$null;$responseId=$null;$childStarted=$false
try{
 Check;$job=Get-Content -Raw "$root/inputs/$Batch.json"|ConvertFrom-Json -Depth 65
 if($job.batch -cne $Batch -or $job.start -ne $Start -or $job.count -ne $Count -or @($job.selected).Count -ne $Count -or $job.plan_sha256 -cne (Get-FileHash "$root/plan.json").Hash){throw 'Job identity/scope differs'}
 $body=$job.body|ConvertTo-Json -Depth 60 -Compress;$bytes=[Text.UTF8Encoding]::new($false).GetBytes($body)
 if($bytes.Length -gt 131072){throw 'Request byte cap'};[IO.File]::WriteAllBytes("$dir/request.json",$bytes)
 $seconds=[int][Math]::Floor([Math]::Min(240,($deadline-[DateTimeOffset]::UtcNow).TotalSeconds-8));if($seconds -lt 1){throw 'No request budget remaining'}
 Save "$dir/provenance.json" @{stage=$job.stage;batch=$Batch;source_ids=@($job.selected.record_id);request_sha256=(Get-FileHash "$dir/request.json").Hash;input_sha256=(Get-FileHash "$root/inputs/$Batch.json").Hash;plan_sha256=$job.plan_sha256;model='gpt-6.1-sol';effort='max';endpoint='https://sonnet.vip/v1/responses';credential_source='CC Switch selected sonnet_vip, memory only';worker_sha256=(Get-FileHash $PSCommandPath).Hash;transport_sha256=(Get-FileHash "$root/Send-TeacherDirect.py").Hash;labels_hidden_from_reaudit=($job.stage -ceq 'review');state_text_immutable=$true;formal=$false;human_review='pending'}
 $info=[Diagnostics.ProcessStartInfo]::new('C:\Users\mysti\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe');$info.UseShellExecute=$false;$info.CreateNoWindow=$true
 foreach($arg in @("$root/Send-TeacherDirect.py",$Batch,$seconds.ToString())){$info.ArgumentList.Add($arg)}
 $child=[Diagnostics.Process]::Start($info);$childStarted=$true
 try{
  $nativeChild=Get-CimInstance Win32_Process -Filter "ProcessId=$($child.Id)" -Property ProcessId,CreationDate,CommandLine,ParentProcessId -OperationTimeoutSec 5
  Save "$dir/transport.identity.json" @{Pid=$child.Id;Started=$child.StartTime.ToUniversalTime().ToString('o');CommandLine=$nativeChild.CommandLine;ParentPid=$PID;Arguments=@($info.ArgumentList)}
  for($poll=0;$poll -lt 245 -and -not $child.HasExited;$poll++){Check;[void]$child.WaitForExit(1000);if(($poll+1)%30 -eq 0){Write-Output "$Batch waiting $([int]$clock.Elapsed.TotalSeconds)s"}}
  if(-not $child.HasExited -or $child.ExitCode -ne 0){throw 'Transport failed; original attempt evidence retained'}
 }finally{$child.Dispose()}
 Check;$response=Get-Content -Raw "$dir/response.json"|ConvertFrom-Json -Depth 80;$responseId=$response.id;$http=200
 if($response.status -cne 'completed' -or $response.model -cne 'gpt-6.1-sol' -or ($response.reasoning.effort -and $response.reasoning.effort -cne 'max')){throw 'Response identity/status differs'}
 $texts=@($response.output|Where-Object type -CEQ message|ForEach-Object {$_.content}|Where-Object type -CEQ output_text);if($texts.Count -ne 1){throw 'Output text denominator'}
 if($job.stage -ceq 'repair'){. "$root/Test-RepairPayload.ps1";$validated=@(Test-RepairPayload -Json $texts[0].text -SelectedRows @($job.selected));Save "$dir/validated-proposals.json" $validated}else{. "$root/Test-LabelAuditPayload.ps1";$validated=@(Test-LabelAuditPayload -Json $texts[0].text -SelectedRows @($job.selected));Save "$dir/validated-reviews.json" $validated}
 Check;$status='completed_and_validated';Write-Output "$Batch validated $Count/$Count"
}catch{$status='failed';$failure=$_.Exception.Message;Write-Output "$Batch failed: $failure"}
finally{Save "$dir/result.json" @{status=$status;batch=$Batch;stage=$job.stage;count=$Count;response_id=$responseId;http_status=$http;failure=$failure;identity=$identity;children_started=$childStarted;elapsed_seconds=$clock.Elapsed.TotalSeconds;maximum_seconds=260;formal=$false;human_review='pending'}}
if($status -cne 'completed_and_validated'){exit 1}
