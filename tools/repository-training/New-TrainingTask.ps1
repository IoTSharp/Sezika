param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]{2,63}$')][string]$RunId,
    [ValidateSet('encoder-full')][string]$Method = 'encoder-full',
    [ValidatePattern('^[a-f0-9]{40}$')][string]$DatasetCommit = 'b8ea13b81aaaca722baf6a3c0f12e2217fc4353e',
    [ValidatePattern('^[a-f0-9]{40}$')][string]$ModelCommit = '06ebe82325e58c0ae723fb9014a38cdb2f87b22a',
    [ValidateRange(300,86400)][int]$MaximumSeconds = 14400,
    [ValidateRange(1,1000000)][int]$MaximumRecords = 100000,
    [ValidateRange(300,7200)][int]$TransferReserveSeconds = 1800
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 required.' }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$task = Join-Path $repo ('.artifacts\repository-training-' + $RunId)
if (Test-Path -LiteralPath $task) { throw 'Task directory already exists; preserve it and choose a new run ID.' }
if ($TransferReserveSeconds -ge $MaximumSeconds) { throw 'Transfer reserve must be smaller than the total task budget.' }
$watch = [Diagnostics.Stopwatch]::StartNew()
function Check { if ($watch.Elapsed.TotalSeconds -ge 30) { throw 'Task preparation exceeded 30 seconds.' } }
$head = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -cnotmatch '^[a-f0-9]{40}$') { throw 'Cannot resolve source commit.' }
$dirty = @(& git -C $repo status --porcelain=v1 --untracked-files=no)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect worktree.' }
Check
$plan = [ordered]@{
    schema_version = 1; run_id = $RunId; created_utc = [DateTime]::UtcNow.ToString('o')
    status = 'PREPARED_configuration_only_NOT_READY_for_rental'; method = $Method
    source = @{ url='https://github.com/IoTSharp/Sezika.git'; commit=$head; worktree_has_changes=($dirty.Count -gt 0); new_tools_require_frozen_overlay_or_source_commit=$true }
    dataset = @{ url='https://moark.com/maikebing/public-decision-data'; commit=$DatasetCommit; publish_branch='master'; raw_immutable=$true; output_prefix=('derived/'+$RunId+'/') }
    model = @{ url='https://moark.com/maikebing/Sezika-mmBERT-8192-experimental'; commit=$ModelCommit; publish_branch='master'; output_prefix=('runs/'+$RunId+'/'); initialization='zero_new_head'; preserve_base_encoder_and_history=$true }
    hardware = @{cuda='12.8';gpu='RTX 5090';gpu_memory_gib=32;cpu_cores=25;host_memory_gib=48;system_disk_gib=30;data_disk_gib=50;actual_gpu_uuid=$null;all_assets_on_data_disk=$true}
    limits = @{ maximum_seconds=$MaximumSeconds; maximum_records=$MaximumRecords; maximum_sources=18; maximum_external_calls=64; gpu_concurrency=1; transfer_reserve_seconds=$TransferReserveSeconds; cancel_file='CANCEL' }
    method_gates = @{ frozen_encoder_gpu_forward='historical_pass_new_instance_NOT_RUN'; head_gpu_backward='tiny_synthetic_pass_real_base_NOT_RUN'; repository_data_adapter='single_source_engineered_task_contract_NOT_RUN'; encoder_backward='tiny_synthetic_gradient_pass_real_mmBERT_NOT_RUN'; checkpoint='tiny_bitwise_resume_pass_real_size_NOT_RUN'; real_base_profile='NOT_RUN' }
    rental_gates = @{ local_checks='NOT_RUN'; data_contract='NOT_RUN'; strict_lengths='NOT_RUN'; publish_preview='NOT_RUN'; server='WAIT_new_instance'; deadline='WAIT_new_lease'; tiny_gpu='NOT_RUN'; training='NOT_RUN'; independent_validation='NOT_RUN'; model_download='NOT_RUN'; dataset_push='NOT_RUN_or_no_change'; model_push='NOT_RUN'; public_readback='NOT_RUN' }
    authorization = @{ model_and_changed_dataset_push='authorized_by_current_request'; purchase_instance=$false; old_server=$false; source_push=$false }
}
[void][IO.Directory]::CreateDirectory($task)
$encoding = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $task 'task.json'), ($plan | ConvertTo-Json -Depth 8), $encoding)
$server = [ordered]@{ host_name=$null; user=$null; port=$null; identity_file=$null; known_hosts=(Join-Path $task 'known_hosts'); host_key_fingerprint=$null; authentication='supply_for_new_instance_in_memory_or_secure_store' }
[IO.File]::WriteAllText((Join-Path $task 'connection.template.json'), ($server | ConvertTo-Json), $encoding)
$deadline = [ordered]@{ rental_created_utc=$null; rental_end_utc=$null; deadline_utc=$null; download_and_push_reserve_seconds=$TransferReserveSeconds; new_gpu_launch_cutoff_utc=$null; user_timezone='Asia/Shanghai' }
[IO.File]::WriteAllText((Join-Path $task 'deadline.template.json'), ($deadline | ConvertTo-Json), $encoding)
$training = [ordered]@{
    RunId=$RunId;DataRoot=$null;DataManifestSha256=$null;BaseModelRoot=$null;SourceCommit=$head;DatasetCommit=$DatasetCommit;ModelCommit=$ModelCommit
    Seed=17;MaximumUpdates=17615;MaximumTokens=8192;CheckpointEvery=128;ValidationPerSplit=64
    EncoderRate=0.00001;HeadRate=0.001;WeightDecay=0.01;MaximumOwnedBytes=25769803776;ExpectedGpuUuid=$null;ExperimentalOnly=$true
}
[IO.File]::WriteAllText((Join-Path $task 'run-plan.template.json'),($training|ConvertTo-Json -Depth 4),$encoding)
Check
[pscustomobject]@{ task_directory=$task; plan=(Join-Path $task 'task.json'); method=$Method; status=$plan.status; old_server_contacted=$false; elapsed_seconds=$watch.Elapsed.TotalSeconds }
