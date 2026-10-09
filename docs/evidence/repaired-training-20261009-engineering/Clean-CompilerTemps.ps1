#requires -Version 7.0
$ErrorActionPreference='Stop'
$task='D:\source\Sezika\.artifacts\repaired-training-20261009'
$clock=[Diagnostics.Stopwatch]::StartNew()
$archive=Join-Path $task 'compiler-log-evidence'
[void][IO.Directory]::CreateDirectory($archive)
$records=[Collections.Generic.List[object]]::new()
$count=0
function Check {if($clock.Elapsed.TotalSeconds -ge 35 -or $count -gt 120 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'Temp cleanup bound/cancel'}}
foreach($relative in @('helper-compiler-temp','linux-compiler-temp','audit-length-reference/compiler-temp')){
 Check
 $base=[IO.Path]::GetFullPath((Join-Path $task $relative))
 if(-not $base.StartsWith($task+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Temp root escaped task'}
 if(-not (Test-Path -LiteralPath $base)){continue}
 $rootItem=Get-Item -LiteralPath $base
 if(($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Linked compiler root rejected'}
 $queue=[Collections.Generic.Queue[object]]::new();$queue.Enqueue(@{path=$base;depth=0})
 $directories=[Collections.Generic.List[string]]::new()
 for($iteration=0;$iteration -lt 40 -and $queue.Count -gt 0;$iteration++){
  Check;$item=$queue.Dequeue();$directories.Add($item.path)
  $entries=@(Get-ChildItem -LiteralPath $item.path -Force|Select-Object -First 31)
  if($entries.Count -gt 30){throw 'Compiler directory items cap30'}
  foreach($entry in $entries){
   $count++;Check
   $absolute=[IO.Path]::GetFullPath($entry.FullName)
   if(-not $absolute.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase) -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Compiler child ownership/link mismatch'}
   if($entry.PSIsContainer){if($item.depth -ge 3){$records.Add(@{path=$absolute;action='preserved_depth_cap'})}else{$queue.Enqueue(@{path=$absolute;depth=$item.depth+1})}}
   elseif($entry.Name -cmatch '^Microsoft\.NET\.Workload_[0-9]+_[0-9_]+\.log$' -and $entry.Length -le 64KB){
    $sha=(Get-FileHash -LiteralPath $absolute).Hash
    $destination=Join-Path $archive ($relative.Replace('/','-')+'-'+$entry.Name)
    if(Test-Path -LiteralPath $destination){throw 'Preserve existing compiler archive'}
    Move-Item -LiteralPath $absolute -Destination $destination
    if((Get-FileHash -LiteralPath $destination).Hash -cne $sha){throw 'Compiler log archive hash differs'}
    $records.Add(@{path=$absolute;action='moved_exact_owned_log';destination=$destination;sha256=$sha})
   }else{$records.Add(@{path=$absolute;action='preserved_unrecognized_task_file';bytes=$entry.Length})}
  }
 }
 if($queue.Count -gt 0){throw 'Compiler directory queue cap40'}
 # Only exact empty directories; no recursive delete and no user/shared cache scope.
 foreach($directory in @($directories|Sort-Object Length -Descending)){
  Check
  if(@(Get-ChildItem -LiteralPath $directory -Force|Select-Object -First 1).Count -eq 0){[IO.Directory]::Delete($directory,$false);$records.Add(@{path=$directory;action='removed_exact_empty_directory'})}
 }
}
[IO.File]::WriteAllText((Join-Path $task 'compiler-temp-cleanup.json'),(@{utc=[DateTime]::UtcNow.ToString('o');items=$count;seconds=$clock.Elapsed.TotalSeconds;scope='new exclusive task compiler roots; exact logs moved by hash, empty nonrecursive directory deletes';records=$records}|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
Write-Output "compiler_temp_checked_items=$count seconds=$($clock.Elapsed.TotalSeconds)"
