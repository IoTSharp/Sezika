#requires -Version 7.0
function Test-RepairPayload([string]$Json,[object[]]$SelectedRows){
 $clock=[Diagnostics.Stopwatch]::StartNew();$doc=$null
 function Check {if($clock.Elapsed.TotalSeconds -gt 30 -or (Test-Path "$PSScriptRoot/cancel.request")){throw 'Repair validator30s/cancel'}}
 function Fields($Node,[string[]]$Names){Check;if($Node.ValueKind -ne 'Object'){throw 'Expected object'};$map=@{};foreach($p in $Node.EnumerateObject()){Check;if($map.ContainsKey($p.Name) -or $p.Name -cnotin $Names){throw 'Duplicate/unknown field'};$map[$p.Name]=$p.Value};if($map.Count -ne $Names.Count){throw 'Missing fields'};return $map}
 function Text($Node,[int]$Max){Check;if($Node.ValueKind -ne 'String'){throw 'Expected string'};$s=$Node.GetString();if([string]::IsNullOrWhiteSpace($s) -or $s.Length -gt $Max){throw 'String bounds'};return $s}
 try{
  if($SelectedRows.Count -lt 1 -or $SelectedRows.Count -gt 6 -or [Text.Encoding]::UTF8.GetByteCount($Json) -gt 1MB){throw 'Repair payload bounds'}
  $doc=[Text.Json.JsonDocument]::Parse($Json);$top=Fields $doc.RootElement @('decisions');$values=$top.decisions
  if($values.ValueKind -ne 'Array' -or $values.GetArrayLength() -ne $SelectedRows.Count){throw 'Repair denominator'}
  $sources=@{};foreach($row in $SelectedRows){$sources[$row.record_id]=$row};$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal);$out=[Collections.Generic.List[object]]::new()
  foreach($value in $values.EnumerateArray()){
   Check;$f=Fields $value @('record_id','decision','reason','instruction','statement','candidates','suggested_correct_id','suggested_correct_index','evidence_quotes')
   $id=Text $f.record_id 128;if(-not $sources.ContainsKey($id) -or -not $seen.Add($id)){throw 'Unknown/duplicate ID'};$source=$sources[$id]
   $decision=Text $f.decision 10;if($decision -cnotin @('repair','delete')){throw 'Decision enum'};$reason=Text $f.reason 2000
   if($f.candidates.ValueKind -ne 'Array' -or $f.candidates.GetArrayLength() -gt 8 -or $f.evidence_quotes.ValueKind -ne 'Array' -or $f.evidence_quotes.GetArrayLength() -gt 4){throw 'Array bounds'}
   $quotes=@(foreach($q in $f.evidence_quotes.EnumerateArray()){Check;$quote=Text $q 4000;if(-not $source.request.state.text.Contains($quote,[StringComparison]::Ordinal)){throw 'Quote not verbatim original state'};$quote})
   $revised=$null
   if($decision -ceq 'delete'){
    foreach($key in @('instruction','statement','suggested_correct_id','suggested_correct_index')){if($f[$key].ValueKind -ne 'Null'){throw 'Deleted row must not suggest labels/text'}}
    if($f.candidates.GetArrayLength() -ne 0){throw 'Deleted candidates must be empty'}
   }else{
    if($quotes.Count -lt 1){throw 'Repair requires original evidence quote'}
    $revised=$source|ConvertTo-Json -Depth 45 -Compress|ConvertFrom-Json;$q=$revised.request.questions[0];$q.instruction=Text $f.instruction 1400
    $ids=[Collections.Generic.List[string]]::new()
    if($q.type -ceq 'choice'){
     if($f.statement.ValueKind -ne 'Null' -or $f.candidates.GetArrayLength() -lt 2){throw 'Choice fields'}
     $candidates=@(foreach($c in $f.candidates.EnumerateArray()){Check;$cf=Fields $c @('id','text');$cid=Text $cf.id 64;if($cid -cnotmatch '^[a-z][a-z0-9_]*$' -or $ids.Contains($cid)){throw 'Candidate identity'};$ids.Add($cid);[pscustomobject]@{id=$cid;text=(Text $cf.text 300)}})
     if(@($candidates.text|Sort-Object -Unique).Count -ne $candidates.Count){throw 'Duplicate candidate text'};$q.candidates=$candidates
    }elseif($q.type -ceq 'boolean'){
     if($f.candidates.GetArrayLength() -ne 0){throw 'Boolean candidates'};$q.statement=Text $f.statement 800;$ids.Add('false');$ids.Add('true')
    }else{throw 'Unsupported repair primitive'}
    $index=0;if($f.suggested_correct_index.ValueKind -ne 'Number' -or -not $f.suggested_correct_index.TryGetInt32([ref]$index) -or $index -lt 0 -or $index -ge $ids.Count){throw 'Target integer range'}
    if((Text $f.suggested_correct_id 64) -cne $ids[$index]){throw 'Target ID/index mismatch'};$revised.target_index=$index
    if(($source.request|ConvertTo-Json -Depth 35 -Compress) -ceq ($revised.request|ConvertTo-Json -Depth 35 -Compress)){throw 'Repair did not change question'}
    $revised.ai_review_status='repair_proposed_pending_blind_reaudit'
   }
   $out.Add(@{record_id=$id;decision=$decision;reason=$reason;evidence_quotes=$quotes;original_source_gold=$source.target_index;revised_row=$revised;human_review='pending';formal=$false})
  }
  return @($out)
 }finally{if($doc){$doc.Dispose()}}
}
