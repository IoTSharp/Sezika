#requires -Version 7.0
param([switch]$Probe)
$ErrorActionPreference='Stop';$root=$PSScriptRoot;$clock=[Diagnostics.Stopwatch]::StartNew();$destination='D:\source\Sezika\docs\evidence\teacher-repair-20261009-final'
function Check {if($clock.Elapsed.TotalSeconds -gt 180 -or (Test-Path "$root/cancel.request")){throw 'Archive180s/cancel'}}
$items=[Collections.Generic.List[string]]::new()
$items.Add('Verify-Package.ps1');$items.Add('package-verification.json');$items.Add('Build-HttpLedger.ps1')
foreach($name in @('plan.json','state.json','repair-schedule.json','review-schedule.json','repair-queue-result.json','review-queue-result.json','validator-probe.json','root-semantic-exclusions.json','semantic-review.json','revised-drafts.json','final-summary.json','repair-deletion-report.md','process-closure.json','closure-followup.json','http-ledger.json','Prepare-Jobs.ps1','Test-RepairPayload.ps1','Test-LabelAuditPayload.ps1','Invoke-TeacherBatch.ps1','Invoke-OwnedApiProcess.ps1','Send-TeacherDirect.py','Build-RepairedPackage.ps1','Close-RepairProcesses.ps1','Archive-RepairEvidence.ps1')){Check;$items.Add($name)}
foreach($stage in @('repair','review')){
 $schedule=Get-Content -Raw "$root/$stage-schedule.json"|ConvertFrom-Json
 if($schedule.batches.Count -gt 14){throw 'Stage batch cap14'}
 foreach($job in $schedule.batches){Check;$items.Add("inputs/$($job.batch).json");foreach($file in @('reader.identity.json','request.json','provenance.json','transport.identity.json','response-attempt1.json','response-attempt2.json','response.json','transport-result.json','result.json','validated-proposals.json','validated-reviews.json')){Check;$path="$($job.batch)/$file";if(Test-Path "$root/$path"){$items.Add($path)}}}
}
foreach($file in @('experiment-manifest.json','experiment-records.jsonl','source-catalog.json','input-provenance.json','teacher-requests.json','teacher-responses.json','derivation-ledger.json')){$items.Add("data-v3/$file")}
$logs=@(Get-ChildItem -LiteralPath "$root/api-owned-logs" -File|Select-Object -First 151);if($logs.Count -gt 150){throw 'Log cap150'}
foreach($file in $logs){Check;$items.Add('api-owned-logs/'+$file.Name)}
if($items.Count -gt 450){throw 'Archive cap450'}
if(-not $Probe -and (Test-Path $destination)){throw 'Archive already exists'}
$inventory=[Collections.Generic.List[object]]::new();$bytes=0L
for($i=0;$i -lt $(if($Probe){1}else{$items.Count});$i++){
 Check;$relative=$items[$i];$source=Join-Path $root $relative;$file=Get-Item -LiteralPath $source
 if($file.PSIsContainer -or ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $file.Length -gt 16MB){throw 'Archive regular file/size'};$bytes+=$file.Length;if($bytes -gt 64MB){throw 'Archive total64MiB'}
 $sha=(Get-FileHash -LiteralPath $source).Hash
 if($Probe){Write-Output 'Archive tiny source identity passed; no copy';exit 0}
 $target=Join-Path $destination $relative;[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target));[IO.File]::Copy($source,$target,$false)
 if((Get-FileHash $target).Hash -cne $sha){throw 'Copy hash mismatch'};$inventory.Add(@{path=$relative;bytes=$file.Length;sha256=$sha})
 if(($i+1)%50 -eq 0){Write-Output "Archived $($i+1)/$($items.Count)"}
}
$parent='D:\source\Sezika\docs\evidence\teacher-expansion-20261009-complete\inventory.json'
[IO.File]::WriteAllText("$destination/inventory.json",(@{utc=[DateTimeOffset]::UtcNow.ToString('o');files=@($inventory);total_bytes=$bytes;parent_inventory='../teacher-expansion-20261009-complete/inventory.json';parent_inventory_sha256=(Get-FileHash $parent).Hash;formal=$false;model_quality_passed=$false;training_executed=$false}|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
Write-Output "Archived $($items.Count) repair evidence files, $bytes bytes."
