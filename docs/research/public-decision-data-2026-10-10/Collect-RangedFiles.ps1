param(
 [Parameter(Mandatory)][string]$PlanPath,
 [string]$OutputRoot='C:\Users\mysti\Documents\SezikaData\public-decision-2026-10-10',
 [string]$LedgerName='range-ledger.jsonl',
 [ValidateRange(10,1800)][int]$MaxSeconds=1500,
 [ValidateRange(10,300)][int]$RequestSeconds=150,
 [ValidateRange(1024,134217728)][int]$SegmentBytes=67108864,
 [ValidateSet('OfficialProxy','MirrorDirect')][string]$Transport='OfficialProxy'
)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required'}
$OutputRoot=[IO.Path]::GetFullPath($OutputRoot)
$watch=[Diagnostics.Stopwatch]::StartNew();$started=[DateTime]::UtcNow
$plan=@(Get-Content -LiteralPath $PlanPath -Raw|ConvertFrom-Json)
$ledger=Join-Path $OutputRoot $LedgerName
$cancel=Join-Path $OutputRoot 'CANCEL'
$known=@{}
$oldLedgers=@(Get-ChildItem -LiteralPath $OutputRoot -File -Filter '*ledger*.jsonl')
for($l=0;$l -lt $oldLedgers.Count;$l++){
 if($watch.Elapsed.TotalSeconds -ge $MaxSeconds){throw 'Ledger deadline'}
 $lines=@(Get-Content -LiteralPath $oldLedgers[$l].FullName)
 for($i=0;$i -lt $lines.Count;$i++){
  if($watch.Elapsed.TotalSeconds -ge $MaxSeconds){throw 'Ledger deadline'}
  $old=$lines[$i]|ConvertFrom-Json
  if($old.status -eq 'downloaded' -and (Test-Path -LiteralPath $old.path)){$known[$old.url]=$old}
 }
}
$handler=[Net.Http.HttpClientHandler]::new();$handler.UseProxy=($Transport -ne 'MirrorDirect');$handler.Proxy=[Net.WebProxy]::new('http://127.0.0.1:7890')
$client=[Net.Http.HttpClient]::new($handler);$client.Timeout=[Threading.Timeout]::InfiniteTimeSpan
$fallbackHandler=[Net.Http.HttpClientHandler]::new();$fallbackHandler.UseProxy=$true;$fallbackHandler.Proxy=[Net.WebProxy]::new('http://127.0.0.1:7890')
$fallbackClient=[Net.Http.HttpClient]::new($fallbackHandler);$fallbackClient.Timeout=[Threading.Timeout]::InfiniteTimeSpan
$results=[Collections.Generic.List[object]]::new()
try{
 for($index=0;$index -lt $plan.Count;$index++){
  if($watch.Elapsed.TotalSeconds -ge $MaxSeconds -or (Test-Path -LiteralPath $cancel)){break}
  $item=$plan[$index]
  if($known.ContainsKey($item.url)){continue}
  $destination=[IO.Path]::GetFullPath((Join-Path $OutputRoot $item.path))
  if(-not $destination.StartsWith($OutputRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Outside task output root'}
  [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
  $partial=$destination+'.range-partial';$sidecar=$partial+'.meta.json'
  $expected=[long]$item.expected_bytes
  if($expected -le 0){throw 'Ranged download requires frozen source length'}
  if($Transport -eq 'MirrorDirect' -and -not $item.expected_sha256){throw 'Mirror transport requires frozen upstream SHA256'}
  if(Test-Path -LiteralPath $destination){throw ('Untracked destination '+$destination)}
  $identity=[ordered]@{url=$item.url;revision=$item.revision;expected_bytes=$expected;expected_sha256=$item.expected_sha256;destination=$destination}
  if(Test-Path -LiteralPath $partial){
   if(-not(Test-Path -LiteralPath $sidecar)){throw 'Partial identity missing'}
   $prior=Get-Content -LiteralPath $sidecar -Raw|ConvertFrom-Json
   if($prior.url -cne $item.url -or $prior.expected_bytes -ne $expected -or $prior.expected_sha256 -cne $item.expected_sha256){throw 'Partial source identity mismatch'}
  }else{
   $identity|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $sidecar -Encoding utf8
   [IO.File]::WriteAllBytes($partial,[byte[]]::new(0))
  }
  $offset=(Get-Item -LiteralPath $partial).Length
  if($offset -gt $expected){throw 'Partial exceeds frozen source length'}
  $record=[ordered]@{name=$item.name;kind=$item.kind;dataset=$item.dataset;split=$item.split;url=$item.url;path=$destination;started_utc=[DateTime]::UtcNow.ToString('o');status='pending';bytes=0;partial_bytes=$offset;sha256=$null;expected_sha256=$item.expected_sha256;source_revision=$item.revision;requests=0;error=$null;transport=$Transport;mirror_url=if($Transport -eq 'MirrorDirect'){$item.url.Replace('https://huggingface.co/','https://hf-mirror.com/')}else{$null}}
  $segments=[long][Math]::Ceiling(($expected-$offset)/[double]$SegmentBytes)
  for($segment=0L;$segment -lt $segments;$segment++){
   if($watch.Elapsed.TotalSeconds -ge $MaxSeconds -or (Test-Path -LiteralPath $cancel)){break}
   $end=[Math]::Min($expected-1,$offset+$SegmentBytes-1);$succeeded=$false
   for($attempt=0;$attempt -lt 2;$attempt++){
    if($watch.Elapsed.TotalSeconds -ge $MaxSeconds -or (Test-Path -LiteralPath $cancel)){break}
    $req=$null;$res=$null;$input=$null;$output=$null;$cts=$null
    try{
     $drive=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($destination))
     if($drive.AvailableFreeSpace -lt ($end-$offset+1+2GB)){throw 'Disk reserve reached'}
     $budget=[Math]::Max(1,[Math]::Min($RequestSeconds,$MaxSeconds-[int]$watch.Elapsed.TotalSeconds))
     $cts=[Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($budget))
     $requestUrl=if($Transport -eq 'MirrorDirect' -and $attempt -eq 0){$record.mirror_url}else{$item.url}
     $activeClient=if($Transport -eq 'MirrorDirect' -and $attempt -gt 0){$fallbackClient}else{$client}
     $req=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get,$requestUrl)
     $req.Headers.Range=[Net.Http.Headers.RangeHeaderValue]::new($offset,$end)
     $req.Headers.UserAgent.ParseAdd('Sezika-Public-Decision-Data/1.0')
     $record.requests++
     $res=$activeClient.SendAsync($req,[Net.Http.HttpCompletionOption]::ResponseHeadersRead,$cts.Token).GetAwaiter().GetResult()
     if([int]$res.StatusCode -ne 206){throw ('Range request HTTP '+[int]$res.StatusCode)}
     $range=$res.Content.Headers.ContentRange
     if($range.From -ne $offset -or $range.To -ne $end -or $range.Length -ne $expected){throw 'Content-Range does not match frozen file identity'}
     $input=$res.Content.ReadAsStreamAsync($cts.Token).GetAwaiter().GetResult()
     $output=[IO.FileStream]::new($partial,[IO.FileMode]::Open,[IO.FileAccess]::Write,[IO.FileShare]::Read,1048576)
     $output.SetLength($offset);$output.Position=$offset
     $buffer=[byte[]]::new(1048576);$got=0L;$want=$end-$offset+1
     for($readIndex=0L;$readIndex -le $want;$readIndex++){
      $cts.Token.ThrowIfCancellationRequested()
      if(Test-Path -LiteralPath $cancel){throw 'Cancelled'}
      $n=$input.ReadAsync($buffer,0,[int][Math]::Min($buffer.Length,$want-$got),$cts.Token).GetAwaiter().GetResult()
      if($n -eq 0){break}
      $output.Write($buffer,0,$n);$got+=$n
      if($got -eq $want){break}
     }
     if($got -ne $want){throw 'Range body length mismatch'}
     $output.Dispose();$output=$null
     $offset=$end+1;$record.partial_bytes=$offset;$record.error=$null;$succeeded=$true
     Write-Output ($item.name+' verified-range-bytes='+$offset+'/'+$expected)
     break
    }catch{
     $record.error=$_.Exception.Message
     if($output){$output.SetLength($offset)}
     if($record.error -match 'HTTP (400|401|403|404)|identity|frozen|Disk|Cancelled'){break}
    }finally{
     if($output){$output.Dispose()};if($input){$input.Dispose()};if($res){$res.Dispose()};if($req){$req.Dispose()};if($cts){$cts.Dispose()}
    }
    if($attempt -eq 0){Start-Sleep -Milliseconds 500}
   }
   if(-not $succeeded){break}
  }
  if($offset -eq $expected){
   $digest=(Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash.ToLowerInvariant()
   if($item.expected_sha256 -and $digest -cne $item.expected_sha256.ToLowerInvariant()){$record.status='hash_mismatch';$record.error='Upstream SHA256 mismatch; retained for diagnosis'}
   else{
    Move-Item -LiteralPath $partial -Destination $destination
    $resolved=[IO.Path]::GetFullPath($sidecar)
    if($resolved -cne ($destination+'.range-partial.meta.json') -or -not $resolved.StartsWith($OutputRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Sidecar cleanup path mismatch'}
    Remove-Item -LiteralPath $resolved
    $record.status='downloaded';$record.bytes=$expected;$record.sha256=$digest
   }
  }
  $record.finished_utc=[DateTime]::UtcNow.ToString('o');$results.Add([pscustomobject]$record)
  [IO.File]::AppendAllText($ledger,($record|ConvertTo-Json -Depth 8 -Compress)+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
  Write-Output ('['+($index+1)+'/'+$plan.Count+'] '+$item.name+' '+$record.status)
 }
}finally{
 $client.Dispose();$handler.Dispose();$fallbackClient.Dispose();$fallbackHandler.Dispose()
 $receipt=Join-Path $OutputRoot ('range-batch-'+$started.ToString('yyyyMMdd-HHmmss-fff')+'-'+$PID+'.json')
 [pscustomobject]@{started_utc=$started.ToString('o');finished_utc=[DateTime]::UtcNow.ToString('o');max_seconds=$MaxSeconds;request_seconds=$RequestSeconds;segment_bytes=$SegmentBytes;plan_path=$PlanPath;planned=$plan.Count;visited=$index;results=$results;pending_parts='Task-owned resumable data retained with source-bound identity sidecars; no background process is required'}|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $receipt -Encoding utf8
 Write-Output ('Receipt '+$receipt)
}
