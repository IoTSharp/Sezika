$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required'}
$session='sezika-public-data-20261010-moark-filter'
$wrapper='C:\Users\mysti\.codex\skills\playwright\scripts\playwright_cli.ps1'
$watch=[Diagnostics.Stopwatch]::StartNew()
$actions=@(
@{args=@('open','https://moark.com/datasets?task=text-classification','--browser','chrome')},
@{args=@('snapshot')},
@{args=@('requests')},
@{args=@('eval','document.body.innerText')},
@{args=@('goto','https://moark.com/docs/products/models/repositories')},
@{args=@('eval','document.body.innerText')},
@{args=@('requests')}
)
try {
 for($i=0;$i -lt $actions.Count;$i++){
  if($watch.Elapsed.TotalSeconds -ge 130 -or (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'CANCEL'))){throw 'Browser deadline/cancellation'}
  $a=$actions[$i].args
  & $wrapper ('-s='+$session) @a
  if($LASTEXITCODE -ne 0){throw ('Browser operation failed '+$i)}
 }
}finally {& $wrapper ('-s='+$session) close}
