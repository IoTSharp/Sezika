param([string]$OutputRoot='C:\Users\mysti\Documents\SezikaData\public-decision-2026-10-10',[string]$ResearchRoot=$PSScriptRoot,[ValidateRange(5,600)][int]$MaxSeconds=120)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7){throw 'PowerShell 7 required'}
$watch=[Diagnostics.Stopwatch]::StartNew()
$definition=@'
using System;
using System.IO;
using System.Diagnostics;
using System.Buffers.Binary;
public sealed class PublicParquetFooter {
 readonly byte[] data; int position; int operations; readonly Stopwatch watch = Stopwatch.StartNew();
 PublicParquetFooter(byte[] bytes) { data=bytes; }
 void Check(int depth=0) { if(depth>64 || ++operations>data.Length*4L+1 || watch.Elapsed.TotalSeconds>2) throw new InvalidDataException("Footer parsing bound"); }
 byte Byte() {Check(); if(position>=data.Length) throw new EndOfStreamException(); return data[position++];}
 ulong Varint() {ulong value=0;for(int i=0;i<10;i++){byte b=Byte(); if(i==9 && b>1)throw new InvalidDataException("Varint overflow");value|=(ulong)(b&127)<<(7*i);if((b&128)==0)return value;}throw new InvalidDataException("Varint bound");}
 void Advance(ulong n){Check();if(n>(ulong)(data.Length-position))throw new EndOfStreamException();position+=checked((int)n);}
 void Skip(int type,int depth,bool collection=false){
  Check(depth);
  switch(type){
   case 1:case 2:if(collection)Byte();return;
   case 3:Byte();return;
   case 4:case 5:case 6:Varint();return;
   case 7:Advance(8);return;
   case 8:Advance(Varint());return;
   case 9:case 10:{byte h=Byte();ulong count=(ulong)(h>>4);if(count==15)count=Varint();if(count>(ulong)data.Length)throw new InvalidDataException("List bound");for(ulong i=0;i<count;i++)Skip(h&15,depth+1,true);return;}
   case 11:{ulong count=Varint();if(count>(ulong)data.Length)throw new InvalidDataException("Map bound");if(count>0){byte h=Byte();for(ulong i=0;i<count;i++){Skip(h>>4,depth+1,true);Skip(h&15,depth+1,true);}}return;}
   case 12:for(int i=0;i<data.Length;i++){byte h=Byte();if(h==0)return;if((h>>4)==0)Varint();Skip(h&15,depth+1);}throw new InvalidDataException("Struct bound");
   default:throw new InvalidDataException("Unknown compact type");
  }
 }
 long Rows(){int field=0;for(int i=0;i<data.Length;i++){byte h=Byte();if(h==0)break;int delta=h>>4;if(delta==0){ulong x=Varint();field=checked((int)((long)(x>>1)^-((long)x&1)));}else field+=delta;if(field==3){if((h&15)!=6)throw new InvalidDataException("num_rows type");ulong x=Varint();long n=(long)(x>>1)^-((long)x&1);if(n<0)throw new InvalidDataException("Negative row count");return n;}Skip(h&15,1);}throw new InvalidDataException("num_rows missing");}
 public static long Read(string path){
  using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(stream.Length<12)throw new InvalidDataException("Parquet length");
  Span<byte> tail=stackalloc byte[8];stream.Position=stream.Length-8;stream.ReadExactly(tail);
  if(tail[4]!=80||tail[5]!=65||tail[6]!=82||tail[7]!=49)throw new InvalidDataException("Unsupported/encrypted Parquet footer");
  int length=BinaryPrimitives.ReadInt32LittleEndian(tail);
  if(length<1||length>64*1024*1024||length>stream.Length-12)throw new InvalidDataException("Footer metadata bound");
  byte[] bytes=new byte[length];stream.Position=stream.Length-8-length;stream.ReadExactly(bytes);
  return new PublicParquetFooter(bytes).Rows();
 }
}
'@
Add-Type -TypeDefinition $definition
$countCache=@{}
$previousInventory=Join-Path $ResearchRoot 'download-inventory.json'
if(Test-Path -LiteralPath $previousInventory){
 $previous=@(Get-Content -LiteralPath $previousInventory -Raw|ConvertFrom-Json)
 for($p=0;$p -lt $previous.Count;$p++){
  if($watch.Elapsed.TotalSeconds -ge $MaxSeconds){throw 'Count cache deadline'}
  if($null -ne $previous[$p].rows){$countCache[$previous[$p].path]=$previous[$p]}
 }
}
$known=@{};$failures=[Collections.Generic.List[object]]::new()
$ledgers=@(Get-ChildItem -LiteralPath $OutputRoot -File -Filter '*ledger*.jsonl')
for($l=0;$l -lt $ledgers.Count;$l++){
 if($watch.Elapsed.TotalSeconds -ge $MaxSeconds){throw 'Inventory ledger deadline'}
 $lines=@(Get-Content -LiteralPath $ledgers[$l].FullName)
 for($i=0;$i -lt $lines.Count;$i++){
  if($watch.Elapsed.TotalSeconds -ge $MaxSeconds){throw 'Inventory deadline'}
  $row=$lines[$i]|ConvertFrom-Json
  if($row.status -eq 'downloaded'){$known[$row.path]=$row}else{$failures.Add($row)}
 }
}
$inventory=[Collections.Generic.List[object]]::new()
$values=@($known.Values|Sort-Object dataset,path)
for($i=0;$i -lt $values.Count;$i++){
 if($watch.Elapsed.TotalSeconds -ge $MaxSeconds -or(Test-Path -LiteralPath (Join-Path $OutputRoot 'CANCEL'))){break}
 $row=$values[$i];$count=$null;$countSource=$null;$countError=$null
 $cached=$countCache[$row.path]
 if($cached -and $cached.sha256 -ceq $row.sha256 -and $cached.bytes -eq $row.bytes){
  $count=$cached.rows;$countSource=$cached.row_count_source
 }elseif($row.path -like '*.parquet'){
  try{$count=[PublicParquetFooter]::Read($row.path);$countSource='Parquet FileMetaData.num_rows (footer only)'}catch{$countError=$_.Exception.Message}
 }elseif($row.path -like '*.jsonl'){
  $reader=$null
  try{
   $reader=[IO.StreamReader]::new($row.path);$bytes=(Get-Item -LiteralPath $row.path).Length;$n=0L
   for($line=0L;$line -le $bytes;$line++){
    if($watch.Elapsed.TotalSeconds -ge $MaxSeconds){throw 'JSONL count deadline'}
    $text=$reader.ReadLine();if($null -eq $text){break};if(-not[string]::IsNullOrWhiteSpace($text)){$n++}
   }
   $count=$n;$countSource='nonblank JSONL physical lines; schema validity and duplicate questions not inferred'
  }catch{$countError=$_.Exception.Message}finally{if($reader){$reader.Dispose()}}
 }
 $inventory.Add([pscustomobject]@{dataset=$row.dataset;path=$row.path;kind=$row.kind;split_hint=$row.split;source_revision=$row.source_revision;source_url=$row.url;bytes=$row.bytes;sha256=$row.sha256;expected_sha256=$row.expected_sha256;expected_git_blob_sha1=$row.expected_git_blob_sha1;verified_git_blob_sha1=$row.git_blob_sha1;acquisition_transport=$row.transport;mirror_url=$row.mirror_url;rows=$count;row_count_source=$countSource;row_count_error=$countError;label_quality='not_assessed';training_admission='pending';redistribution='source_specific_review'})
 if(($i % 250) -eq 0){Write-Output ('Inventory '+($i+1)+'/'+$values.Count)}
}
$inventory|ConvertTo-Json -Depth 9|Set-Content -LiteralPath (Join-Path $ResearchRoot 'download-inventory.json') -Encoding utf8
$inventory|Export-Csv -LiteralPath (Join-Path $ResearchRoot 'download-inventory.csv') -NoTypeInformation -Encoding utf8
$summary=[pscustomobject]@{snapshot_utc=[DateTime]::UtcNow.ToString('o');output_root=$OutputRoot;downloaded_files=$inventory.Count;downloaded_bytes=($inventory|Measure-Object bytes -Sum).Sum;physical_rows=($inventory|Measure-Object rows -Sum).Sum;row_counted_files=@($inventory|Where-Object {$null -ne $_.rows}).Count;downloaded_dataset_repos=@($inventory|Where-Object kind -eq hf-source-file|Select-Object -ExpandProperty dataset -Unique).Count;failure_or_pending_ledger_events=$failures.Count;inventory_snapshot_complete=($i -eq $values.Count);selected_collection_complete='See source-completeness.json';rows_are_unique_questions=$false;formal_training_admitted=$false;moark_published=$false;elapsed_seconds=$watch.Elapsed.TotalSeconds}
$summary|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $ResearchRoot 'collection-summary.json') -Encoding utf8
$failures|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $ResearchRoot 'failed-and-pending-attempts.json') -Encoding utf8
$summary|ConvertTo-Json -Compress
