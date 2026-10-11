param(
    [Parameter(Mandatory)][string]$SpecificationPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateRange(1,8192)][int]$MaximumFiles=8192,
    [ValidateRange(1,1800)][int]$MaximumSeconds=1800,
    [ValidateRange(1,137438953472)][long]$MaximumBytes=137438953472,
    [string]$CancelFile='',
    [switch]$LocalFixture
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required.'}
$watch=[Diagnostics.Stopwatch]::StartNew()
$encoding=[Text.UTF8Encoding]::new($false)
$files=[Collections.Generic.List[object]]::new()
$destinations=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
$hashes=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
$pending=[Collections.Generic.List[string]]::new()
$verifiedBytes=0L;$lastProgress=0.0;$failed=$null;$createdOutput=$false
$out=[IO.Path]::GetFullPath($OutputDirectory)
function Guard {
    if($watch.Elapsed.TotalSeconds -ge $MaximumSeconds -or ($CancelFile -and [IO.File]::Exists($CancelFile))){throw 'Delivery deadline/cancel.'}
}
function Value($Object,[string]$Name,$Default=$null){
    if($null -ne $Object -and $Object.Contains($Name)){return $Object[$Name]}
    return $Default
}
function Pin([string]$Value,[int]$Length,[string]$Label){
    if($Value -cnotmatch ('^[a-f0-9]{'+$Length+'}$')){throw "Pinned $Label must be lowercase $Length hex."}
}
function Within([string]$Root,[string]$Relative){
    Guard
    $rootPath=[IO.Path]::GetFullPath($Root)
    $parts=$Relative.Replace('\','/').Split('/')
    if($parts.Count -gt 16 -or $Relative.Length -gt 1024 -or $Relative -match '[:\x00-\x1f]' -or $parts.Count -eq 0 -or @($parts|Where-Object {$_ -eq '' -or $_ -eq '.' -or $_ -eq '..'}).Count -gt 0){throw 'Unsafe relative transport path.'}
    $path=$rootPath
    if((Get-Item -LiteralPath $rootPath).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked asset root refused.'}
    foreach($part in $parts){
        Guard;$path=Join-Path $path $part
        if(Test-Path -LiteralPath $path){if((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked asset path refused.'}}
    }
    $path=[IO.Path]::GetFullPath($path)
    if(-not $path.StartsWith($rootPath.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Transport path escaped asset root.'}
    return $path
}
function Hash([string]$Path){
    Guard;$absolute=[IO.Path]::GetFullPath($Path)
    if($hashes.ContainsKey($absolute)){return $hashes[$absolute]}
    $before=Get-Item -LiteralPath $absolute
    if($before.PSIsContainer -or ($before.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $before.Length -gt 8GB){throw 'Regular asset file/8GiB bound required.'}
    $stream=[IO.File]::Open($absolute,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $digest=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    $buffer=[byte[]]::new(1MB);$readBytes=0L;$ended=$false
    try {
        for($block=0;$block -lt 8193;$block++){
            Guard;$count=$stream.Read($buffer,0,$buffer.Length)
            if($count -eq 0){$ended=$true;break}
            $readBytes+=$count
            if($readBytes -gt 8GB -or $script:verifiedBytes+$count -gt $MaximumBytes){throw 'Hashed byte budget exceeded.'}
            $digest.AppendData($buffer,0,$count);$script:verifiedBytes+=$count
            if($watch.Elapsed.TotalSeconds-$script:lastProgress -ge 2){Write-Host "delivery SHA files=$($files.Count) bytes=$script:verifiedBytes elapsed=$([Math]::Round($watch.Elapsed.TotalSeconds,1))s";$script:lastProgress=$watch.Elapsed.TotalSeconds}
        }
        if(-not $ended -or $readBytes -ne $before.Length){throw 'Asset EOF/byte denominator differs.'}
        $sha=[Convert]::ToHexString($digest.GetHashAndReset()).ToLowerInvariant()
    } finally {$digest.Dispose();$stream.Dispose()}
    $after=Get-Item -LiteralPath $absolute
    if($after.Length -ne $before.Length -or $after.LastWriteTimeUtc -ne $before.LastWriteTimeUtc){throw 'Asset changed during hashing.'}
    $hashes.Add($absolute,$sha);return $sha
}
function Json([string]$Path){
    Guard
    if((Get-Item -LiteralPath $Path).Length -gt 64MB){throw 'JSON metadata byte bound64MiB.'}
    return [IO.File]::ReadAllText($Path,$encoding)|ConvertFrom-Json -AsHashtable
}
function WriteJson([string]$Name,$Object){
    $path=Join-Path $out $Name
    $stream=[IO.FileStream]::new($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$bytes=$encoding.GetBytes(($Object|ConvertTo-Json -Depth 20));$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)}finally{$stream.Dispose()}
}
function AddAsset([string]$Root,[string]$Relative,[string]$Prefix,[string]$Expected,$ExpectedBytes=$null){
    Guard;Pin $Expected 64 'asset SHA'
    $relativePath=$Relative.Replace('\','/');$destination=$Prefix+'/'+$relativePath
    if($destinations.ContainsKey($destination)){
        $existing=$destinations[$destination]
        if($existing.sha256 -cne $Expected -or $existing.source -cne (Within $Root $Relative)){throw 'Conflicting transport destination.'}
        return
    }
    if($files.Count -ge $MaximumFiles){throw 'Transport file item budget exceeded.'}
    $path=Within $Root $Relative;$item=Get-Item -LiteralPath $path
    if($null -ne $ExpectedBytes -and $item.Length -ne [long]$ExpectedBytes){throw 'Pinned asset byte count differs.'}
    $actual=Hash $path
    if($actual -cne $Expected){throw "Asset SHA differs: $destination"}
    $entry=[ordered]@{source=$path;destination=$destination;bytes=$item.Length;sha256=$actual}
    $files.Add($entry);$destinations.Add($destination,$entry)
}
function AddExternal([string]$Path,[string]$Destination,[string]$Expected){
    $absolute=[IO.Path]::GetFullPath($Path)
    AddAsset ([IO.Path]::GetDirectoryName($absolute)) ([IO.Path]::GetFileName($absolute)) $Destination $Expected
}
function Pending([string]$Reason){if(-not $pending.Contains($Reason)){$pending.Add($Reason)}}
function TransportBytes {
    $sum=0L
    foreach($file in $files){Guard;$sum+=[long]$file['bytes']}
    return $sum
}

try {
    # A cooperative payload does no file hashing before the guardian registers it.
    if($env:SEZIKA_START_GATE){
        $ready=$false
        for($attempt=0;$attempt -lt 160;$attempt++){
            Guard
            if($watch.Elapsed.TotalSeconds -ge 40){throw 'Delivery startup deadline40s.'}
            if([IO.File]::Exists((Join-Path $env:SEZIKA_START_GATE 'go'))){$ready=$true;break}
            Start-Sleep -Milliseconds 250
        }
        if(-not $ready){throw 'Delivery startup iteration bound160.'}
    }
    $spec=Json $SpecificationPath
    $specificationSha=Hash $SpecificationPath
    $preparationToolSha=Hash $PSCommandPath
    if((Value $spec 'SchemaVersion') -ne 1 -or (Value $spec 'RunId') -cnotmatch '^[a-z0-9][a-z0-9-]{2,63}$'){throw 'Version1 and explicit run ID required.'}
    if($LocalFixture -and (Value $spec 'RunId') -cnotmatch '^fixture-'){throw 'Fixture run ID required.'}
    $corpusRoot=[IO.Path]::GetFullPath((Value $spec 'CorpusRoot'))
    $modelRoot=[IO.Path]::GetFullPath((Value $spec 'BaseModelRoot'))
    if($out.StartsWith($corpusRoot.TrimEnd('\','/')+'\',[StringComparison]::OrdinalIgnoreCase) -or $out -eq $corpusRoot -or $out.StartsWith($modelRoot.TrimEnd('\','/')+'\',[StringComparison]::OrdinalIgnoreCase) -or $out -eq $modelRoot){throw 'Delivery output cannot modify an input asset root.'}
    if(Test-Path -LiteralPath $out){throw 'Fresh output directory required; preserve previous delivery.'}
    [void][IO.Directory]::CreateDirectory($out);$createdOutput=$true
    $sourceCommit=Value $spec 'SourceCommit';$sourceBaselineCommit=$null
    if($sourceCommit){Pin $sourceCommit 40 'source commit'}else{Pending 'final_source_commit'}
    if((Value $spec 'SourceCommitIsFinal' $true) -ne $true){$sourceBaselineCommit=$sourceCommit;$sourceCommit=$null;Pending 'final_source_commit'}
    foreach($name in @('DatasetCommit','ModelCommit')){Pin (Value $spec $name) 40 $name}
    $manifestSha=Value $spec 'CorpusManifestSha256';$bindingsSha=Value $spec 'CorpusBindingsSha256'
    AddAsset $corpusRoot 'manifest.json' 'corpus' $manifestSha
    AddAsset $corpusRoot 'corpus-bindings.json' 'corpus' $bindingsSha
    $manifest=Json (Within $corpusRoot 'manifest.json');$binding=Json (Within $corpusRoot 'corpus-bindings.json')
    $shards=@(Value $manifest 'shards');$physical=[long](Value $manifest 'records_written')
    if((Value $manifest 'schema_version') -ne 1 -or (Value $manifest 'status') -cne 'COMPLETE_PREPARATION_ONLY' -or $shards.Count -lt 1 -or $shards.Count -gt 4096 -or $physical -lt 1 -or $physical -gt 50000000){throw 'Closed complete corpus and physical/shard bounds required.'}
    if(Value $manifest 'pilot' $false){Pending 'pilot_corpus_cannot_be_full_training'}
    if((Value $binding 'ManifestSha256') -cne $manifestSha -or (Value $binding 'FormalTrainingAdmitted' $false)){throw 'Manifest binding/experimental status differs.'}
    AddAsset $corpusRoot 'exclusions.jsonl' 'corpus' (Value $binding 'ExclusionsSha256')
    AddAsset $corpusRoot 'source-rights.json' 'corpus' (Value $binding 'SourceRightsSha256')
    AddAsset $corpusRoot (Value $binding 'SourceAssetsPath' 'source-assets.json') 'corpus' (Value $binding 'SourceAssetsSha256')
    $rows=0L;$trainSources=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $shardNames=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($shard in $shards){
        Guard
        if(-not $shardNames.Add($shard.path) -or $shard.split -cnotin @('train','development') -or [long]$shard.records -lt 1 -or [long]$shard.records -gt 50000 -or [long]$shard.bytes -lt 1 -or [long]$shard.bytes -gt 2GB){throw 'Shard path/split/row/byte bounds differ.'}
        $rows+=[long]$shard.records
        if($shard.split -ceq 'train'){if([string]::IsNullOrWhiteSpace($shard.source)){throw 'Training source absent.'};[void]$trainSources.Add($shard.source)}
        AddAsset $corpusRoot $shard.path 'corpus' $shard.sha256 $shard.bytes
    }
    if($rows -ne $physical -or $trainSources.Count -lt 1 -or $trainSources.Count -gt 128){throw 'Full physical/source denominator differs.'}
    $notices=Value $manifest 'source_notice_bindings' ([ordered]@{})
    if($notices.Count -gt 2048){throw 'Bound source metadata item budget2048.'}
    foreach($notice in $notices.GetEnumerator()){Guard;AddAsset $corpusRoot $notice.Key 'corpus' $notice.Value}
    $policyPath=Value $spec 'SourcePolicyPath';$policySha=Value $spec 'SourcePolicySha256'
    if(-not $policyPath -or -not $policySha){Pending 'source_policy_identity'}else{
        AddExternal $policyPath 'audit/source-policy' $policySha
        $namedPolicySha=Value $notices 'source-scope-policy.json'
        $bindingPolicySha=Value $binding 'SourcePolicySha256'
        if(($namedPolicySha -and $namedPolicySha -cne $policySha) -or ($bindingPolicySha -and $bindingPolicySha -cne $policySha)){throw 'Named source policy bindings disagree.'}
        $policyBound=$bindingPolicySha -ceq $policySha -or $namedPolicySha -ceq $policySha
        if($policyBound){AddAsset $corpusRoot 'source-scope-policy.json' 'corpus' $policySha}
        if(-not $policyBound){Pending 'source_policy_not_bound_to_corpus'}
    }
    $modelReceiptPath=Value $spec 'ModelReadbackReceiptPath';$modelReceiptSha=Value $spec 'ModelReadbackReceiptSha256'
    AddExternal $modelReceiptPath 'audit/model-readback' $modelReceiptSha
    $modelReceipt=Json $modelReceiptPath
    if((Value $modelReceipt 'status') -cne 'PASS_local_complete_base_and_run_full_bytes_sha256' -or (Value $modelReceipt 'source_model_commit') -cne (Value $spec 'ModelCommit') -or [IO.Path]::GetFullPath((Value $modelReceipt 'source_checkout')) -ine $modelRoot){throw 'Fixed model readback/source commit/root differs.'}
    $baseCount=[int](Value $modelReceipt 'base_files');$modelFiles=@(Value $modelReceipt 'files')
    if($baseCount -lt 3 -or $baseCount -gt 256 -or $modelFiles.Count -lt $baseCount -or $modelFiles.Count -gt 1000){throw 'Complete model base asset denominator differs.'}
    for($index=0;$index -lt $baseCount;$index++){Guard;$asset=$modelFiles[$index];AddAsset $modelRoot $asset.path 'base-model' $asset.sha256 $asset.bytes}
    if(-not $destinations.ContainsKey('base-model/model.json')){throw 'Bound model.json required.'}
    $model=Json (Within $modelRoot 'model.json')
    $encoderPath=Value $model 'weights_file';$encoderSha=Value $model 'weights_sha256'
    $tokenizerPath=Value $model 'tokenizer_file';$tokenizerSha=Value $model 'tokenizer_sha256'
    foreach($pair in @(@($encoderPath,$encoderSha),@($tokenizerPath,$tokenizerSha))){
        if(-not $destinations.ContainsKey('base-model/'+$pair[0]) -or $destinations['base-model/'+$pair[0]].sha256 -cne $pair[1]){throw 'Encoder/tokenizer is outside the complete bound model base.'}
    }
    $lengthPath=Value $spec 'LengthReportPath';$lengthSha=Value $spec 'LengthReportSha256';$length=$null
    $maxTokens=[int](Value $spec 'MaximumTokens' 8192)
    if(-not $lengthPath -or -not $lengthSha){Pending 'complete_strict_length_receipt'}else{
        AddExternal $lengthPath 'audit/lengths' $lengthSha;$length=Json $lengthPath
        if((Value $length 'Status') -cne 'PASS_full_corpus_strict_lengths_only'){Pending 'strict_length_receipt_not_complete_PASS'}else{
            if($length.CorpusManifestSha256 -cne $manifestSha -or $length.CorpusBindingsSha256 -cne $bindingsSha -or $length.TokenizerSha256 -cne $tokenizerSha -or $length.MaximumTokens -ne $maxTokens -or $length.PhysicalRows -ne $physical -or $length.Rejected -ne 0 -or $length.PhysicalRows -ne $length.ExcludedRows+$length.Accepted -or $length.Accepted -ne $length.Train+$length.Development -or $length.Train -lt 2 -or $length.Development -lt 1 -or $length.GpuFeaturesComputed -or $length.FormalTrainingAdmitted){throw 'Full identity-bound strict length denominator differs.'}
            if($length.TrainingBySource.Count -lt 1 -or $length.TrainingBySource.Count -gt 128 -or @($length.TrainingBySource.Keys|Where-Object {-not $trainSources.Contains($_)}).Count -gt 0 -or @($length.TrainingBySource.Values|Where-Object {$_ -lt 1}).Count -gt 0 -or ($length.TrainingBySource.Values|Measure-Object -Sum).Sum -ne $length.Train){throw 'Full training-by-source denominator differs.'}
            if($length.LongestTokens -lt 2 -or $length.LongestTokens -gt $maxTokens -or $length.LongestTrainingTokens -lt 2 -or $length.LongestTrainingTokens -gt $length.LongestTokens -or [string]::IsNullOrWhiteSpace($length.LongestTrainingRecordId) -or [string]::IsNullOrWhiteSpace($length.LongestTrainingSource)){throw 'Complete maximum length identities required.'}
            $hasScanIdentity=$length.Contains('CompleteScan') -or $length.Contains('SourcePolicySha256') -or $length.Contains('ScanCompletionSha256') -or $binding.Contains('LengthScanSha256')
            if($hasScanIdentity){
                $scanSha=Value $binding 'LengthScanSha256'
                if((Value $length 'CompleteScan') -ne $true -or -not $policySha -or (Value $length 'SourcePolicySha256') -cne $policySha -or (Value $binding 'SourcePolicySha256') -cne $policySha -or (Value $length 'ScanCompletionSha256') -cne $scanSha){throw 'Complete length/corpus/source policy three-way identity differs.'}
                AddAsset $corpusRoot 'length-scan-complete.json' 'corpus' $scanSha
                $scan=Json (Within $corpusRoot 'length-scan-complete.json')
                if((Value $scan 'Status' '') -cnotmatch '^PASS_complete_full_corpus' -or (Value $scan 'CompleteScan') -ne $true -or (Value $scan 'SourcePolicySha256') -cne $policySha){throw 'Partial or unbound complete-scan receipt rejected.'}
                if($scan.Contains('UnresolvedErrors') -and $scan.UnresolvedErrors -ne 0){throw 'Complete scan has unresolved errors.'}
            }
            $failureLedger=Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($lengthPath))) 'length-failures.jsonl'
            if(-not [IO.File]::Exists($failureLedger)){Pending 'length_failure_ledger_identity'}else{AddExternal $failureLedger 'audit/length-failures' $length.FailureLedgerSha256}
        }
    }
    $bundleRoot=Value $spec 'ToolBundleRoot';$bundleSha=Value $spec 'ToolBundleManifestSha256';$engineering=$null
    if(-not $bundleRoot -or -not $bundleSha){Pending 'frozen_linux_tool_bundle'}else{
        AddAsset $bundleRoot 'files.sha256' 'tool-bundle' $bundleSha
        if((Get-Item -LiteralPath (Within $bundleRoot 'files.sha256')).Length -gt 1MB){throw 'Tool bundle manifest byte bound1MiB.'}
        $lines=[IO.File]::ReadAllLines((Within $bundleRoot 'files.sha256'),$encoding)
        if($lines.Count -lt 1 -or $lines.Count -gt 600){throw 'Frozen tool bundle file count bound600.'}
        foreach($line in $lines){
            Guard
            if($line -cnotmatch '^([a-f0-9]{64})  (.+)$'){throw 'Tool bundle manifest line differs.'}
            $assetSha=$Matches[1];$relative=$Matches[2]
            if($relative -ceq 'files.sha256'){throw 'Self-including tool manifest refused.'}
            AddAsset $bundleRoot $relative 'tool-bundle' $assetSha
        }
        foreach($required in @('engineering-sources.json','guardian.sh','Sezika.RepositoryTrainer/Sezika.RepositoryTrainer','Sezika.FullTrainingChecks/Sezika.FullTrainingChecks')){if(-not $destinations.ContainsKey('tool-bundle/'+$required)){throw 'Complete Linux tool bundle required.'}}
        $engineering=Json (Within $bundleRoot 'engineering-sources.json')
        if(-not $sourceCommit){Pending 'frozen_source_commit_binding'}elseif((Value $engineering 'source_commit') -cne $sourceCommit){throw 'Tool bundle source commit differs.'}
        $sourceRoot=Value $spec 'SourceRoot';$sourceFiles=@(Value $engineering 'files')
        if(-not $sourceRoot){Pending 'frozen_source_bytes_root'}elseif($sourceFiles.Count -lt 1 -or $sourceFiles.Count -gt 256){throw 'Engineering source item bound256.'}else{
            foreach($asset in $sourceFiles){Guard;AddAsset $sourceRoot $asset.path 'engineering-sources' $asset.sha256 $asset.bytes}
        }
    }
    $epochs=[int](Value $spec 'Epochs' 1);$train=if($length -and (Value $length 'Status') -ceq 'PASS_full_corpus_strict_lengths_only'){[long]$length.Train}else{$null}
    $seed=[int](Value $spec 'Seed' 17);$checkpoint=[int](Value $spec 'CheckpointEvery' 128);$validation=[int](Value $spec 'ValidationPerSource' 32)
    $encoderRate=[double](Value $spec 'EncoderRate' 0.00001);$headRate=[double](Value $spec 'HeadRate' 0.001);$decay=[double](Value $spec 'WeightDecay' 0.01);$owned=[long](Value $spec 'MaximumOwnedBytes' 25769803776)
    if($epochs -lt 1 -or $epochs -gt 16 -or ($null -ne $train -and $train*$epochs -gt 800000000) -or $maxTokens -lt 2 -or $maxTokens -gt 8192 -or $checkpoint -lt 1 -or $checkpoint -gt 1000000 -or $validation -lt 1 -or $validation -gt 32 -or -not [double]::IsFinite($encoderRate) -or $encoderRate -le 0 -or $encoderRate -gt .01 -or -not [double]::IsFinite($headRate) -or $headRate -le 0 -or $headRate -gt .1 -or -not [double]::IsFinite($decay) -or $decay -lt 0 -or $decay -gt 1 -or $owned -lt 1048576 -or $owned -gt 25769803776){throw 'Full CorpusRunPlan engineering bounds differ.'}
    $totalBytes=TransportBytes
    $localCopies=[int](Value $spec 'LocalArchiveCopies' 0);$localReserve=[long](Value $spec 'LocalReserveBytes' 1073741824);$serverReserve=[long](Value $spec 'ServerReserveBytes' 4294967296)
    if($localCopies -lt 0 -or $localCopies -gt 3 -or $localReserve -lt 0 -or $serverReserve -lt 0){throw 'Explicit archive copies/reserve bounds required.'}
    $weightEstimate=[long]$destinations['base-model/'+$encoderPath].bytes+1MB
    $localRequired=$totalBytes*$localCopies+$localReserve
    $serverRequired=$totalBytes+12*$weightEstimate+$serverReserve
    $drive=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($out));$localFree=$drive.AvailableFreeSpace
    if($localFree -lt $localRequired){Pending 'local_transport_volume_capacity'}
    $offlinePending=@($pending.ToArray());$offlineReady=$offlinePending.Count -eq 0
    $instance=Value $spec 'NewInstance' ([ordered]@{});$uuid=Value $instance 'GpuUuid';$remoteCorpus=Value $instance 'CorpusRoot';$remoteModel=Value $instance 'BaseModelRoot';$remoteLength=Value $instance 'LengthReportPath'
    if(-not $uuid){Pending 'new_gpu_uuid'}else{Pin $uuid 32 'actual new GPU UUID'}
    foreach($remotePath in @($remoteCorpus,$remoteModel,$remoteLength)){
        if(-not $remotePath){Pending 'fixed_new_instance_linux_paths'}elseif($remotePath -cnotmatch '^/[a-zA-Z0-9_./-]+$' -or $remotePath -match '(^|/)\.\.(/|$)'){throw 'Safe explicit Linux asset path required.'}
    }
    $preflightPath=Value $instance 'PreflightReceiptPath';$preflightSha=Value $instance 'PreflightReceiptSha256';$preflight=$null
    if(-not $preflightPath -or -not $preflightSha){Pending 'new_instance_readonly_preflight'}else{
        AddExternal $preflightPath 'audit/new-instance-preflight' $preflightSha;$preflight=Json $preflightPath
        if($preflight.GpuUuid -cne $uuid -or $preflight.DriverVersion -lt 12080 -or $preflight.TotalMemoryBytes -lt 30GB -or $preflight.GpuName -notmatch '5090' -or $preflight.ComputeUsers -ne 0 -or [string]::IsNullOrWhiteSpace($preflight.BootId)){throw 'Actual new-instance GPU/driver/ownership identity differs.'}
        if($preflight.FreeDataDiskBytes -lt $serverRequired){Pending 'new_instance_data_disk_capacity'}
    }
    $leaseEnd=Value $instance 'LeaseEndUtc';$remaining=$null
    if(-not $leaseEnd){Pending 'new_lease_deadline'}else{
        if($leaseEnd -notmatch 'Z$'){throw 'Explicit UTC Z lease deadline required.'}
        $remaining=([DateTimeOffset]::Parse($leaseEnd)-[DateTimeOffset]::UtcNow).TotalSeconds
        if($remaining -le 0){Pending 'new_lease_expired'}
    }
    $plan=[ordered]@{SchemaVersion=1;RunId=$spec.RunId;CorpusRoot=$remoteCorpus;CorpusManifestSha256=$manifestSha;CorpusBindingsSha256=$bindingsSha;LengthReportPath=$remoteLength;LengthReportSha256=$lengthSha;BaseModelRoot=$remoteModel;SourceCommit=$sourceCommit;DatasetCommit=$spec.DatasetCommit;ModelCommit=$spec.ModelCommit;Seed=$seed;Epochs=$epochs;TrainRecords=$train;MaximumTokens=$maxTokens;CheckpointEvery=$checkpoint;ValidationPerSource=$validation;EncoderRate=$encoderRate;HeadRate=$headRate;WeightDecay=$decay;MaximumOwnedBytes=$owned;ExpectedGpuUuid=$uuid;ExperimentalOnly=$true}
    WriteJson 'corpus-run-plan.pending.json' $plan
    $planSha=Hash (Join-Path $out 'corpus-run-plan.pending.json')
    $profilePath=Value $instance 'ProfileReceiptPath';$profileSha=Value $instance 'ProfileReceiptSha256';$profile=$null;$phaseEstimate=$null;$fullEstimate=$null
    if(-not $profilePath -or -not $profileSha){Pending 'same_plan_new_instance_real_profile'}else{
        AddExternal $profilePath 'audit/new-instance-profile' $profileSha;$profile=Json $profilePath
        $backwardSha=Value $spec 'BackwardManifestSha256'
        if(-not $backwardSha){Pending 'frozen_backward_kernel_identity'}else{
            Pin $backwardSha 64 'backward manifest SHA'
            $schedule='source_round_robin_v1;source_order=sha256(seed,epoch,source);within_source=manifest_shard_row_order;train_only;no_oversampling'
            $recordHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($encoding.GetBytes($manifestSha+"`n"+$bindingsSha+"`n"+$binding.ExclusionsSha256+"`n"+$lengthSha))).ToLowerInvariant()
            $orderHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($encoding.GetBytes($schedule+"`n"+$seed+"`n"+$epochs+"`n"+$train+"`n"+$manifestSha))).ToLowerInvariant()
            $expected=[ordered]@{PlanSha256=$planSha;DataManifestSha256=$manifestSha;RecordsSha256=$recordHash;BaseEncoderSha256=$encoderSha;TokenizerSha256=$tokenizerSha;OrderSha256=$orderHash;BackwardManifestSha256=$backwardSha}
            foreach($identity in $expected.GetEnumerator()){Guard;if((Value $profile.Inputs $identity.Key) -cne $identity.Value){throw 'Same fixed plan/profile input identity differs.'}}
        }
        if($profile.Status -cne 'PASS_real_base_corpus_profile_only' -or $profile.RunId -cne $spec.RunId -or $profile.GpuUuid -cne $uuid -or -not $length -or $profile.MaximumTokensTested -lt $length.LongestTokens -or -not [double]::IsFinite([double]$profile.MaximumStepSeconds) -or $profile.MaximumStepSeconds -le 0 -or $profile.QualityAccepted){throw 'Complete real profile/current UUID/maximum length differs.'}
        $marker=Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($profilePath))) 'resources-reclaimed.txt'
        if(-not [IO.File]::Exists($marker)){Pending 'profile_resources_reclaimed_marker'}else{AddExternal $marker 'audit/profile-resources' (Hash $marker)}
        $stage=[long](Value $spec 'StageUpdates' 100);$reserve=[long](Value $spec 'RemainingPhaseReserveSeconds' 1800)
        if($null -eq $train -or $stage -lt 1 -or $stage -gt $train*$epochs -or $reserve -lt 0){throw 'Explicit stage/global update and remaining-phase reserve bounds differ.'}
        $phaseEstimate=4*$profile.MaximumStepSeconds*$stage+600+120*(1+[Math]::Floor($stage/$checkpoint))
        $fullEstimate=4*$profile.MaximumStepSeconds*$train*$epochs+600+120*(1+[Math]::Floor($train*$epochs/$checkpoint))
        if($null -eq $remaining -or $remaining -lt $phaseEstimate+$reserve){Pending 'new_lease_remaining_stage_and_required_phase_budget'}
    }
    # Include the optional new-instance audit files added after the first disk
    # estimate. A partial file denominator must never become a complete manifest.
    $totalBytes=TransportBytes
    $localRequired=$totalBytes*$localCopies+$localReserve
    $serverRequired=$totalBytes+12*$weightEstimate+$serverReserve
    if($localFree -lt $localRequired){Pending 'local_transport_volume_capacity';$offlineReady=$false}
    if($preflight -and $preflight.FreeDataDiskBytes -lt $serverRequired){Pending 'new_instance_data_disk_capacity'}
    if($LocalFixture){Pending 'synthetic_fixture_not_real_training_assets';$offlineReady=$false}
    # The actual C# entrypoint rechecks runtime GPU/profile/cursor identities.
    Pending 'remote_CSharp_runtime_admission_NOT_RUN'
    WriteJson 'training-inputs.json' ([ordered]@{SchemaVersion=1;RunId=$spec.RunId;SpecificationSha256=$specificationSha;PreparationToolSha256=$preparationToolSha;SourceBaselineCommit=$sourceBaselineCommit;SourceCommit=$sourceCommit;DatasetCommit=$spec.DatasetCommit;ModelCommit=$spec.ModelCommit;CorpusManifestSha256=$manifestSha;CorpusBindingsSha256=$bindingsSha;SourcePolicySha256=$policySha;LengthReportSha256=$lengthSha;BaseEncoderSha256=$encoderSha;TokenizerSha256=$tokenizerSha;PlanSha256=$planSha;TrainRecords=$train;Epochs=$epochs;GlobalUpdates=if($null -ne $train){$train*$epochs}else{$null};Initialization='fixed_base_encoder_zero_new_head';ExperimentalOnly=$true;FormalTrainingAdmitted=$false;PublicPublicationAccepted=$false})
    WriteJson 'transport-manifest.json' ([ordered]@{SchemaVersion=1;Status='PASS_all_declared_transport_bytes_SHA_only';RunId=$spec.RunId;Files=@($files.ToArray());FilesCount=$files.Count;TotalInputBytes=$totalBytes;PayloadCopied=$false;ArchiveCreated=$false;PrivateTrainingData=$true;PublicPublicationAccepted=$false;SyntheticFixture=[bool]$LocalFixture})
    WriteJson 'readiness.json' ([ordered]@{SchemaVersion=1;Status='PENDING_explicit_new_instance_runtime_admission';RunId=$spec.RunId;LocalDeliveryReady=$offlineReady;OfflinePending=$offlinePending;Pending=@($pending.ToArray());TrainingReady=$false;FormalTrainingAdmitted=$false;SyntheticFixture=[bool]$LocalFixture;FilesVerified=$files.Count;BytesHashed=$verifiedBytes;MaximumFiles=$MaximumFiles;MaximumBytes=$MaximumBytes;MaximumSeconds=$MaximumSeconds;ElapsedSeconds=$watch.Elapsed.TotalSeconds;Storage=@{LocalFreeBytes=$localFree;LocalRequiredBytes=$localRequired;LocalArchiveCopies=$localCopies;ServerRequiredBytesEstimate=$serverRequired;ServerEstimate='inputs+two_3x_weight_checkpoints+final_3x_weight_package+3x_weight_archive+reserve; actual lease disk still required'};StageSecondsEstimate=$phaseEstimate;FullEpochsSecondsEstimate=$fullEstimate;EstimatesAreMeasuredCompletion=$false;GpuTrainingRun=$false;OldServerContacted=$false;NetworkPerformed=$false;PublicPublicationAccepted=$false})
    Write-Host "delivery complete files=$($files.Count) input_bytes=$totalBytes local_ready=$offlineReady; GPU training remains Pending"
} catch {
    $failed=$_.Exception.Message
    if($createdOutput){
        WriteJson 'failure.json' ([ordered]@{Status='FAIL_partial_files_never_full_PASS';Error=$failed;ScriptStackTrace=$_.ScriptStackTrace;VerifiedFiles=@($files.ToArray());VerifiedFileCount=$files.Count;BytesHashed=$verifiedBytes;ElapsedSeconds=$watch.Elapsed.TotalSeconds;TrainingReady=$false;GpuTrainingRun=$false;NetworkPerformed=$false;SyntheticFixture=[bool]$LocalFixture})
    }
    throw
}
