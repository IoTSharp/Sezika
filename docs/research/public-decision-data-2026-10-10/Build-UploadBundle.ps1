param(
 [string]$OutputRoot='C:\Users\mysti\Documents\SezikaData\public-decision-2026-10-10',
 [string]$ResearchRoot=$PSScriptRoot,
 [ValidateRange(10,600)][int]$MaxSeconds=300,
 [switch]$VerifyHashes,
 [switch]$Extended,
 [ValidatePattern('^moark-public-decision(?:-v[0-9]+)?$')][string]$BundleName='moark-public-decision'
)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required'}
$OutputRoot=[IO.Path]::GetFullPath($OutputRoot)
$bundle=Join-Path (Join-Path $OutputRoot 'upload') $BundleName
$watch=[Diagnostics.Stopwatch]::StartNew()
$cancel=Join-Path $OutputRoot 'CANCEL'
[void][IO.Directory]::CreateDirectory($bundle)
$rules=@(
 @{id='PolyAI/banking77';license='CC-BY-4.0';evidence='raw/hf/PolyAI/banking77/README.md';reason='Original dataset license declaration and preserved attribution.'},
 @{id='clinc/clinc_oos';license='CC-BY-3.0';evidence='raw/hf/clinc/clinc_oos/README.md';reason='Original dataset license declaration; original splits retained.'},
 @{id='AmazonScience/massive';license='CC-BY-4.0';evidence='raw/hf/AmazonScience/massive/README.md';reason='Original multilingual intent dataset; release and attribution retained.'},
 @{id='nvidia/HelpSteer';license='CC-BY-4.0';evidence='raw/hf/nvidia/HelpSteer/README.md';reason='Publisher declares the dataset CC BY 4.0.'},
 @{id='nvidia/HelpSteer2';license='CC-BY-4.0';evidence='raw/hf/nvidia/HelpSteer2/README.md';reason='Publisher declares the dataset CC BY 4.0; original score dimensions retained.'},
 @{id='google/civil_comments';license='CC0-1.0';evidence='raw/hf/google/civil_comments/README.md';reason='Card explicitly states both released dataset and underlying comment text CC0.'},
 @{id='LocalLLaMA/typed-decisions';license='Apache-2.0';evidence='raw/hf/LocalLLaMA/typed-decisions/README.md';reason='Publisher synthetic workflow benchmark; original card and train/test configurations retained.'},
 @{id='kaivoss/system-one-270m-data';license='Apache-2.0';evidence='raw/hf/kaivoss/system-one-270m-data/README.md';reason='Publisher declares synthetic data Apache 2.0; retain teacher provenance. Soft targets are teacher votes, not measured correctness probabilities.'},
 @{id='dwidlee/systemone-lite-phase2';license='Apache-2.0';evidence='raw/hf/dwidlee/systemone-lite-phase2/README.md';reason='Publisher declares generated typed decision data Apache 2.0; retain declared train/test and state-task isolation.'},
 @{id='samatv256/jev-decisions-v1';license='CC-BY-4.0 with retained component terms';evidence='raw/hf/samatv256/jev-decisions-v1/SOURCE_LICENSES.md';reason='Initial bundle includes only public CLEAN-50K plus source documentation. CLEAN-50K excludes Open-SWE; retain NVIDIA attribution and all component declarations. Full mixed corpus is held for row-level Open-SWE license review.';include_regex='^(clean50k/|README\.md$|SOURCE_LICENSES\.md$|LICENSE[^/]*$|NOTICE[^/]*$)'}
)
if($Extended){
 $controls='(?:citation|entity-alignment|amount-extraction|email-selection|phone-extraction|context-retention|sponsor-segment|silent-failure|ir|mailroom)-control-v1'
 $rules+=@(
  @{id='google/boolq';license='CC-BY-SA-3.0';evidence='raw/hf/google/boolq/README.md';reason='Dataset card explicitly states CC BY SA 3.0. Exact bytes, original attribution and share-alike terms retained; original validation remains evaluation.'},
  @{id='stanfordnlp/snli';license='CC-BY-SA-4.0';evidence='raw/hf/stanfordnlp/snli/README.md';reason='Card identifies SNLI and Flickr30k premises under attribution/share-alike; unchanged bytes and attribution retained; original split boundaries preserved.'},
  @{id='google-research-datasets/go_emotions';license='Apache-2.0';evidence='raw/hf/google-research-datasets/go_emotions/README.md';reason='Original Google dataset card explicitly declares Apache 2.0; retain annotation metadata and full original configurations.'},
  @{id='Anthropic/hh-rlhf';license='MIT';evidence='raw/hf/Anthropic/hh-rlhf/README.md';reason='Publisher dataset card declares MIT. Preference pairs and red-team records retained as distinct supervision; original research-use warnings preserved.'},
  @{id='Team-ACE/ToolACE';license='Apache-2.0';evidence='raw/hf/Team-ACE/ToolACE/README.md';reason='Publisher synthetic tool-learning data card declares Apache 2.0; exact dialogs, API definitions and paper attribution retained.'},
  @{id='gorilla-llm/Berkeley-Function-Calling-Leaderboard';license='Apache-2.0';evidence='raw/hf/gorilla-llm/Berkeley-Function-Calling-Leaderboard/README.md';reason='Publisher card explicitly releases data under Apache 2.0; all question and answer files retained for independent evaluation, not training.'},
  @{id='Muennighoff/xwinograd';license='CC-BY-4.0';evidence='raw/hf/Muennighoff/xwinograd/README.md';reason='Publisher explicitly licenses multilingual Winograd data under CC BY 4.0, referring to the original English source; retain all language files and attribution for evaluation isolation.'},
  @{id='ZefanCai/Open-Jev';license='CC0-1.0 for selected original generated controls';evidence='raw/hf/ZefanCai/Open-Jev/README.md';reason='Only ten stand-alone original generated control configs, their raw/Parquet representations, cards and exact provenance are included. Composite mixtures and customer-control/TypeSafe descriptions excluded; README and THIRD_PARTY_NOTICES retain the release boundary.';include_regex=('^(?:data/'+$controls+'/|raw/'+$controls+'/|provenance/original-manifests/'+$controls+'\.json$|cards/'+$controls+'\.md$|README\.md$|THIRD_PARTY_NOTICES\.md$|REPRODUCTION\.md$|LICENSE[^/]*$)')}
 )
}
$approved=@{}
for($r=0;$r -lt $rules.Count;$r++){
 if($watch.Elapsed.TotalSeconds -ge $MaxSeconds){throw 'Rights setup deadline'}
 if(-not(Test-Path -LiteralPath (Join-Path $OutputRoot $rules[$r].evidence))){throw ('Rights evidence missing: '+$rules[$r].id)}
 $approved[$rules[$r].id]=$rules[$r]
}
$known=@{}
$ledgers=@(Get-ChildItem -LiteralPath $OutputRoot -File -Filter '*ledger*.jsonl')
for($l=0;$l -lt $ledgers.Count;$l++){
 $lines=@(Get-Content -LiteralPath $ledgers[$l].FullName)
 for($i=0;$i -lt $lines.Count;$i++){
  if($watch.Elapsed.TotalSeconds -ge $MaxSeconds -or(Test-Path -LiteralPath $cancel)){throw 'Bundle scan deadline/cancellation'}
  $row=$lines[$i]|ConvertFrom-Json
  if($row.status -eq 'downloaded' -and $approved.ContainsKey([string]$row.dataset)){
   $rule=$approved[[string]$row.dataset]
   $sourceRelative=[IO.Path]::GetRelativePath($OutputRoot,$row.path).Replace('\','/')
   $prefix='raw/hf/'+$row.dataset+'/'
   if($rule.include_regex){
    if(-not $sourceRelative.StartsWith($prefix,[StringComparison]::Ordinal)){continue}
    $withinSource=$sourceRelative.Substring($prefix.Length)
    if($withinSource -notmatch $rule.include_regex){continue}
   }
   $known[$row.path]=$row
  }
 }
}
$linkCode=@'
using System;
using System.Runtime.InteropServices;
public static class DecisionBundleLinks {
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
 static extern bool CreateHardLink(string newPath,string existingPath,IntPtr attributes);
 public static void Create(string target,string source) {
  if(!CreateHardLink(target,source,IntPtr.Zero))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
 }
}
'@
Add-Type -TypeDefinition $linkCode
$manifest=[Collections.Generic.List[object]]::new()
$rows=@($known.Values|Sort-Object dataset,path)
for($i=0;$i -lt $rows.Count;$i++){
 if($watch.Elapsed.TotalSeconds -ge $MaxSeconds -or(Test-Path -LiteralPath $cancel)){throw 'Bundle build deadline/cancellation'}
 $row=$rows[$i]
 $source=[IO.Path]::GetFullPath($row.path)
 if(-not $source.StartsWith($OutputRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Source outside task root'}
 if((Get-Item -LiteralPath $source).Length -ne $row.bytes){throw ('Source length changed '+$source)}
 if($VerifyHashes -and (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -cne $row.sha256){throw ('Source SHA changed '+$source)}
 $relative=[IO.Path]::GetRelativePath($OutputRoot,$source)
 $target=[IO.Path]::GetFullPath((Join-Path $bundle $relative))
 if(-not $target.StartsWith($bundle+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Bundle destination outside root'}
 [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
 if(Test-Path -LiteralPath $target){
  if((Get-Item -LiteralPath $target).Length -ne $row.bytes){throw 'Existing bundle length mismatch'}
 }else{[DecisionBundleLinks]::Create($target,$source)}
 $manifest.Add([pscustomobject]@{
  dataset=$row.dataset;path=$relative.Replace('\','/');source_url=$row.url;source_revision=$row.source_revision;
  bytes=$row.bytes;sha256=$row.sha256;upstream_sha256=$row.expected_sha256;upstream_git_blob_sha1=$row.expected_git_blob_sha1;verified_git_blob_sha1=$row.git_blob_sha1;split_hint=$row.split;
  declared_license=$approved[$row.dataset].license;license_evidence=$approved[$row.dataset].evidence;
  changes='None: exact downloaded bytes. Local staging uses NTFS hard links; treat staged data as immutable.';
  training_admission='Pending schema, source split, family overlap and label review.'
 })
 if(($i % 50) -eq 0){Write-Output ('Bundle '+($i+1)+'/'+$rows.Count)}
}
$sourceCatalog=@(Get-Content -LiteralPath (Join-Path $ResearchRoot 'source-catalog.json') -Raw|ConvertFrom-Json)
$rights=[Collections.Generic.List[object]]::new()
for($i=0;$i -lt $sourceCatalog.Count;$i++){
 if($watch.Elapsed.TotalSeconds -ge $MaxSeconds){throw 'Rights manifest deadline'}
 $s=$sourceCatalog[$i]
 $rule=$approved[[string]$s.id]
 $rights.Add([pscustomobject]@{dataset=$s.id;revision=$s.revision;source_url=$s.source_url;card_license=$s.license;decision=if($rule){'eligible_under_declared_terms_with_attribution'}else{'not_in_initial_bundle_pending_source_or_component_review'};reason=if($rule){$rule.reason}else{'Unknown, restricted, mixed source or component terms require individual review; no blanket relicensing.'};evidence=if($rule){$rule.evidence}else{$s.metadata_path};download_complete='See source-completeness.json';training_admitted=$false})
}
$manifest|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $bundle 'ASSET_MANIFEST.json') -Encoding utf8
$rights|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $bundle 'RIGHTS_MANIFEST.json') -Encoding utf8
$rights|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $ResearchRoot 'redistribution-review.json') -Encoding utf8
@'
# Sezika public decision data collection

This collection stages exact public source files for reproducible download.
Files retain their original source-specific licenses; this collection does not
replace those licenses. See RIGHTS_MANIFEST.json, ASSET_MANIFEST.json and each
source README/NOTICE/LICENSE for attribution, terms, source URL and revision.

Original training, validation and test partitions are retained. No merged
training-ready dataset or deduplicated sample count is claimed. Synthetic,
human-rating, intent and probability labels retain different semantics.

Do not edit hard-linked local files. Upload exact bytes and validate downloaded
SHA256 against ASSET_MANIFEST.json. This staging directory is not publication
proof. MOARK_PUBLICATION.json must contain verified repository identity, commit
and round-trip download evidence before marking publication successful.
'@|Set-Content -LiteralPath (Join-Path $bundle 'README.md') -Encoding utf8
@'
Collection metadata and directory structure do not relicense the contained data.
Each asset remains under the source-specific license identified in its source
card and RIGHTS_MANIFEST.json. Preserve all source attributions and notices.
'@|Set-Content -LiteralPath (Join-Path $bundle 'LICENSE') -Encoding utf8
@'
*.parquet filter=lfs diff=lfs merge=lfs -text
*.jsonl filter=lfs diff=lfs merge=lfs -text
*.csv filter=lfs diff=lfs merge=lfs -text
*.tsv filter=lfs diff=lfs merge=lfs -text
*.gz filter=lfs diff=lfs merge=lfs -text
*.zip filter=lfs diff=lfs merge=lfs -text
*.tar filter=lfs diff=lfs merge=lfs -text
*.xz filter=lfs diff=lfs merge=lfs -text
*.zst filter=lfs diff=lfs merge=lfs -text
*.arrow filter=lfs diff=lfs merge=lfs -text
'@|Set-Content -LiteralPath (Join-Path $bundle '.gitattributes') -Encoding utf8
if($Extended){
 'raw/**/*.json filter=lfs diff=lfs merge=lfs -text'|Add-Content -LiteralPath (Join-Path $bundle '.gitattributes') -Encoding utf8
 @'

The Open-Jev selection contains only the ten stand-alone original control
configs. Composite mixtures and customer-control text with unverified upstream
terms are excluded. Raw and Parquet representations overlap; do not count them
as independent examples. BFCL is reserved for independent evaluation. BoolQ
and SNLI retain their attribution and share-alike source terms. See each
selected source card and manifest before constructing training partitions.
'@|Add-Content -LiteralPath (Join-Path $bundle 'README.md') -Encoding utf8
}
$publication=Join-Path $bundle 'MOARK_PUBLICATION.json'
if(-not(Test-Path -LiteralPath $publication)){
 [pscustomobject]@{platform='Moark';namespace='maikebing';published=$false;repository_url=$null;commit=$null;authentication='Saved Gitee Git credential validated for maikebing; web dataset creation requires a logged-in session';round_trip_verified=$false}|ConvertTo-Json|Set-Content -LiteralPath $publication -Encoding utf8
}
$summary=[pscustomobject]@{snapshot_utc=[DateTime]::UtcNow.ToString('o');bundle_root=$bundle;datasets=@($manifest|Select-Object -ExpandProperty dataset -Unique).Count;files=$manifest.Count;bytes=($manifest|Measure-Object bytes -Sum).Sum;local_storage='NTFS hard links, immutable exact bytes';hashes_rechecked=[bool]$VerifyHashes;moark_published=$false;scope=if($Extended){'Extended reviewed sources and ten original Open-Jev controls; other sources retained locally pending review'}else{'Initial eligible sources; other sources retained locally with review status'};elapsed_seconds=$watch.Elapsed.TotalSeconds}
$summary|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $ResearchRoot 'upload-bundle-summary.json') -Encoding utf8
$summary|ConvertTo-Json -Compress
