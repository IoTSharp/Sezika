#requires -Version 7.0
param([switch]$Tiny)
$ErrorActionPreference='Stop'
$task=[IO.Path]::GetFullPath($PSScriptRoot)
$clock=[Diagnostics.Stopwatch]::StartNew()
$moved=[Collections.Generic.List[object]]::new()
$removed=[Collections.Generic.List[string]]::new()
$retained=[Collections.Generic.List[string]]::new()
$status='started'
function Check {if($clock.Elapsed.TotalSeconds -ge 35 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'temp deadline/cancellation'}}
try{
 foreach($name in @('compiler-temp','linux-compiler-temp')){
  Check
  $root=[IO.Path]::GetFullPath((Join-Path $task $name))
  if($root -cne (Join-Path $task $name) -or -not $root.StartsWith($task+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'temp root differs'}
  $top=@(Get-ChildItem -LiteralPath $root -Force | Select-Object -First 65)
  if($top.Count -gt 64){throw 'top entry cap64'}
  if($Tiny){Write-Output "tiny root=$root entries=$($top.Count) no mutation";exit 0}
  $files=@($top | Where-Object {-not $_.PSIsContainer})
  if($files.Count -gt 32){throw 'log cap32'}
  foreach($file in $files){
   Check
   if(($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $file.Name -notmatch '^Microsoft\.NET\.Workload_[0-9]+_20261009_[0-9_]+\.log$'){throw 'unrecognized task temp file retained'}
   $absolute=[IO.Path]::GetFullPath($file.FullName)
   if(-not $absolute.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'log escaped temp root'}
   $hash=(Get-FileHash -LiteralPath $absolute).Hash
   $destination=Join-Path $task ('compiler-temp-retained/'+$name+'/'+$file.Name)
   [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
   [IO.File]::Copy($absolute,$destination,$false)
   if((Get-FileHash -LiteralPath $destination).Hash -cne $hash){throw 'temp log mirror differs'}
   # Only this exact verified file is removed; the byte-identical log is retained.
   if((Get-FileHash -LiteralPath $absolute).Hash -cne $hash){throw 'temp log changed before removal'}
   Remove-Item -LiteralPath $absolute -Force
   $moved.Add([pscustomobject]@{source=$absolute;retained=$destination;bytes=$file.Length;sha256=$hash})
  }
  $directories=[Collections.Generic.List[string]]::new()
  $stack=[Collections.Generic.Stack[object]]::new()
  $stack.Push(@{path=$root;depth=0})
  for($i=0;$i -lt 128 -and $stack.Count -gt 0;$i++){
   Check
   $entry=$stack.Pop()
   $children=@(Get-ChildItem -LiteralPath $entry.path -Force | Select-Object -First 65)
   if($children.Count -gt 64){throw 'directory entry cap64'}
   foreach($child in $children){
    Check
    if(($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'temp reparse point retained'}
    if($child.PSIsContainer){
     if($entry.depth -ge 8){throw 'temp depth cap8'}
     $path=[IO.Path]::GetFullPath($child.FullName)
     if(-not $path.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'directory escaped root'}
     $directories.Add($path);$stack.Push(@{path=$path;depth=$entry.depth+1})
    }else{$retained.Add($child.FullName)}
   }
  }
  if($stack.Count -gt 0){throw 'temp directory cap128'}
  $ordered=@($directories | Sort-Object Length -Descending)
  foreach($path in $ordered){
   Check
   if(@(Get-ChildItem -LiteralPath $path -Force | Select-Object -First 1).Count -eq 0){[IO.Directory]::Delete($path,$false);$removed.Add($path)}
   else{$retained.Add($path)}
  }
  Write-Output "temp=$name logs_retained=$($files.Count) directories_checked=$($ordered.Count)"
 }
 $status='completed'
}finally{
 if(-not $Tiny){[IO.File]::WriteAllText((Join-Path $task 'compiler-temp-closure.json'),(@{utc=[DateTime]::UtcNow.ToString('o');status=$status;log_count=$moved.Count;logs=$moved;removed_empty_directories=$removed;retained_unknown=$retained;scope='two exact task compiler temp roots; exact log SHA mirrored before removal; non-recursive empty directory deletion; no shared TEMP or Linux failure tmp touched'} | ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))}
}
