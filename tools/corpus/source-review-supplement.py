"""Read at most two original-train rows from each of 29 catalog-only sources.

No source loader is imported or run. Raw samples remain in private artifacts.
"""
import argparse, collections, csv, gzip, hashlib, json, lzma, pathlib, subprocess, tarfile, time
from bounded_start import wait_for_guardian


MAX_ROW_BYTES=4*1024*1024


class Bounds:
    def __init__(self,a):self.a=a;self.started=time.monotonic();self.source_started=self.started;self.samples=0
    def check(self):
        now=time.monotonic()
        if now-self.started>=self.a.seconds or (self.a.cancel and pathlib.Path(self.a.cancel).exists()):raise TimeoutError('global_deadline_or_cancel')
        if now-self.source_started>=20:raise TimeoutError('per_source_20_second_deadline')
    def remaining(self):return max(.1,min(20-(time.monotonic()-self.source_started),self.a.seconds-(time.monotonic()-self.started)))


def fields(value,depth=0):
    if depth>=4:return type(value).__name__
    if isinstance(value,dict):return {str(k):fields(v,depth+1) for k,v in list(value.items())[:64]}
    if isinstance(value,list):return {'type':'array','length':len(value),'first_element_shape':fields(value[0],depth+1) if value else None}
    return type(value).__name__


def first_json_rows(stream,bounds):
    """Incrementally parse a top-level array; small dict/columnar carriers bounded to4MiB."""
    decoder=json.JSONDecoder();buffer='';total=0;position=0;array=None;yielded=0
    for _ in range(1026):
        bounds.check()
        if array is None:
            stripped=buffer.lstrip()
            if stripped:
                array=stripped.startswith('[')
                if array:buffer=stripped[1:];position=0
        if array:
            produced=0
            for _ in range(2):
                # This branch only yields when a complete object exists in the current buffer.
                suffix=buffer[position:].lstrip();position=len(buffer)-len(suffix)
                if suffix.startswith(','):position+=1;suffix=buffer[position:].lstrip();position=len(buffer)-len(suffix)
                if suffix.startswith(']'):return
                try:row,end=decoder.raw_decode(buffer,position)
                except json.JSONDecodeError:break
                if not isinstance(row,dict):raise ValueError('json_array_element_not_object')
                yield row;position=end;produced+=1;yielded+=1
                if yielded>=2:return
            if produced:
                # Two-object total is controlled by the caller wrapper below, not read chunks.
                pass
        data=stream.read(4096)
        total+=len(data.encode('utf-8'))
        if total>MAX_ROW_BYTES:raise ValueError('json_prefix_4MiB_bound_columnar_or_large_row_requires_adapter')
        if not data:
            if array:return
            value=json.loads(buffer)
            if isinstance(value,dict) and isinstance(value.get('data'),list):
                for row in value['data'][:2]:
                    if not isinstance(row,dict):raise ValueError('json_data_element_not_object')
                    yield row
                return
            if isinstance(value,dict) and value and all(isinstance(v,list) for v in value.values()):
                length=min(len(v) for v in value.values())
                for index in range(min(2,length)):yield {k:v[index] for k,v in value.items()}
                return
            if isinstance(value,dict):yield value;return
            raise ValueError('json_carrier_not_record_array_or_columnar')
        buffer+=data
    raise ValueError('json_prefix_iteration_bound')


def rows(asset,decoder,bounds,logs):
    path=pathlib.Path(asset['path']);name=path.name.lower()
    if name.endswith('.parquet'):
        command=['C:/Program Files/dotnet/dotnet.exe',decoder,'export-json',str(path),'-','2',str(max(1,int(bounds.remaining()-1)))]
        error_file=logs/(hashlib.sha256(str(path).encode()).hexdigest()[:16]+'.stderr.log')
        with error_file.open('xb') as err:
            proc=subprocess.Popen(command,stdout=subprocess.PIPE,stderr=err)
            try:
                stdout,_=proc.communicate(timeout=max(.1,bounds.remaining()-4))
                if proc.returncode!=0:raise RuntimeError('parquet_decoder_nonzero_exit_'+str(proc.returncode))
                if len(stdout)>2*MAX_ROW_BYTES:raise ValueError('decoder_output_8MiB_bound')
                lines=stdout.decode('utf-8-sig').splitlines()
                if len(lines)>2:raise ValueError('decoder_row_bound')
                for line in lines:bounds.check();yield json.loads(line),None
            finally:
                if proc.poll() is None:
                    proc.terminate()
                    try:proc.wait(timeout=2)
                    except subprocess.TimeoutExpired:proc.kill();proc.wait(timeout=2)
                proc.stdout.close()
        return
    if name.endswith('.tar.gz'):
        with tarfile.open(path,'r:gz') as archive:
            for index,member in enumerate(archive):
                bounds.check()
                if index>=80:raise ValueError('tar_80_member_bound')
                if not member.isfile() or not member.name.endswith('.jsonl'):continue
                # Inspect only the first two rows in this actual member, no extraction or arbitrary scan.
                with archive.extractfile(member) as stream:
                    for row_index in range(2):
                        bounds.check();line=stream.readline(MAX_ROW_BYTES+1)
                        if not line:return
                        if len(line)>MAX_ROW_BYTES:raise ValueError('archive_row_4MiB_bound')
                        row=json.loads(line.decode('utf-8-sig'))
                        if row.get('partition')!='train':raise ValueError('first_archive_member_prefix_not_original_train')
                        yield row,{'member':member.name,'member_row':row_index+1,'partition':'train'}
                return
        raise ValueError('no_jsonl_member_in_bounded_archive')
    op=gzip.open if name.endswith('.gz') else lzma.open if name.endswith('.xz') else open
    if name.endswith(('.jsonl','.jsonl.gz','.jsonl.xz')):
        with op(path,'rb') as stream:
            for _ in range(2):
                bounds.check();line=stream.readline(MAX_ROW_BYTES+1)
                if not line:return
                if len(line)>MAX_ROW_BYTES:raise ValueError('jsonl_row_4MiB_bound')
                yield json.loads(line.decode('utf-8-sig')),None
        return
    if name.endswith('.csv'):
        with path.open(encoding='utf-8-sig',newline='') as stream:
            csv.field_size_limit(MAX_ROW_BYTES);reader=csv.DictReader(stream)
            for _ in range(2):
                bounds.check()
                try:row=next(reader)
                except StopIteration:return
                if len(json.dumps(row).encode())>MAX_ROW_BYTES:raise ValueError('csv_row_4MiB_bound')
                yield row,None
        return
    if name.endswith(('.json','.json.gz')):
        # Decode UTF8 incrementally so chunk boundaries never corrupt multibyte text.
        import codecs
        with op(path,'rb') as binary:
            reader=codecs.getreader('utf-8-sig')(binary)
            local=0;old_samples=bounds.samples;bounds.samples=0
            try:
                for row in first_json_rows(reader,bounds):
                    bounds.check();local+=1
                    if local>2:raise ValueError('json_source_row_bound')
                    yield row,None
                    if local==2:return
            finally:bounds.samples=old_samples
        return
    raise ValueError('unsupported_carrier_format')


def semantics(row):
    names=set(row)
    if {'state','questions','gold'}<=names:return 'typed_state_questions_gold;parse embedded schema and criteria-to-probability mapping; synthetic/human provenance remains source-specific'
    if {'question','choices','answerKey'}<=names:return 'finite_choices_answer_key;match choice label identities; benchmark origin and split remain reserved'
    if {'question','options','target'}<=names:return 'finite_options_target;verify target ordering/dimension and original task type'
    if {'prompt','response','is_safe'}<=names:return 'response_safety_boolean_and_optional_multilabel_category;not free text generation supervision'
    if {'human_ref_A','human_ref_B','labels'}<=names:return 'pair_preference_labels;verify whether id1 means A/B and preserve voting provenance'
    if {'premise','hypothesis','label'}<=names or {'context','hypothesis','label'}<=names:return 'three_relation_nli_label;class dictionary must fix integer relation mapping'
    if {'sentence1','sentence2','label'}<=names:return 'sentence_pair_binary_label;verify source class names before false/true mapping'
    if {'text','category'}<=names:return 'utterance_class_category;original class space may exceed32 and requires explicit training derivation'
    if {'utt','intent','scenario','partition'}<=names:return 'MASSIVE18scenario_and60intent_classes;cross-locale original id family;partition train verified in sampled rows'
    if 'messages' in names or 'conversations' in names:return 'dialogue_or_message_carrier;inspect reference answer for finite decision versus arguments/free text generation'
    if any(name.lower() in ('label','labels','score') for name in names):return 'labelled_record;check observed field type and original class dictionary/ordinal rubric before mapping'
    return 'answer_semantics_not_identified_by_bounded_field_pattern;explicit_source_adapter_review_required'


def main():
    wait_for_guardian()
    ap=argparse.ArgumentParser();ap.add_argument('--semantics',required=True);ap.add_argument('--inventory',required=True)
    ap.add_argument('--decoder',required=True);ap.add_argument('--output',required=True);ap.add_argument('--report')
    ap.add_argument('--maximum-sources',type=int,default=29);ap.add_argument('--seconds',type=int,default=600);ap.add_argument('--cancel')
    ap.add_argument('--source')
    a=ap.parse_args()
    if not 1<=a.maximum_sources<=29 or not 1<=a.seconds<=600:raise ValueError('Explicit supplement bounds required')
    bounds=Bounds(a);bounds.source_started=time.monotonic()
    report_input=pathlib.Path(a.semantics);inventory_input=pathlib.Path(a.inventory)
    if report_input.stat().st_size>4*1024*1024 or inventory_input.stat().st_size>8*1024*1024:raise ValueError('Input metadata byte bound')
    previous=json.loads(report_input.read_text(encoding='utf-8-sig'));inventory=json.loads(inventory_input.read_text(encoding='utf-8-sig'))
    if len(inventory)>5000 or len(previous['sources'])>75:raise ValueError('Metadata item bound')
    selected=sorted(x['source'] for x in previous['sources'] if x['schema_evidence_strength']=='catalog_task_only_explicit_schema_validation_pending')
    if len(selected)>29:raise ValueError('Catalog-only source bound')
    if a.source:
        if a.source not in selected:raise ValueError('Explicit source outside catalog-only selected list')
        selected=[a.source]
    selected=selected[:a.maximum_sources]
    grouped=collections.defaultdict(list)
    for asset in inventory:
        bounds.check()
        if asset.get('dataset'):grouped[asset['dataset']].append(asset)
    out=pathlib.Path(a.output).resolve();out.mkdir(parents=True,exist_ok=False);logs=out/'decoder-logs';logs.mkdir()
    (out/'PRIVATE_SCHEMA_SAMPLES').write_text('At most2 source rows per source, no model/training/teacher/publication.\n',encoding='utf-8')
    results=[];sample_total=0;hash_bytes_total=0
    extensions=('.parquet','.jsonl','.jsonl.gz','.jsonl.xz','.csv','.json','.json.gz')
    for index,source in enumerate(selected,1):
        bounds.source_started=time.monotonic();bounds.check();asset=None;samples=[]
        result={'source':source,'status':'not_inspected','rows_sampled':0,'full_source_decoded':False,'formal_training_admitted':False,'teacher_called':False,'new_training':False}
        try:
            candidates=[];train_named=[]
            for item in grouped[source]:
                bounds.check();name=pathlib.Path(item['path']).name.lower()
                if any(token in name for token in ('manifest','metadata','config','catalog','schema','statistics')):continue
                if item.get('split_hint')=='source-train' or name.startswith('train.') or name.startswith('train-') or name.startswith('train_') or name.startswith('sent_train.'):
                    train_named.append(item)
                    if name.endswith(extensions):candidates.append(item)
            if source=='AmazonScience/massive':candidates=[x for x in grouped[source] if x['path'].endswith('.tar.gz')]
            if not candidates:
                result.update(status='unsupported_original_train_carrier' if train_named else 'no_confirmed_original_train_data_carrier',reason='Train carrier format unsupported by bounded reader.' if train_named else 'Only metadata/evaluation/unsplit or missing training asset in frozen inventory; no path guessed and no loader run.')
                if train_named:result['source_asset']={k:train_named[0].get(k) for k in ('path','sha256','bytes','source_revision','split_hint')}
            else:
                asset=sorted(candidates,key=lambda x:x['path'])[0];path=pathlib.Path(asset['path']).resolve()
                result['source_asset']={k:asset.get(k) for k in ('path','sha256','bytes','source_revision','source_url','split_hint','rows')}
                if path.stat().st_size!=asset['bytes'] or asset['bytes']>512*1024*1024:raise ValueError('source_size_identity_or512MiB_bound')
                digest=hashlib.sha256()
                with path.open('rb') as file:
                    for _ in range((asset['bytes']+1048575)//1048576+1):
                        bounds.check();chunk=file.read(1048576)
                        if not chunk:break
                        hash_bytes_total+=len(chunk)
                        if hash_bytes_total>2*1024**3:raise ValueError('aggregate_source_hash2GiB_bound')
                        digest.update(chunk)
                if digest.hexdigest()!=asset['sha256']:raise ValueError('immutable_source_sha256_differs')
                for row,location in rows(asset,a.decoder,bounds,logs):
                    bounds.check()
                    if len(samples)>=2 or sample_total>=58:raise ValueError('source2_global58_sample_bound')
                    samples.append({'sample_number':len(samples)+1,'schema':fields(row),'answer_semantics':semantics(row),'original_location':location,'raw_row_private':row})
                    sample_total+=1
                result.update(status='sample_schema_decoded' if samples else 'empty_or_unreadable_prefix',rows_sampled=len(samples),samples=samples)
                result['source_sha256_verified']=True
        except (ValueError,KeyError,TypeError,OSError,RuntimeError,TimeoutError,subprocess.TimeoutExpired) as error:
            result.update(status='unsupported_train_selection_from_archive_prefix' if 'first_archive_member_prefix_not_original_train' in str(error) else 'FAILED_SAMPLE_PRESERVED',
                          failure=repr(error),reason='One attempt per source; no retry/loader execution/download.',samples=samples,rows_sampled=len(samples))
            if a.cancel and pathlib.Path(a.cancel).exists():results.append(result);break
            if time.monotonic()-bounds.started>=a.seconds:results.append(result);break
        result['elapsed_seconds']=time.monotonic()-bounds.source_started;results.append(result)
        print('schema supplement=',index,'/',len(selected),source,'status=',result['status'],'samples=',result['rows_sampled'],flush=True)
    counts=dict(collections.Counter(x['status'] for x in results))
    report={'schema_version':1,'status':'SOURCE_SCHEMA_SUPPLEMENT_PILOT' if a.maximum_sources<29 else 'SOURCE_SCHEMA_SUPPLEMENT_REVIEW_ONLY',
        'sources_targeted':len(selected),'sources_processed':len(results),'status_counts':counts,'rows_sampled':sample_total,
        'maximum_sources':29,'maximum_rows_per_source':2,'maximum_total_rows':58,'per_source_seconds':20,
        'total_wall_seconds':a.seconds,'elapsed_seconds':time.monotonic()-bounds.started,'source_bytes_hashed':hash_bytes_total,
        'input_bindings':{str(path):hashlib.sha256(path.read_bytes()).hexdigest() for path in (report_input,inventory_input)},
        'formal_training_admitted':False,'new_training':False,'teacher_called':False,'sources':results}
    (out/'supplement.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    lines=['## 29 个仅目录来源的真实训练字段补查','',
        f"目标 {len(selected)} 来源，处理 {len(results)} 来源；最多每源2行，累计 {sample_total} 行。结果统计 `{json.dumps(counts,ensure_ascii=False)}`。只读冻结 inventory 中的真实原train载体；没有运行来源 loader、教师、模型、训练或新增下载。每源最多一次20秒，整体上限{a.seconds}秒。",'',
        '| 来源 | 补查状态 | 样本行数 | 实际字段/答案语义或未做原因 |', '|---|---|---:|---|']
    for result in results:
        samples=result.get('samples',[])
        description=(', '.join(samples[0]['schema'])+'；'+samples[0]['answer_semantics']) if samples else result.get('failure',result.get('reason','unknown'))
        lines.append('| '+result['source']+' | '+result['status']+' | '+str(result['rows_sampled'])+' | '+description.replace('|','/')+' |')
    lines+=['','真实样本及逐字节来源SHA只保存在本机私人 `'+str(out/'supplement.json')+'`。前2行格式成功不是全源标签质量、完整训练或正式准入证明；多配置只检查第一个真实train载体，其他配置仍须逐项适配。']
    content='\n'.join(lines)+'\n';(out/'report.md').write_text(content,encoding='utf-8')
    if a.report:
        path=pathlib.Path(a.report);existing=path.read_text(encoding='utf-8')
        if '## 29 个仅目录来源的真实训练字段补查' in existing:raise ValueError('Preserve existing supplement appendix')
        path.write_text(existing+'\n'+content,encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k not in ('sources','input_bindings')},ensure_ascii=False),flush=True)


if __name__=='__main__':main()
