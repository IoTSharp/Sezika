"""Independent original-row/rotation audit for a standalone phase2 recovery."""
import argparse, collections, gzip, hashlib, json, pathlib, time, unicodedata
from bounded_start import wait_for_guardian
from prepare_corpus import Budget, compact, sha, text, source_rows


def main():
    wait_for_guardian();ap=argparse.ArgumentParser();ap.add_argument('--dataset',required=True);ap.add_argument('--decoder',required=True);ap.add_argument('--task-root',required=True);ap.add_argument('--output',required=True);a=ap.parse_args()
    task=pathlib.Path(a.task_root).resolve();root=pathlib.Path(a.dataset).resolve();out=pathlib.Path(a.output).resolve();out.mkdir(exist_ok=False)
    budget=Budget(argparse.Namespace(seconds=120,maximum_records=100000,cancel=str(out/'cancel.request')))
    source='dwidlee/systemone-lite-phase2';recovered=task/'phase2-recovered-v1';manifest=json.loads((recovered/'manifest.json').read_text(encoding='utf-8'))
    assert manifest['status']=='COMPLETE_PREPARATION_ONLY' and not manifest['pilot'] and len(manifest['shards'])<=16
    entry=next(x for x in manifest['source_coverage'] if x['source']==source);assert len(entry['files'])==1
    carrier=entry['files'][0];source_path=(root/carrier['path']).resolve();assert source_path.is_relative_to(root)
    recovery_rows=set();ledger=task/'full-reviewed-v1'/'stage-01'/'quarantine.jsonl'
    with ledger.open(encoding='utf-8') as f:
        for index,line in enumerate(f):
            budget.check();assert index<2000000
            item=json.loads(line)
            if item['source']==source and item['reason']=='phase2_non_choice_or_label' and item['path']==carrier['path']:recovery_rows.add(item['row'])
    excluded=set()
    with (recovered/'exclusions.jsonl').open(encoding='utf-8') as f:
        for index,line in enumerate(f):
            budget.check();assert index<100000;excluded.add(json.loads(line)['record_id'])
    reasons=collections.Counter()
    with (recovered/'quarantine.jsonl').open(encoding='utf-8') as f:
        for index,line in enumerate(f):
            budget.check();assert index<100000;item=json.loads(line);assert item['row'] in recovery_rows;reasons[item['reason']]+=1
    rows={};kinds=collections.Counter();families={};ids=set()
    for shard in manifest['shards']:
        budget.check();path=(recovered/shard['path']).resolve();assert path.is_relative_to(recovered) and path.stat().st_size==shard['bytes'] and path.stat().st_size<64*1024*1024
        assert hashlib.sha256(path.read_bytes()).hexdigest()==shard['sha256']
        with gzip.open(path,'rt',encoding='utf-8') as f:
            for index,line in enumerate(f):
                budget.check();assert index<100000;row=json.loads(line);number=row['SourceRow'];assert number in recovery_rows and number not in rows
                assert row['RecordId'] not in ids and row['Source']['OriginalSplit']=='train' and row['Source']['FileSha256']==carrier['sha256'] and row['Type']=='Choice'
                assert row['LabelKind'] in ('source_engine_reference','synthetic_reference') and row['HumanReview']=='pending'
                assert row['Family'] not in families or families[row['Family']]==row['Split'];families[row['Family']]=row['Split']
                rows[number]=row;ids.add(row['RecordId']);kinds[row['LabelKind']]+=1
    assert excluded.issubset(ids) and len(rows)==manifest['records_written']==7898
    digest=hashlib.sha256()
    with source_path.open('rb') as f:
        for index in range(512):
            budget.check();block=f.read(1024*1024)
            if not block:break
            digest.update(block)
        else:raise ValueError('Original carrier byte bound')
    assert digest.hexdigest()==carrier['sha256']
    logs=out/'decoder-logs';logs.mkdir();decoded=0;audited=0;upstream=collections.Counter()
    for number,original in source_rows(source_path,a.decoder,budget,300000,logs):
        decoded+=1
        if decoded%50000==0:print('phase2 independent source audit decoded=',decoded,'mapped=',audited,flush=True)
        if number in recovery_rows:
            meta=json.loads(original['meta']);upstream[('task',original['task'])]+=1;upstream[('label_source',str(meta.get('label_source')))]+=1
            upstream[('schema',str(meta.get('schema') or meta.get('task_schema')))]+=1
        if number not in rows:continue
        row=rows[number];n=len(original['criteria_keys']);alias=original['label_alias'];assert alias in original['criteria_keys'] and 2<=n<=32
        candidates=[{'Id':str(i),'Text':text(t)} for i,t in enumerate(original['criteria_values'])]
        state=unicodedata.normalize('NFC',text(original['state'])).replace('\r\n','\n')
        content=sha(compact([state,original['instructions'],'Choice',candidates]));offset=int(content[:8],16)%n
        target=[float(i==original['criteria_keys'].index(alias)) for i in range(n)]
        assert row['RecordId']=='corpus-'+content and row['Candidates']==candidates[offset:]+candidates[:offset] and row['Target']==target[offset:]+target[:offset]
        assert row['SourceLineSha256']==sha(compact(original)) and row['State']==text(original['state']) and row['Instruction']==original['instructions']
        assert row['Family']==sha(source+'|'+sha(original['state']))
        meta=json.loads(original['meta']);kind='source_engine_reference' if meta.get('label_source')=='stockfish' else 'synthetic_reference'
        assert row['LabelKind']==kind and row['LabelSemantics']=='upstream_reference_choice; stockfish_reference_when_declared; not_teacher_accuracy'
        audited+=1
    assert decoded==carrier['rows_read']==240800 and audited==len(rows)
    report={'status':'PASS_independent_original_row_and_candidate_rotation_audit','source':source,'source_carrier_sha256':carrier['sha256'],'source_rows_scanned':decoded,
            'historical_quarantine_rows_selected':len(recovery_rows),'written_records':len(rows),'mandatory_conflict_exclusions':len(excluded),'effective_records_after_exclusions':len(rows)-len(excluded),
            'duplicates':manifest['counts'].get('duplicates',0),'conflict_events':manifest['counts'].get('conflicts',0),'new_quarantine_reasons':dict(reasons),'label_kinds':dict(kinds),
            'upstream_recovery_rows':{category:{name:value for (c,name),value in upstream.items() if c==category} for category in ('task','label_source','schema')},
            'source_rows_directly_verified':audited,'candidate_rotation_labels_verified':True,'original_split':'train','exact_family_split_verified':True,
            'semantics':'Declared Stockfish engine references or upstream synthetic references, not human gold, teacher quality, or calibrated probabilities',
            'formal_training_admitted':False,'measured_student_accuracy':None,'elapsed_seconds':time.monotonic()-budget.start}
    with (out/'receipt.json').open('x',encoding='utf-8') as f:json.dump(report,f,ensure_ascii=False,indent=2)
    print(compact(report),flush=True)


if __name__=='__main__':main()
