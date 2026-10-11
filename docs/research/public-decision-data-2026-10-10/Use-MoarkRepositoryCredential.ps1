param([string]$OutputRoot='C:\Users\mysti\Documents\SezikaData\public-decision-2026-10-10')
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required'}
$watch=[Diagnostics.Stopwatch]::StartNew()
$secure=Read-Host 'Moark repository credential (hidden)' -AsSecureString
$secret=[Net.NetworkCredential]::new('',$secure).Password
$rows=[Collections.Generic.List[object]]::new()
try{
 $checks=@(
  @{name='maikebing-profile';url='https://moark.com/api/base/maikebing/profile'},
  @{name='maikebing-datasets';url='https://moark.com/api/base/datasets?namespace=maikebing&page=1&size=24'}
 )
 for($i=0;$i -lt $checks.Count;$i++){
  if($watch.Elapsed.TotalSeconds -ge 55){throw 'Authentication check deadline'}
  $headers=@{Authorization=('Bearer '+$secret)}
  try{$r=Invoke-WebRequest -Uri $checks[$i].url -Headers $headers -TimeoutSec 10 -SkipHttpErrorCheck}
  catch{$r=Invoke-WebRequest -Uri $checks[$i].url -Headers $headers -TimeoutSec 15 -SkipHttpErrorCheck -Proxy 'http://127.0.0.1:7890'}
  $identity=$null
  if([int]$r.StatusCode -eq 200){
   $j=$r.Content|ConvertFrom-Json
   if($checks[$i].name -eq 'maikebing-profile'){
    $identity=[pscustomobject]@{id=$j.id;name=$j.name;path=$j.path;username=$j.username;role=$j.role;login=$j.login}
   }
  }
  $row=[pscustomobject]@{check=$checks[$i].name;url=$checks[$i].url;status=[int]$r.StatusCode;identity=$identity}
  $rows.Add($row);$row|ConvertTo-Json -Depth 5 -Compress
  $headers.Clear()
 }
 $authRoot=Join-Path $OutputRoot '.auth'
 [void][IO.Directory]::CreateDirectory($authRoot)
 $encrypted=ConvertFrom-SecureString -SecureString $secure
 [IO.File]::WriteAllText((Join-Path $authRoot 'moark-repository.dpapi'),$encrypted,[Text.UTF8Encoding]::new($false))
 $receipt=[pscustomobject]@{checked_utc=[DateTime]::UtcNow.ToString('o');target='maikebing';checks=$rows;temporary_credential='Task-scoped Windows DPAPI encrypted file; never included in publication; delete on task completion';repository_write_verified=$false}
 $receipt|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $OutputRoot 'moark-repository-auth.json') -Encoding utf8
}finally{$secret=$null;$secure.Dispose()}
