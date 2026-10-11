"""Refine only already saved schema samples. Never reads additional source rows."""
import argparse, collections, hashlib, json, pathlib, time
from bounded_start import wait_for_guardian


def main():
    wait_for_guardian()
    ap=argparse.ArgumentParser();ap.add_argument('--supplement',required=True);ap.add_argument('--inventory',required=True)
    ap.add_argument('--report',required=True);ap.add_argument('--cancel');a=ap.parse_args()
    started=time.monotonic()
    def check():
        if time.monotonic()-started>=60 or (a.cancel and pathlib.Path(a.cancel).exists()):raise TimeoutError('Interpretation deadline/cancel')
    path=pathlib.Path(a.supplement).resolve();inventory_path=pathlib.Path(a.inventory)
    if path.stat().st_size>8*1024*1024 or inventory_path.stat().st_size>8*1024*1024:raise ValueError('Metadata byte bound')
    report=json.loads(path.read_text(encoding='utf-8-sig'));inventory=json.loads(inventory_path.read_text(encoding='utf-8-sig'))
    if len(report['sources'])>29 or len(inventory)>5000:raise ValueError('Interpretation item bound')
    results=[];record_samples=0;metadata_samples=0
    for result in report['sources']:
        check();source=result['source'];samples=result.get('samples',[])
        if len(samples)>2:raise ValueError('Source sample bound')
        entry={'source':source,'original_status':result['status'],'rows_sampled':len(samples),'formal_training_admitted':False}
        if not samples:
            entry['status']=result['status'];entry['reason']=result.get('failure',result.get('reason'))
        else:
            rows=[sample['raw_row_private'] for sample in samples];first=rows[0]
            if 'counts' in first and 'output_sha256' in first and 'state' not in first:
                entry.update(status='processing_receipt_not_training_record',reason='首个带train路径的JSON是counts/输出SHA/复用证明收据，不是问题与答案；未把1个资料对象算作训练样本。')
                metadata_samples+=len(samples)
            elif set(first)<= {'id','split','metadata'} and 'metadata' in first:
                entry.update(status='benchmark_index_without_supervised_answer',reason='首个benchmark/train.jsonl只含id/split/metadata及环境状态索引，无question/target/答案；不能训练此载体。')
                metadata_samples+=len(samples)
            else:
                entry['status']='labelled_or_task_sample_schema_observed';record_samples+=len(samples)
                if source=='GhostScientist/jev-decisions-v1':
                    entry['reason']='已见state/question/options及answer_key；可候选选择适配，先核answer_key对应options.key或label，messages中的答案不送入模型输入。'
                    entry['observed_option_key_matches']=[sum(option.get('key')==row.get('answer_key') for option in row['options']) for row in rows]
                    entry['observed_option_label_matches']=[sum(option.get('label')==row.get('answer_key') for option in row['options']) for row in rows]
                elif source=='HIT-TMG/JevEmbed-Data':
                    entry['reason']='已见request_json/answers_json与question_type，需解码嵌入请求和答案，再按问题id/候选顺序映射；license/source_repo/source_revision逐组件审查。'
                    embedded=[]
                    for row in rows:
                        check();request=json.loads(row['request_json']);answers=json.loads(row['answers_json'])
                        embedded.append({'request_root_fields':list(request)[:32] if isinstance(request,dict) else type(request).__name__,
                                         'answer_root_fields':list(answers)[:32] if isinstance(answers,dict) else type(answers).__name__,
                                         'question_type':row['question_type'],'declared_row_license':row.get('license')})
                    entry['embedded_schema']=embedded
                elif source=='loaiabdalslam/VEJI-Synth-v2-200K':
                    entry['reason']='已见state与questions数组，每题含type/criteria/options/answer_index/target_probabilities，有限决策可适配；confidence_target不是已校准正确率，evidence/answer须隔离于输入。'
                    entry['observed_question_types']=sorted(set(question['type'] for row in rows for question in row['questions']))
                    entry['observed_target_dimensions']=[{'options':len(question.get('options',[])),'target':len(question.get('target_probabilities',[]))} for row in rows for question in row['questions']][:16]
                    entry['row_declared_license']=[row.get('metadata',{}).get('license') for row in rows]
                elif source=='SetFit/TREC-QC':
                    entry['reason']='实际有label/label_text细类与label_coarse/label_coarse_text粗类；当前32候选上限下应明确选粗6类或设计细类派生。'
                elif source=='SetFit/subj':
                    entry['reason']='text+label+label_text表明主观/客观分类；应据label_text固定布尔映射，不猜0/1对应哪一类。'
                    entry['observed_label_dictionary']=[{'id':row['label'],'name':row['label_text']} for row in rows]
                elif source=='Praveenrajus/jev-bench':
                    entry['reason']='已见state/question/primitive/label/soft_label/meta；是有限题型引用，但bench组件来源和原评测边界仍须保留，不能因文件名train就自动正式准入。'
                    entry['observed_primitives']=sorted(set(row['primitive'] for row in rows))
                elif source=='PKU-Alignment/BeaverTails':
                    entry['reason']='已见prompt/response/category/is_safe，安全Boolean与多标签类别可适配；CC-BY-NC范围及30k/330k复用待审。'
                else:
                    entry['reason']=samples[0]['answer_semantics']
                entry['observed_fields']=list(first)
        if entry['status'] in ('processing_receipt_not_training_record','benchmark_index_without_supervised_answer'):
            candidates=[x for x in inventory if x.get('dataset')==source and x.get('split_hint')=='source-train' and
                x['path'].endswith(('.parquet','.jsonl','.jsonl.gz')) and '/receipts/' not in x['path'].replace('\\','/') and '/benchmark/' not in x['path'].replace('\\','/')]
            if source=='C-Tianyu/NanoJev-Data':candidates=sorted(candidates,key=lambda x:('/unified/hard/' not in x['path'].replace('\\','/'),x['path']))
            entry['future_candidate_paths_from_inventory_only']=[{'path':x['path'],'sha256':x['sha256'],'note':'未在本次读取；供下一次明确选择载体'} for x in candidates[:2]]
        results.append(entry)
    counts=dict(collections.Counter(x['status'] for x in results))
    refined={'schema_version':1,'status':'SAVED_SAMPLES_SEMANTIC_INTERPRETATION_ONLY','parent_supplement_sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
             'no_additional_source_rows_read':True,'source_records_with_task_or_label_fields':record_samples,
             'metadata_or_receipt_objects':metadata_samples,'status_counts':counts,'sources':results,'formal_training_admitted':False}
    output=path.parent/'interpretation.json'
    with output.open('x',encoding='utf-8') as file:json.dump(refined,file,ensure_ascii=False,indent=2)
    lines=['### 已保存样本的语义澄清','',
        f"上述37个前缀对象中，{record_samples}个来自17个带任务/标签字段的来源；另外{metadata_samples}个是2来源的索引/处理收据，不能作为训练记录。原始补查清单保持不变，澄清清单 `{output}`；本阶段没有再读取原始文件、重试失败、调用教师或训练。",'',
        '| 来源 | 澄清状态 | 答案字段或未适配原因 |','|---|---|---|']
    for entry in results:
        check();lines.append('| '+entry['source']+' | '+entry['status']+' | '+str(entry.get('reason','')).replace('|','/')+' |')
    lines+=['','保留的格式失败：两个MTEB MASSIVE视图`.json.gz`按单JSON载体读取时出现第2行Extra data，提示实际可能是JSONL载体；SHP `.json`未在4MiB单JSON前缀中完成解析。没有为消除失败重复尝试来源。MASSIVE归档首个JSONL成员的第1行非train，本次2行约束下不继续扫描；完整转换主任务已有独立分区适配，不能混淆两项证据。','',
            'C-Tianyu的正确train候选与bekko更合适的Parquet/JSONL路径仅从原inventory列出，尚未读取；主任务可在下一次明确选择原始训练载体。']
    content='\n'.join(lines)+'\n';(path.parent/'interpretation-report.md').write_text(content,encoding='utf-8')
    target=pathlib.Path(a.report);previous=target.read_text(encoding='utf-8')
    if '### 已保存样本的语义澄清' in previous:raise ValueError('Preserve prior interpretation appendix')
    target.write_text(previous+'\n'+content,encoding='utf-8')
    print(json.dumps({k:v for k,v in refined.items() if k not in ('sources','parent_supplement_sha256')},ensure_ascii=False),flush=True)


if __name__=='__main__':main()
