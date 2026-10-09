#requires -Version 7.0
$ErrorActionPreference='Stop'
$task='D:\source\Sezika\.artifacts\repaired-training-20261009'
$source='D:\source\Sezika\docs\evidence\teacher-repair-20261009-final'
$clock=[Diagnostics.Stopwatch]::StartNew()
function Check {if($clock.Elapsed.TotalSeconds -ge 60 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'Inputs cancelled/deadline'}}
function Copy-One([string]$From,[string]$To){
 Check
 $entry=Get-Item -LiteralPath $From
 if($entry.PSIsContainer -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $entry.Length -gt 5MB){throw 'Input regular file/size mismatch'}
 [IO.File]::Copy($From,$To,$false)
 if((Get-FileHash -LiteralPath $From).Hash -cne (Get-FileHash -LiteralPath $To).Hash){throw 'Input copy hash differs'}
}
if(Test-Path -LiteralPath (Join-Path $task 'data')){throw 'Fresh inputs required; preserve prior copies'}
[void][IO.Directory]::CreateDirectory((Join-Path $task 'data/repair-evidence'))
$files=@('experiment-manifest.json','experiment-records.jsonl','input-provenance.json','source-catalog.json','teacher-requests.json','teacher-responses.json','derivation-ledger.json')
# Tiny first file provides a copy/hash probe before the bounded seven-file batch.
Copy-One (Join-Path $source 'data-v3/experiment-manifest.json') (Join-Path $task 'data/experiment-manifest.json')
for($i=1;$i -lt 7;$i++){Check;Copy-One (Join-Path $source ('data-v3/'+$files[$i])) (Join-Path $task ('data/'+$files[$i]))}
Copy-One 'D:\source\Sezika\docs\evidence\teacher-expansion-20261009-final\prepared-records.jsonl' (Join-Path $task 'data/parent-records.jsonl')
if((Get-FileHash -LiteralPath (Join-Path $task 'data/experiment-manifest.json')).Hash -cne 'E8E74CE0BBF1A72DA5FCDF4DCAD9D302513DC5DC42CB548BFD21D2569621AEB6'){throw 'Pinned manifest differs'}
if((Get-FileHash -LiteralPath (Join-Path $task 'data/derivation-ledger.json')).Hash -cne '55F3226946831C4B9CBD7F6EE10D1194764860C8343A9386CAA40C678CE24EC0'){throw 'Pinned ledger differs'}
for($batch=0;$batch -lt 14;$batch++){
 Check
 $name='repair'+$batch.ToString('00')
 foreach($kind in @('request','response')){Check;Copy-One (Join-Path $source ($name+'/'+$kind+'.json')) (Join-Path $task ('data/repair-evidence/'+$name+'.'+$kind+'.json'))}
 Write-Output "repair_input_copied=$name/14"
}
[void][IO.Directory]::CreateDirectory((Join-Path $task 'regression_input'))
foreach($name in @('experiment-records.jsonl','audit-report.json')){Check;Copy-One (Join-Path 'D:\source\Sezika\.artifacts\teacher-expansion-20261008\regression-input' $name) (Join-Path $task ('regression_input/'+$name))}
Write-Output 'immutable_v3_parent_repair_and_regression_inputs_copied=true'
