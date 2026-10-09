#requires -Version 7.0
$ErrorActionPreference='Stop'
$clock=[Diagnostics.Stopwatch]::StartNew()
$cancel=Join-Path $PSScriptRoot 'cancel.request'
$target=Join-Path $PSScriptRoot 'reboot-direct-tcp-probe-final.json'
if([IO.File]::Exists($target)){throw 'one direct probe already recorded'}
$client=[Net.Sockets.TcpClient]::new()
$connected=$false
$errorText=$null
try {
 if([IO.File]::Exists($cancel)){throw 'cancelled before probe'}
 $connect=$client.ConnectAsync('211.149.170.42',2222)
 for($step=0;$step -lt 40 -and $clock.Elapsed.TotalSeconds -lt 8;$step++){
  if([IO.File]::Exists($cancel)){throw 'cancelled during probe'}
  if($connect.IsCompleted){$connect.GetAwaiter().GetResult();$connected=$client.Connected;break}
  Start-Sleep -Milliseconds 200
 }
 if(-not $connect.IsCompleted){throw 'direct TCP8s/40checks deadline'}
}catch{$errorText=$_.Exception.Message}
finally{
 $client.Dispose()
 [IO.File]::WriteAllText($target,(@{utc=[DateTime]::UtcNow.ToString('o');target='211.149.170.42:2222';connected=$connected;error=$errorText;elapsed_seconds=$clock.Elapsed.TotalSeconds;scope='one direct TCP probe; max40checks/8s; no proxy or remote mutation'}|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
}
Get-Content -LiteralPath $target
