param(
    [Parameter(Mandatory)][string]$PlanPath,
    [string]$OutputRoot='C:\Users\mysti\Documents\SezikaData\public-decision-2026-10-10',
    [ValidateRange(5,1800)][int]$MaxSeconds=900,
    [ValidateRange(5,600)][int]$RequestSeconds=90,
    [string]$LedgerName='download-ledger.jsonl',
    [ValidateSet('OfficialProxy','MirrorDirect')][string]$Transport='OfficialProxy',
    [string]$HFCredentialTarget
)
$ErrorActionPreference='Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) {throw 'PowerShell 7 required'}
$OutputRoot=[IO.Path]::GetFullPath($OutputRoot)
[void][IO.Directory]::CreateDirectory($OutputRoot)
$cancelPath=Join-Path $OutputRoot 'CANCEL'
$plan=@(Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json)
$watch=[Diagnostics.Stopwatch]::StartNew()
$started=[DateTime]::UtcNow
$ledger=Join-Path $OutputRoot $LedgerName
$receiptPath=Join-Path $OutputRoot ('batch-'+$started.ToString('yyyyMMdd-HHmmss-fff')+'-'+$PID+'.json')
$credCode=@'
using System;
using System.Runtime.InteropServices;
public static class PublicDataCredential {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] public struct CREDENTIAL { public uint Flags; public uint Type; public string TargetName; public string Comment; public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten; public uint CredentialBlobSize; public IntPtr CredentialBlob; public uint Persist; public uint AttributeCount; public IntPtr Attributes; public string TargetAlias; public string UserName; }
 [DllImport("advapi32.dll",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CredRead(string target,uint type,uint flags,out IntPtr credential);
 [DllImport("advapi32.dll")] static extern void CredFree(IntPtr credential);
 public static string Read(string target) {IntPtr p; if(!CredRead(target,1,0,out p)) return null; try {var c=Marshal.PtrToStructure<CREDENTIAL>(p);return Marshal.PtrToStringUni(c.CredentialBlob,(int)c.CredentialBlobSize/2);} finally {CredFree(p);} }
}
public static class PublicGitBlob {
 public static string Sha1(string path) {
  using var input=new System.IO.FileStream(path,System.IO.FileMode.Open,System.IO.FileAccess.Read,System.IO.FileShare.Read);
  using var hash=System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA1);
  hash.AppendData(System.Text.Encoding.UTF8.GetBytes("blob "+input.Length+"\0"));
  var buffer=new byte[1024*1024];var watch=System.Diagnostics.Stopwatch.StartNew();
  for(long i=0;i<=input.Length;i++){
   if(watch.Elapsed.TotalSeconds>10)throw new System.TimeoutException("Git blob validation bound");
   int count=input.Read(buffer,0,buffer.Length);if(count==0)break;hash.AppendData(buffer,0,count);
  }
  return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
 }
}
'@
Add-Type -TypeDefinition $credCode
$githubSecret=[PublicDataCredential]::Read('Codex:GitHub:maikebing')
$hfSecret=if($HFCredentialTarget){[PublicDataCredential]::Read($HFCredentialTarget)}else{$null}
$handler=[Net.Http.HttpClientHandler]::new()
# Direct GitHub/HF TLS failed in the current task; use this per-client fallback only.
$handler.UseProxy=($Transport -ne 'MirrorDirect')
$handler.Proxy=[Net.WebProxy]::new('http://127.0.0.1:7890')
$client=[Net.Http.HttpClient]::new($handler)
$client.Timeout=[Threading.Timeout]::InfiniteTimeSpan
$fallbackHandler=[Net.Http.HttpClientHandler]::new()
$fallbackHandler.UseProxy=$true;$fallbackHandler.Proxy=[Net.WebProxy]::new('http://127.0.0.1:7890')
$fallbackClient=[Net.Http.HttpClient]::new($fallbackHandler);$fallbackClient.Timeout=[Threading.Timeout]::InfiniteTimeSpan
$results=[Collections.Generic.List[object]]::new()
$known=@{}
$priorLedgers=@(Get-ChildItem -LiteralPath $OutputRoot -File -Filter '*ledger*.jsonl')
for ($l=0; $l -lt $priorLedgers.Count; $l++) {
    if ($watch.Elapsed.TotalSeconds -ge $MaxSeconds) {throw 'Ledger scan deadline'}
    $oldLines=@(Get-Content -LiteralPath $priorLedgers[$l].FullName)
    for ($i=0; $i -lt $oldLines.Count; $i++) {
        if ($watch.Elapsed.TotalSeconds -ge $MaxSeconds) {throw 'Ledger read deadline'}
        $old=$oldLines[$i]|ConvertFrom-Json
        if ($old.status -eq 'downloaded' -and (Test-Path -LiteralPath $old.path)) {$known[$old.url]=$old}
    }
}
try {
    for ($index=0; $index -lt $plan.Count; $index++) {
        if ($watch.Elapsed.TotalSeconds -ge $MaxSeconds -or (Test-Path -LiteralPath $cancelPath)) {break}
        $item=$plan[$index]
        if ($known.ContainsKey($item.url)) {Write-Output ('Cached '+$item.name); continue}
        $relative=[string]$item.path
        $destination=[IO.Path]::GetFullPath((Join-Path $OutputRoot $relative))
        if (-not $destination.StartsWith($OutputRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {throw 'Destination outside task root'}
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
        if (Test-Path -LiteralPath $destination) {throw ('Untracked destination exists: '+$destination)}
        $partial=$destination+'.partial'
        if (Test-Path -LiteralPath $partial) {throw ('Retained prior partial requires review: '+$partial)}
        $useMirror=$Transport -eq 'MirrorDirect' -and ([Uri]$item.url).Host -eq 'huggingface.co' -and ($item.expected_sha256 -or $item.expected_git_blob_sha1)
        $mirrorUrl=if($useMirror){$item.url.Replace('https://huggingface.co/','https://hf-mirror.com/')}else{$null}
        $record=[ordered]@{name=$item.name;kind=$item.kind;dataset=$item.dataset;split=$item.split;url=$item.url;path=$destination;started_utc=[DateTime]::UtcNow.ToString('o');status='failed';bytes=0;sha256=$null;http_status=$null;attempts=0;expected_sha256=$item.expected_sha256;expected_git_blob_sha1=$item.expected_git_blob_sha1;git_blob_sha1=$null;source_revision=$item.revision;error=$null;transport=if($useMirror){'MirrorDirect with upstream hash'}else{'OfficialProxy'};mirror_url=$mirrorUrl}
        for ($attempt=0; $attempt -lt 2; $attempt++) {
            if ($watch.Elapsed.TotalSeconds -ge $MaxSeconds -or (Test-Path -LiteralPath $cancelPath)) {break}
            $request=$null;$response=$null;$inputStream=$null;$outputStream=$null;$cts=$null
            $record.attempts++
            try {
                $budget=[Math]::Max(1,[Math]::Min($RequestSeconds,$MaxSeconds-[int]$watch.Elapsed.TotalSeconds))
                $cts=[Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($budget))
                $requestUrl=if($useMirror -and $attempt -eq 0){$mirrorUrl}else{$item.url}
                $activeClient=if($useMirror -and $attempt -eq 0){$client}else{$fallbackClient}
                $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get,[string]$requestUrl)
                $request.Headers.UserAgent.ParseAdd('Sezika-Public-Decision-Data/1.0')
                if (([Uri]$item.url).Host -eq 'api.github.com' -and $githubSecret) {$request.Headers.Authorization=[Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer',$githubSecret)}
                # Use an already-authorized account only on the official host.
                # A 401/403 is retained; this collector never accepts gated terms.
                if (([Uri]$requestUrl).Host -eq 'huggingface.co' -and $hfSecret) {$request.Headers.Authorization=[Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer',$hfSecret)}
                $response=$activeClient.SendAsync($request,[Net.Http.HttpCompletionOption]::ResponseHeadersRead,$cts.Token).GetAwaiter().GetResult()
                $record.http_status=[int]$response.StatusCode
                if (-not $response.IsSuccessStatusCode) {throw ('HTTP '+[int]$response.StatusCode)}
                $drive=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($destination))
                $length=$response.Content.Headers.ContentLength
                if ($drive.AvailableFreeSpace -lt 2GB -or ($null -ne $length -and $drive.AvailableFreeSpace -lt ($length+2GB))) {throw 'Insufficient disk space with 2GiB operational reserve'}
                $inputStream=$response.Content.ReadAsStreamAsync($cts.Token).GetAwaiter().GetResult()
                $outputStream=[IO.FileStream]::new($partial,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read,1048576)
                $buffer=[byte[]]::new(1048576)
                $contentBytes=[int64]0
                # Max chunk count derives from declared content length; unknown-length
                # responses use the finite filesystem capacity, not an arbitrary data cap.
                # A network ReadAsync may return one byte; a bound based on full
                # buffer-sized reads would incorrectly truncate small responses.
                $maxChunks=if ($null -ne $length) {[int64]$length+1} else {[int64]$drive.AvailableFreeSpace-2GB+1}
                for ($chunk=0L; $chunk -lt $maxChunks; $chunk++) {
                    $cts.Token.ThrowIfCancellationRequested()
                    if (Test-Path -LiteralPath $cancelPath) {throw 'Cancelled by task marker'}
                    $read=$inputStream.ReadAsync($buffer,0,$buffer.Length,$cts.Token).GetAwaiter().GetResult()
                    if ($read -eq 0) {break}
                    $outputStream.Write($buffer,0,$read)
                    $contentBytes+=$read
                    if (($chunk % 32) -eq 0 -and ([IO.DriveInfo]::new($drive.Name)).AvailableFreeSpace -lt 2GB) {throw 'Disk reserve reached'}
                    if (($chunk % 64) -eq 0 -and $chunk -gt 0) {Write-Output ($item.name+' bytes='+$contentBytes)}
                }
                $outputStream.Dispose();$outputStream=$null
                if ($null -ne $length -and $contentBytes -ne $length) {throw 'Content length mismatch'}
                $digest=(Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash.ToLowerInvariant()
                if ($item.expected_sha256 -and $digest -cne $item.expected_sha256.ToLowerInvariant()) {throw 'Upstream SHA256 mismatch'}
                if(-not $item.expected_sha256 -and $item.expected_git_blob_sha1){
                    $blob=[PublicGitBlob]::Sha1($partial);$record.git_blob_sha1=$blob
                    if($blob -cne $item.expected_git_blob_sha1){throw 'Upstream Git blob SHA1 mismatch'}
                }
                Move-Item -LiteralPath $partial -Destination $destination
                $record.bytes=$contentBytes;$record.sha256=$digest;$record.status='downloaded';$record.error=$null
                break
            } catch {
                $record.error=$_.Exception.Message
                if ($record.error -match 'HTTP (400|401|403|404)|Insufficient|reserve|Cancelled') {
                    if(-not($useMirror -and $attempt -eq 0 -and $record.error -match 'HTTP (400|401|403|404)')){break}
                }
            } finally {
                if ($outputStream) {$outputStream.Dispose()}
                if ($inputStream) {$inputStream.Dispose()}
                if ($response) {$response.Dispose()}
                if ($request) {$request.Dispose()}
                if ($cts) {$cts.Dispose()}
                if (Test-Path -LiteralPath $partial) {
                    $resolved=[IO.Path]::GetFullPath($partial)
                    if ($resolved -cne ($destination+'.partial') -or -not $resolved.StartsWith($OutputRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {throw 'Cleanup path mismatch'}
                    Remove-Item -LiteralPath $resolved
                }
            }
            if ($attempt -eq 0) {Start-Sleep -Milliseconds 500}
        }
        $record.finished_utc=[DateTime]::UtcNow.ToString('o')
        $results.Add([pscustomobject]$record)
        [IO.File]::AppendAllText($ledger,($record|ConvertTo-Json -Depth 8 -Compress)+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
        Write-Output ('['+($index+1)+'/'+$plan.Count+'] '+$item.name+' '+$record.status+' bytes='+$record.bytes+' HTTP='+$record.http_status)
    }
} finally {
    $client.Dispose();$handler.Dispose();$fallbackClient.Dispose();$fallbackHandler.Dispose();$githubSecret=$null;$hfSecret=$null
    $summary=[ordered]@{started_utc=$started.ToString('o');finished_utc=[DateTime]::UtcNow.ToString('o');elapsed_seconds=$watch.Elapsed.TotalSeconds;plan_path=[IO.Path]::GetFullPath($PlanPath);planned_items=$plan.Count;visited_items=$index;attempted_items=$results.Count;downloaded=@($results|Where-Object status -eq downloaded).Count;failed=@($results|Where-Object status -eq failed).Count;cancelled=(Test-Path -LiteralPath $cancelPath);timed_out=($watch.Elapsed.TotalSeconds -ge $MaxSeconds);max_seconds=$MaxSeconds;request_seconds=$RequestSeconds;proxy_scope='this HttpClient only; direct TLS failure observed';results=$results}
    [IO.File]::WriteAllText($receiptPath,($summary|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
    Write-Output ('Receipt '+$receiptPath)
}
