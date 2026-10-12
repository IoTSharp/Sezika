param(
    [Parameter(Mandatory)][string]$RepositoryDirectory,
    [Parameter(Mandatory)][ValidateSet('model','dataset')][string]$Kind,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]{2,63}$')][string]$RunId,
    [Parameter(Mandatory)][string]$OwnedManifest,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string]$ExpectedRemoteCommit,
    [Parameter(Mandatory)][string]$TaskDirectory,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9/_-]{0,100}$')][string]$Branch = 'master',
    [ValidateRange(30,1800)][int]$TimeoutSeconds = 900,
    [switch]$PreviewOnly,
    [switch]$CommitOnly,
    [switch]$LocalFixture
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 required.' }
$repository = (Resolve-Path -LiteralPath $RepositoryDirectory).Path
$task = (Resolve-Path -LiteralPath $TaskDirectory).Path
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $task.StartsWith((Join-Path $root '.artifacts\repository-training-'), [StringComparison]::OrdinalIgnoreCase) -or
    (Split-Path $task -Leaf) -cne ('repository-training-'+$RunId)) { throw 'Use the matching task-owned artifact directory.' }
if ((Get-Item -LiteralPath $repository).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Repository cannot be a link.' }
$owner = if ($Kind -eq 'model') {'Sezika-mmBERT-8192-experimental'} else {'public-decision-data'}
$url = 'https://moark.com/maikebing/'+$owner
if($LocalFixture){
    if(-not $RunId.StartsWith('fixture-',[StringComparison]::Ordinal)){throw 'Local fixture requires a dedicated fixture run.'}
    $url='file:///'+(Join-Path $task 'fixture-bare.git').Replace('\','/')
}
$git = @(Get-Command git -CommandType Application -ErrorAction Stop)[0].Source
$logs = Join-Path $task 'publication-processes'
$watch = [Diagnostics.Stopwatch]::StartNew(); $calls = 0; $receipts = [Collections.Generic.List[object]]::new()
function Check { if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds -or (Test-Path -LiteralPath (Join-Path $task 'CANCEL'))) { throw 'Publication cancelled or deadline reached.' } }
function CheckPath([string]$Path){
    $cursor=[IO.Path]::GetFullPath($Path)
    for($depth=0;$depth -lt 64;$depth++){Check;if((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked publication ancestry rejected.'};$parent=[IO.Path]::GetDirectoryName($cursor);if($null -eq $parent){return};$cursor=$parent}
    throw 'Publication path ancestry bound64.'
}
function Git([string]$Label, [string[]]$Arguments) {
    Check; $script:calls++; if ($script:calls -gt 128) { throw 'Git call limit128.' }
    $remaining = [int][Math]::Floor($TimeoutSeconds - $watch.Elapsed.TotalSeconds)
    if ($remaining -lt 5) { throw 'Insufficient time for another Git command.' }
    $prefix = @('-C',$repository,'-c','lfs.standalonetransferagent=','-c','lfs.basictransfersonly=true','-c','lfs.concurrenttransfers=1','-c','lfs.transfer.maxretries=0','-c','lfs.dialtimeout=15','-c','lfs.activitytimeout=90','-c','http.lowSpeedLimit=1024','-c','http.lowSpeedTime=45')
    $tag = 'publish-'+$Kind+'-'+$Label+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
    $gate=Join-Path $task ('git-gate-'+$tag);[void][IO.Directory]::CreateDirectory($gate)
    $commandFile=Join-Path $gate 'command.json'
    [IO.File]::WriteAllText($commandFile,(@{git=$git;directory=$repository;arguments=@($prefix+$Arguments);gate=$gate}|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
    $receipt = & (Join-Path $root 'tools\rental-training\Invoke-BoundedProcess.ps1') -FilePath 'C:\Program Files\PowerShell\7\pwsh.exe' -ArgumentList @('-NoProfile','-File',(Join-Path $PSScriptRoot 'Invoke-GitPayload.ps1'),'-CommandFile',$commandFile) -StartGateDirectory $gate -WorkingDirectory $repository -TimeoutSeconds ([Math]::Min(600,$remaining)) -LogName $tag -LogDirectory $logs -CancelFile (Join-Path $task 'CANCEL') -Environment @{ GIT_TERMINAL_PROMPT='0'; GCM_INTERACTIVE='Never'; GIT_TRACE=$null; GIT_TRACE_CURL=$null; GIT_CURL_VERBOSE=$null }
    $found = @(Get-ChildItem -LiteralPath $logs -File -Filter ('*'+$tag+'.result.json') | Select-Object -First 2)
    if ($found.Count -ne 1) { throw 'Unique guardian receipt is required.' }
    $observed = Get-Content -LiteralPath $found[0].FullName -Raw | ConvertFrom-Json
    $receipts.Add([ordered]@{label=$Label;receipt=$found[0].FullName;status=$observed.Status;exit=$observed.ExitCode})
    if ($observed.Status -cne 'Succeeded' -or $observed.ExitCode -ne 0) { throw 'Git or task cleanup failed; preserve receipt and reconcile before retry.' }
    return [IO.File]::ReadAllText($observed.StdoutPath).Trim()
}
function GitPaths([string]$Label,[string[]]$Arguments,[string[]]$SelectedPaths){
    if($SelectedPaths.Count -lt 1 -or $SelectedPaths.Count -gt 1001){throw 'Path batch requires1..1001 owned paths.'}
    $outputs=[Collections.Generic.List[string]]::new()
    for($offset=0;$offset -lt $SelectedPaths.Count;$offset+=32){
        Check
        $batch=@($SelectedPaths|Select-Object -Skip $offset -First 32)
        $outputs.Add((Git ($Label+'-batch-'+[int]($offset/32)) (@($Arguments)+@('--')+$batch)))
    }
    return ($outputs -join "`n")
}
function RemoteRef([string]$Label){
    try {return Git $Label @('ls-remote','--heads','origin',('refs/heads/'+$Branch))}
    catch {
        # This read has no remote write. Preserve the failed receipt and retry once
        # through the control machine proxy; ambiguous push is never retried here.
        Check;return Git ($Label+'-proxy-once') @('-c','http.proxy=http://127.0.0.1:7890','ls-remote','--heads','origin',('refs/heads/'+$Branch))
    }
}
$result = [ordered]@{schema_version=1;run_id=$RunId;kind=$Kind;status='FAILED_or_pending';preview_only=[bool]$PreviewOnly;commit_only=[bool]$CommitOnly;expected_remote_commit=$ExpectedRemoteCommit;publication_commit=$null;public_content_readback='NOT_RUN';calls=0;receipts=$null}
$receiptPath=Join-Path $task ('publication-'+$Kind+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)+'.json')
function Snapshot { $result.calls=$calls;$result.receipts=$receipts.ToArray();$result.elapsed_seconds=$watch.Elapsed.TotalSeconds;return [pscustomobject]$result }
try {
    $origin = Git 'origin' @('remote','get-url','origin')
    if ($origin.TrimEnd('/') -cne $url -and $origin.TrimEnd('/') -cne ($url+'.git')) { throw 'Repository origin identity differs from the fixed personal destination.' }
    CheckPath $repository;CheckPath $task
    $remote = RemoteRef 'remote-before'
    if ($remote -cnotmatch '^([a-f0-9]{40})\s+refs/heads/' -or $Matches[1] -cne $ExpectedRemoteCommit) { throw 'Remote ref changed or is missing; inspect and prepare against the observed base.' }
    $staged = Git 'existing-index' @('diff','--cached','--name-only')
    if ($staged) { throw 'Existing staged changes belong to another operation; preserve them.' }
    $manifest = Get-Content -LiteralPath $OwnedManifest -Raw | ConvertFrom-Json
    $files = @($manifest.files)
    if ($files.Count -lt 1 -or $files.Count -gt 1000) { throw 'Owned file manifest requires 1..1000 files.' }
    $paths = [Collections.Generic.List[string]]::new(); $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $prefix = if ($Kind -eq 'model') {'runs/'+$RunId+'/'} else {'derived/'+$RunId+'/'}
    foreach ($file in $files) {
        Check; $path=[string]$file.path
        if ($path.Length -gt 512 -or $path -cnotmatch '^[A-Za-z0-9_.\/-]+$' -or $path.Contains('..') -or -not $path.StartsWith($prefix,[StringComparison]::Ordinal) -or -not $seen.Add($path)) { throw 'Only unique bounded task-owned run paths are publishable.' }
        $actual=[IO.Path]::GetFullPath((Join-Path $repository $path))
        if (-not $actual.StartsWith($repository+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Owned path escaped repository.' }
        CheckPath $actual;$item=Get-Item -LiteralPath $actual
        if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.Length -ne $file.bytes -or $file.sha256 -cnotmatch '^[a-f0-9]{64}$' -or (Get-FileHash -LiteralPath $actual).Hash.ToLowerInvariant() -cne $file.sha256) { throw 'Owned file bytes, type or SHA mismatch.' }
        $paths.Add($path)
    }
    # New large files must already be covered by the repo's established LFS rules.
    $attributes = GitPaths 'lfs-rules' @('check-attr','filter') $paths.ToArray()
    $attributeBytes=$null
    if($manifest.PSObject.Properties.Name -contains 'attribute_change' -and $null -ne $manifest.attribute_change){
        $change=$manifest.attribute_change;$attributePath=Join-Path $repository '.gitattributes'
        if((Git 'attribute-worktree' @('diff','--name-only','--','.gitattributes')) -or (Get-FileHash -LiteralPath $attributePath).Hash.ToLowerInvariant() -cne $change.before_sha256){throw 'Attributes changed outside this reviewed append plan.'}
        $old=[IO.File]::ReadAllBytes($attributePath);$append="`n"
        $additions=@($change.paths);if($additions.Count -gt 1000){throw 'LFS addition bound1000.'}
        foreach($path in $additions){Check;if($path -notin $paths){throw 'LFS additions must be manifest-owned paths.'};$append+=$path+' filter=lfs diff=lfs merge=lfs -text'+"`n"}
        if($change.PSObject.Properties.Name -contains 'verbatim_paths'){
            $verbatim=@($change.verbatim_paths);if($verbatim.Count -gt 1000){throw 'Verbatim rule bound1000.'}
            foreach($path in $verbatim){Check;if($path -notin $paths -or $path -in $additions){throw 'Verbatim paths must be owned, separate from LFS additions.'};$append+=$path+' -text'+"`n"}
        }
        $attributeBytes=[byte[]]($old+[Text.UTF8Encoding]::new($false).GetBytes($append))
        if([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($attributeBytes)).ToLowerInvariant() -cne $change.after_sha256){throw 'Reviewed attributes after-SHA differs.'}
    }
    foreach ($file in $files) { Check; if ($file.bytes -ge 10MB -and -not ($attributes -split "`n" | Where-Object { $_.TrimEnd("`r") -ceq ($file.path+': filter: lfs') }) -and ($null -eq $attributeBytes -or $file.path -notin $manifest.attribute_change.paths)) { throw 'Large run file lacks a current or reviewed new LFS rule.' } }
    if ($PreviewOnly) { $result.status='PASS_preview_paths_hashes_destination_and_ref_only'; return Snapshot }
    $validationPath=Join-Path $task ('independent-validation-'+$Kind+'.json')
    if (-not (Test-Path -LiteralPath $validationPath)) { throw 'Kind-specific independent validation receipt required before actual publication.' }
    $validation=Get-Content -LiteralPath $validationPath -Raw | ConvertFrom-Json
    if ($validation.run_id -cne $RunId -or $validation.status -cne 'PASS' -or -not $validation.new_process -or -not $validation.saved_bytes_verified -or $validation.owned_manifest_sha256 -cne (Get-FileHash -LiteralPath $OwnedManifest).Hash.ToLowerInvariant()) { throw 'Independent new-process saved-byte validation is not bound to this owned manifest.' }
    [void](Git 'ancestor' @('merge-base','--is-ancestor',$ExpectedRemoteCommit,'HEAD'))
    $localHead=Git 'local-base' @('rev-parse','HEAD');if($localHead -cne $ExpectedRemoteCommit){throw 'Publication checkout must start at the observed remote commit, without unrelated local commits.'}
    if($null -ne $attributeBytes){[IO.File]::WriteAllBytes((Join-Path $repository '.gitattributes'),$attributeBytes);$paths.Add('.gitattributes')}
    [void](GitPaths 'stage' @('add') $paths.ToArray())
    $owned=Git 'staged-owned' @('diff','--cached','--name-only')
    if (-not $owned) { $result.status='NO_CHANGES_no_commit_no_push'; return Snapshot }
    if (@($owned -split "`n" | Where-Object { $_.TrimEnd("`r") -notin $paths }).Count -gt 0) { throw 'Index contains a path outside this manifest.' }
    [void](GitPaths 'staged-check' @('-c','core.whitespace=cr-at-eol','diff','--cached','--check') $paths.ToArray())
    [void](Git 'commit' @('commit','-m',('training: save '+$Kind+' run '+$RunId)))
    $commit=Git 'commit-id' @('rev-parse','HEAD'); $result.publication_commit=$commit
    if($CommitOnly){$result.status='PASS_owned_commit_saved_remote_NOT_PUSHED';return Snapshot}
    [void](Git 'push' @('push','--porcelain','origin',($commit+':refs/heads/'+$Branch)))
    $after=RemoteRef 'remote-after'
    if ($after -cnotmatch '^([a-f0-9]{40})\s+refs/heads/' -or $Matches[1] -cne $commit) { throw 'Remote commit was not confirmed; read-only reconciliation required.' }
    $result.status='PASS_git_ref_pushed_full_public_readback_still_required'
    Snapshot
} finally {
    $result.calls=$calls; $result.receipts=$receipts.ToArray(); $result.elapsed_seconds=$watch.Elapsed.TotalSeconds
    [IO.File]::WriteAllText($receiptPath,($result|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
}
