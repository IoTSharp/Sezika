param([string]$OutputRoot='C:\Users\mysti\Documents\SezikaData\public-decision-2026-10-10')
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required'}
$code=@'
using System;
using System.Runtime.InteropServices;
public static class SavedRepoCredential {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
 public struct CREDENTIAL { public uint Flags; public uint Type; public string TargetName; public string Comment; public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten; public uint CredentialBlobSize; public IntPtr CredentialBlob; public uint Persist; public uint AttributeCount; public IntPtr Attributes; public string TargetAlias; public string UserName; }
 [DllImport("advapi32.dll",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)]
 static extern bool CredRead(string target,uint type,uint flags,out IntPtr credential);
 [DllImport("advapi32.dll")] static extern void CredFree(IntPtr credential);
 public static string Read(string target) {IntPtr p;if(!CredRead(target,1,0,out p))return null;try{var c=Marshal.PtrToStructure<CREDENTIAL>(p);return Marshal.PtrToStringUni(c.CredentialBlob,(int)c.CredentialBlobSize/2);}finally{CredFree(p);}}
}
'@
Add-Type -TypeDefinition $code
$watch=[Diagnostics.Stopwatch]::StartNew()
$targets=@('git:https://gitee.com','gitee-ai.IoTSharp.Sezika-mmBERT-8192-experimental:gitee-token')
$rows=[Collections.Generic.List[object]]::new()
for($i=0;$i -lt $targets.Count;$i++){
 if($watch.Elapsed.TotalSeconds -ge 40){throw 'Saved repository identity deadline'}
 $secret=[SavedRepoCredential]::Read($targets[$i])
 if(-not $secret){$rows.Add([pscustomobject]@{target=$targets[$i];available=$false});continue}
 $uri=$null
 try{
  $uri='https://gitee.com/api/v5/user?access_token='+[Uri]::EscapeDataString($secret)
  try{$r=Invoke-WebRequest -Uri $uri -TimeoutSec 8 -SkipHttpErrorCheck}
  catch{$r=Invoke-WebRequest -Uri $uri -TimeoutSec 12 -SkipHttpErrorCheck -Proxy 'http://127.0.0.1:7890'}
  $identity=$null
  if([int]$r.StatusCode -eq 200){$j=$r.Content|ConvertFrom-Json;$identity=[pscustomobject]@{id=$j.id;login=$j.login;name=$j.name}}
  $row=[pscustomobject]@{target=$targets[$i];available=$true;status=[int]$r.StatusCode;identity=$identity}
  $rows.Add($row);$row|ConvertTo-Json -Compress -Depth 4
 }finally{$secret=$null;$uri=$null}
}
$rows|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $OutputRoot 'saved-gitee-identity-check.json') -Encoding utf8
