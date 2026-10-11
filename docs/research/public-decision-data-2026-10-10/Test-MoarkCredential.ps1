param([string]$OutputPath)
$ErrorActionPreference='Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) {throw 'PowerShell 7 required'}
$secure=Read-Host 'Moark access token (hidden)' -AsSecureString
$secret=[Net.NetworkCredential]::new('',$secure).Password
$watch=[Diagnostics.Stopwatch]::StartNew()
$rows=[Collections.Generic.List[object]]::new()
$checks=@(
@{name='moark-IoTSharp-profile';url='https://moark.com/api/base/IoTSharp/profile'},
@{name='moark-serverless-models';url='https://api.moark.com/v1/models'}
)
try {
 for ($i=0;$i -lt $checks.Count;$i++) {
  if ($watch.Elapsed.TotalSeconds -ge 50) {break}
  $entry=$checks[$i];$headers=@{Authorization=('Bearer '+$secret)}
  $r=$null
  try {$r=Invoke-WebRequest -Uri $entry.url -Headers $headers -TimeoutSec 8 -SkipHttpErrorCheck -MaximumRedirection 0}
  catch {$r=Invoke-WebRequest -Uri $entry.url -Headers $headers -TimeoutSec 12 -SkipHttpErrorCheck -MaximumRedirection 0 -Proxy 'http://127.0.0.1:7890'}
  $status=[int]$r.StatusCode
  $identity=$null;$count=$null
  if ($status -eq 200) {
   $j=$r.Content|ConvertFrom-Json
   if ($entry.name -eq 'moark-serverless-models') {$count=@($j.data).Count}
   else {$identity=[pscustomobject]@{name=$j.name;path=$j.path;username=$j.username;role=$j.role}}
  }
  $row=[pscustomobject]@{name=$entry.name;url=$entry.url;status=$status;identity=$identity;model_count=$count}
  $rows.Add($row)
  $row|ConvertTo-Json -Compress -Depth 5
  $headers.Clear();Start-Sleep -Milliseconds 150
 }
 $receipt=[pscustomobject]@{checked_utc=[DateTime]::UtcNow.ToString('o');authorization_kind='Bearer';elapsed_seconds=$watch.Elapsed.TotalSeconds;checks=$rows;limitation='A Serverless model listing does not establish Git/Git LFS or organization repository write permission.'}
 $receipt|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $OutputPath -Encoding utf8
} finally {$secret=$null;$secure.Dispose()}
