param([Parameter(Mandatory)][string]$TaskDirectory,[switch]$DummyProbe)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required.'}
$task=(Resolve-Path -LiteralPath $TaskDirectory).Path
$taskInfo=Get-Content -LiteralPath (Join-Path $task 'task.json') -Raw|ConvertFrom-Json
$runId=[string]$taskInfo.run_id
if($runId -cnotmatch '^[a-z0-9][a-z0-9-]{2,63}$' -or (Split-Path $task -Leaf) -cne ('repository-training-'+$runId)){throw 'Matching dedicated task identity required.'}
$connection=Get-Content -LiteralPath (Join-Path $task 'connection.json') -Raw|ConvertFrom-Json
$deadline=Get-Content -LiteralPath (Join-Path $task 'deadline.json') -Raw|ConvertFrom-Json
if((([DateTime]$deadline.deadline_utc).ToUniversalTime()-[DateTime]::UtcNow).TotalSeconds -lt 100 -or (Test-Path -LiteralPath (Join-Path $task 'CANCEL'))){throw 'Credential installation deadline/cancellation.'}
if($connection.host_name -cnotmatch '^[A-Za-z0-9.-]+$' -or $connection.user -cnotmatch '^[A-Za-z0-9_+.-]+$' -or [int]$connection.port -lt 1 -or [int]$connection.port -gt 65535){throw 'Invalid SSH destination.'}
if(-not $env:SEZIKA_SSH_PASSWORD){throw 'Parent-provided SEZIKA_SSH_PASSWORD required; no inferred password.'}
$datasetBytes=$null;$modelBytes=$null;$dataset64=$null;$model64=$null;$script=$null;$arguments=$null
$savedEnvironment=@{}
foreach($name in @('SSH_ASKPASS','SSH_ASKPASS_REQUIRE','DISPLAY')){$savedEnvironment[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try{
    if($DummyProbe){
        $datasetBytes=[Text.Encoding]::UTF8.GetBytes('sezika-dataset-dummy-distinct-20261011')
        $modelBytes=[Text.Encoding]::UTF8.GetBytes('sezika-model-dummy-distinct-20261011')
    }else{
        if(-not ('SezikaRemoteCredentialReader' -as [type])){
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class SezikaRemoteCredentialReader {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    private struct Credential {
        public uint Flags, Type;
        public IntPtr TargetName, Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes, TargetAlias, UserName;
    }
    [DllImport("Advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("Advapi32.dll")] private static extern void CredFree(IntPtr credential);
    public static byte[] ReadUtf8(string target) {
        IntPtr pointer;
        if(!CredReadW(target,1,0,out pointer)) throw new InvalidOperationException("Credential read failed; no secret output.");
        byte[] raw=null; char[] chars=null;
        Credential record=default;
        try {
            record=Marshal.PtrToStructure<Credential>(pointer);
            if(record.Type!=1 || record.CredentialBlobSize<2 || record.CredentialBlobSize>5120 || record.CredentialBlobSize%2!=0 || record.CredentialBlob==IntPtr.Zero)
                throw new InvalidOperationException("Generic UTF16LE credential bounds.");
            raw=new byte[checked((int)record.CredentialBlobSize)];
            Marshal.Copy(record.CredentialBlob,raw,0,raw.Length);
            chars=new UnicodeEncoding(false,false,true).GetChars(raw);
            foreach(char value in chars) if(value=='\0' || value=='\r' || value=='\n') throw new InvalidOperationException("Credential protocol control character.");
            var utf8=new UTF8Encoding(false,true).GetBytes(chars);
            if(utf8.Length<1 || utf8.Length>5120){Array.Clear(utf8,0,utf8.Length);throw new InvalidOperationException("UTF8 credential byte bound.");}
            return utf8;
        } finally {
            if(raw!=null)Array.Clear(raw,0,raw.Length);
            if(chars!=null)Array.Clear(chars,0,chars.Length);
            if(record.CredentialBlob!=IntPtr.Zero && record.CredentialBlobSize>0 && record.CredentialBlobSize<=5120)
                for(int i=0;i<record.CredentialBlobSize;i++)Marshal.WriteByte(record.CredentialBlob,i,0);
            CredFree(pointer);
        }
    }
}
'@
        }
        $datasetBytes=[SezikaRemoteCredentialReader]::ReadUtf8('git:https://gitee.com')
        $modelBytes=[SezikaRemoteCredentialReader]::ReadUtf8('Codex:Gitee:EnterpriseMCP:api.gitee.com')
    }
    $dataset64=[Convert]::ToBase64String($datasetBytes);$model64=[Convert]::ToBase64String($modelBytes)
    $script=@'
set +x
set -euo pipefail
umask 077
export LC_ALL=C
unset GIT_TRACE GIT_TRACE_CURL GIT_CURL_VERBOSE GIT_TRACE_PACKET GIT_TRACE_PERFORMANCE GIT_TRACE_SETUP
run_id='__RUN_ID__'
dummy=__DUMMY__
root=/data/sezika-repository-$run_id
ram=/dev/shm/sezika-repository-$run_id
if ((dummy==1)); then ram=$ram-dummy; fi
[[ $EUID == 0 && -f $root/OWNED_BY_SEZIKA_REPOSITORY_TASK && ! -L $root && $(timeout 3s realpath -- "$root") == "$root" ]]
[[ $(timeout 3s stat -f -c %T /dev/shm) == tmpfs && ! -e $ram && ! -L $ram ]]
owned=0; installed=0
cleanup(){
  if ((owned==1 && (dummy==1 || installed==0))); then
    [[ $ram == /dev/shm/sezika-repository-"$run_id" || $ram == /dev/shm/sezika-repository-"$run_id"-dummy ]] || return 1
    [[ -d $ram && ! -L $ram && -f $ram/OWNED_BY_SEZIKA_CREDENTIAL_TASK ]] || return 1
    [[ $(< "$ram/OWNED_BY_SEZIKA_CREDENTIAL_TASK") == "$run_id" ]] || return 1
    timeout 3s rm -f -- "$ram/dataset.token" "$ram/model.token" "$ram/helper.sh" "$ram/OWNED_BY_SEZIKA_CREDENTIAL_TASK"
    timeout 3s rmdir -- "$ram"
  fi
}
trap cleanup EXIT
mkdir -m 700 -- "$ram"
printf '%s' "$run_id" > "$ram/OWNED_BY_SEZIKA_CREDENTIAL_TASK"
owned=1
printf '%s' '__DATASET64__' | timeout 3s base64 --decode > "$ram/dataset.token"
printf '%s' '__MODEL64__' | timeout 3s base64 --decode > "$ram/model.token"
chmod 600 -- "$ram/dataset.token" "$ram/model.token" "$ram/OWNED_BY_SEZIKA_CREDENTIAL_TASK"
cat > "$ram/helper.sh" <<'HELPER'
#!/bin/bash
set +x
set -euo pipefail
umask 077
export LC_ALL=C
unset GIT_TRACE GIT_TRACE_CURL GIT_CURL_VERBOSE GIT_TRACE_PACKET GIT_TRACE_PERFORMANCE GIT_TRACE_SETUP
[[ $# == 1 ]] || exit 1
case $1 in get) :;; store|erase) exit 0;; *) exit 1;; esac
base=${BASH_SOURCE[0]%/*}
[[ $base =~ ^/dev/shm/sezika-repository-[a-z0-9-]+$ && -d $base && ! -L $base && -f $base/OWNED_BY_SEZIKA_CREDENTIAL_TASK && ! -L $base/OWNED_BY_SEZIKA_CREDENTIAL_TASK ]] || exit 1
begin=$SECONDS; fields=0; total=0; ended=0
protocol=; host=; path=; seen_protocol=0; seen_host=0; seen_path=0
for ((field=0; field<16; field++)); do
  ((SECONDS-begin<5)) || exit 1
  # Git's helper writer closes stdin after its fields; unlike the manual
  # protocol probe it may supply EOF without an additional empty line.
  # Bash read returns 1 for EOF, and >128 for its timeout. Only EOF is valid.
  line=; read_code=0
  IFS= read -r -t 1 -n 5121 line || read_code=$?
  ((read_code==0 || read_code==1)) || exit 1
  (( ${#line}<=5120 )) || exit 1
  total=$((total+${#line}+1)); ((total<=5120)) || exit 1
  if [[ -z $line ]]; then ended=1; break; fi
  fields=$((fields+1))
  case $line in
    protocol=*) ((seen_protocol==0)) || exit 1; protocol=${line#protocol=}; seen_protocol=1;;
    host=*) ((seen_host==0)) || exit 1; host=${line#host=}; seen_host=1;;
    path=*) ((seen_path==0)) || exit 1; path=${line#path=}; seen_path=1;;
    username=*|wwwauth\[\]=*|capability=authtype|capability=state) :;;
    *) exit 1;;
  esac
  if ((read_code==1)); then ended=1; break; fi
done
[[ $ended == 1 && $protocol == https && ( $host == moark.com || $host == gitee.com ) ]] || exit 1
case $path in
  maikebing/public-decision-data|maikebing/public-decision-data.git|maikebing/public-decision-data/info/lfs|maikebing/public-decision-data/info/lfs/*|maikebing/public-decision-data.git/info/lfs|maikebing/public-decision-data.git/info/lfs/*) file=$base/dataset.token;;
  maikebing/Sezika-mmBERT-8192-experimental|maikebing/Sezika-mmBERT-8192-experimental.git|maikebing/Sezika-mmBERT-8192-experimental/info/lfs|maikebing/Sezika-mmBERT-8192-experimental/info/lfs/*|maikebing/Sezika-mmBERT-8192-experimental.git/info/lfs|maikebing/Sezika-mmBERT-8192-experimental.git/info/lfs/*) file=$base/model.token;;
  IoTSharp/Sezika-Decision-Base|IoTSharp/Sezika-Decision-Base.git|IoTSharp/Sezika-Decision-Base/info/lfs|IoTSharp/Sezika-Decision-Base/info/lfs/*|IoTSharp/Sezika-Decision-Base.git/info/lfs|IoTSharp/Sezika-Decision-Base.git/info/lfs/*) file=$base/model.token;;
  *) exit 1;;
esac
[[ $path != *..* && ! -L $file && -f $file ]] || exit 1
((SECONDS-begin<5)) || exit 1
secret=; IFS= read -r -t 1 -n 5121 secret < "$file" || [[ -n $secret ]]
(( ${#secret}>=1 && ${#secret}<=5120 && SECONDS-begin<5 )) || exit 1
printf 'username=maikebing\npassword=%s\n\n' "$secret"
unset secret
HELPER
chmod 700 -- "$ram/helper.sh"
if ((dummy==1)); then
  value=$(printf 'protocol=https\nhost=moark.com\npath=maikebing/public-decision-data\n\n' | timeout 6s /bin/bash "$ram/helper.sh" get)
  [[ $value == $'username=maikebing\npassword=sezika-dataset-dummy-distinct-20261011' ]]
  unset value
  value=$(printf 'protocol=https\nhost=gitee.com\npath=maikebing/Sezika-mmBERT-8192-experimental.git/info/lfs/objects/batch\n\n' | timeout 6s /bin/bash "$ram/helper.sh" get)
  [[ $value == $'username=maikebing\npassword=sezika-model-dummy-distinct-20261011' ]]
  unset value
  code=0
  value=$(printf 'protocol=https\nhost=invalid.example\npath=maikebing/public-decision-data\n\n' | timeout 6s /bin/bash "$ram/helper.sh" get) || code=$?
  [[ $code == 1 && -z $value ]]
  unset value
  # Exercise Git's actual helper stdin/output contract; capture all output in
  # memory and compare only dummy values. Never print credential fill output.
  value=$(printf 'protocol=https\nhost=moark.com\npath=maikebing/public-decision-data\n\n' | GIT_CONFIG_NOSYSTEM=1 GIT_CONFIG_SYSTEM=/dev/null GIT_CONFIG_GLOBAL=/dev/null GIT_TERMINAL_PROMPT=0 timeout 8s git -c credential.helper= -c "credential.helper=!/bin/bash $ram/helper.sh" -c credential.useHttpPath=true credential fill)
  [[ $value == *$'\nusername=maikebing\npassword=sezika-dataset-dummy-distinct-20261011' ]]
  unset value
  value=$(printf 'protocol=https\nhost=moark.com\npath=maikebing/Sezika-mmBERT-8192-experimental.git/info/lfs/objects/batch\n\n' | GIT_CONFIG_NOSYSTEM=1 GIT_CONFIG_SYSTEM=/dev/null GIT_CONFIG_GLOBAL=/dev/null GIT_TERMINAL_PROMPT=0 timeout 8s git -c credential.helper= -c "credential.helper=!/bin/bash $ram/helper.sh" -c credential.useHttpPath=true credential fill)
  [[ $value == *$'\nusername=maikebing\npassword=sezika-model-dummy-distinct-20261011' ]]
  unset value
  value=$(printf 'protocol=https\nhost=moark.com\npath=IoTSharp/Sezika-Decision-Base.git/info/lfs/objects/batch\n\n' | GIT_CONFIG_NOSYSTEM=1 GIT_CONFIG_SYSTEM=/dev/null GIT_CONFIG_GLOBAL=/dev/null GIT_TERMINAL_PROMPT=0 timeout 8s git -c credential.helper= -c "credential.helper=!/bin/bash $ram/helper.sh" -c credential.useHttpPath=true credential fill)
  [[ $value == *$'\nusername=maikebing\npassword=sezika-model-dummy-distinct-20261011' ]]
  unset value
  code=0
  value=$(printf 'protocol=https\nhost=moark.com\npath=IoTSharp/public-decision-data\n\n' | timeout 6s /bin/bash "$ram/helper.sh" get) || code=$?
  [[ $code == 1 && -z $value ]]
  unset value
  cleanup; owned=0
  [[ ! -e $ram && ! -L $ram ]]
  printf 'status=PASS_dummy_manual_and_actual_Git_fill_wrong_host_rejected_RAM_removed\n'
else
  installed=1
  printf 'status=INSTALLED_task_owned_RAM_credentials_no_secret_output\n'
fi
'@
    $script=$script.Replace('__RUN_ID__',$runId).Replace('__DUMMY__',$(if($DummyProbe){'1'}else{'0'})).Replace('__DATASET64__',$dataset64).Replace('__MODEL64__',$model64)
    $root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
    $env:SSH_ASKPASS=Join-Path $root 'tools/rental-training/Askpass.cmd';$env:SSH_ASKPASS_REQUIRE='force';$env:DISPLAY='sezika-task-askpass'
    $options=@('ConnectTimeout=10','StrictHostKeyChecking=accept-new',('UserKnownHostsFile='+$connection.known_hosts),'ServerAliveInterval=10','ServerAliveCountMax=2','NumberOfPasswordPrompts=1','BatchMode=no')
    $arguments=@{Destination=($connection.user+'@'+$connection.host_name);Port=[int]$connection.port;Script=$script;Option=$options;SshExecutable='C:\Windows\System32\OpenSSH\ssh.exe'}
    if($connection.identity_file){$arguments.IdentityFile=$connection.identity_file}
    & 'C:\Users\mysti\.codex\skills\usm-gateshell-ssh\scripts\invoke-ssh-bash.ps1' @arguments
    if($LASTEXITCODE -ne 0){throw 'Remote credential installation/probe failed; no secret output.'}
}finally{
    if($null -ne $datasetBytes){[Array]::Clear($datasetBytes,0,$datasetBytes.Length)}
    if($null -ne $modelBytes){[Array]::Clear($modelBytes,0,$modelBytes.Length)}
    $dataset64=$null;$model64=$null;$script=$null
    if($null -ne $arguments){$arguments.Clear()}
    $datasetBytes=$null;$modelBytes=$null
    foreach($name in @('SSH_ASKPASS','SSH_ASKPASS_REQUIRE','DISPLAY')){[Environment]::SetEnvironmentVariable($name,$savedEnvironment[$name],'Process')}
    $savedEnvironment.Clear()
}
