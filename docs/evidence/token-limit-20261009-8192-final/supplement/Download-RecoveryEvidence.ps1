#requires -Version 7.0
$ErrorActionPreference='Stop'
$task=$PSScriptRoot
$clock=[Diagnostics.Stopwatch]::StartNew()
function Check {if($clock.Elapsed.TotalSeconds -ge 90 -or [IO.File]::Exists((Join-Path $task 'cancel.request'))){throw 'recovery download deadline/cancellation'}}
function Run([string]$Label,[string]$File,[string[]]$Arguments,[int]$Seconds){
 Check
 try {& (Join-Path $task 'Invoke-BoundedProcess.ps1') -FilePath $File -ArgumentList $Arguments -TimeoutSeconds $Seconds -LogName $Label -ProcessQueryTimeoutSeconds 2 -CancelFile (Join-Path $task 'cancel.request') | Out-Null}catch{Write-Output ('guardian retained: '+$_.Exception.Message)}
 $files=@(Get-ChildItem -LiteralPath (Join-Path $task 'process-logs') -File -Filter ('*'+$Label+'.result.json') | Select-Object -First 2)
 if($files.Count -ne 1){throw 'single receipt required'}
 $r=Get-Content -Raw -LiteralPath $files[0].FullName | ConvertFrom-Json
 if($null -eq $r.ExitCode -or $r.ExitCode -ne 0){throw ('actual child exit failed: '+$Label)}
 return $r
}
$tar=Join-Path $task 'recovery-evidence.tar'
if(Test-Path -LiteralPath $tar){throw 'existing recovery tar retained; inspect before reuse'}
$download=Run 'download-recovery-evidence' 'C:\Windows\System32\OpenSSH\scp.exe' @('-P','2222','-o','BatchMode=yes','-o','StrictHostKeyChecking=yes','-o','ConnectTimeout=8','root@211.149.170.42:/root/sezika-experiments/20261009-repaired-training-8192/recovery-evidence.tar',$tar) 30
if((Get-FileHash -LiteralPath $tar).Hash -cne '0A10AE919521C7A8F52CF31980DB5E51BB23E6460DD36C3F720E18C217ABB94A' -or (Get-Item -LiteralPath $tar).Length -ne 143360){throw 'recovery tar bytes/SHA differ'}
$list=Run 'list-recovery-evidence' 'C:\Windows\System32\tar.exe' @('-tf',$tar) 10
$entries=@([IO.File]::ReadAllLines($list.StdoutPath))
if($entries.Count -gt 512){throw 'tar entry cap512'}
foreach($entry in $entries){Check;if($entry.StartsWith('/') -or $entry -match '(^|[\/])\.\.([\/]|$)' -or $entry.Contains(':')){throw 'unsafe tar member'} }
$types=Run 'types-recovery-evidence' 'C:\Windows\System32\tar.exe' @('-tvf',$tar) 10
$typeLines=@([IO.File]::ReadAllLines($types.StdoutPath))
if($typeLines.Count -ne $entries.Count -or @($typeLines | Where-Object {$_ -notmatch '^[-d]'}).Count -gt 0){throw 'non-regular tar member retained; no extraction'}
$destination=Join-Path $task 'recovery-retrieved'
if(Test-Path -LiteralPath $destination){throw 'fresh recovery extract root required'}
[IO.Directory]::CreateDirectory($destination) | Out-Null
$extract=Run 'extract-recovery-evidence' 'C:\Windows\System32\tar.exe' @('-xf',$tar,'-C',$destination) 15
[IO.File]::WriteAllText((Join-Path $task 'recovery-download.json'),(@{utc=[DateTime]::UtcNow.ToString('o');path=$tar;sha256=(Get-FileHash -LiteralPath $tar).Hash;bytes=(Get-Item -LiteralPath $tar).Length;members=$entries.Count;extracted=$destination;actual_download_exit=$download.ExitCode;actual_extract_exit=$extract.ExitCode;type_scope='only regular files/directories, no links or parent/absolute paths'} | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Write-Output "recovery_tar_verified_and_extracted=true members=$($entries.Count)"
