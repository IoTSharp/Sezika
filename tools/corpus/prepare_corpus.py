"""Offline Sezika corpus preparation. No model, feature, training or inference code.

Raw inputs are immutable; output is append-only gzip JSONL with a mandatory
exclusion ledger. Original source labels are never called calibrated probabilities.
"""
import argparse, ast, collections, csv, gzip, hashlib, json, math, pathlib, re, shutil, sqlite3, subprocess, tarfile, threading, time, unicodedata
from bounded_start import wait_for_guardian

def compact(x): return json.dumps(x,ensure_ascii=False,separators=(',',':'),sort_keys=True)
def sha(x): return hashlib.sha256(x.encode('utf-8')).hexdigest()
def parsed(x): return json.loads(x) if isinstance(x,str) else x
def text(x): return x if isinstance(x,str) else compact(x)
def onehot(n,i):
    if not isinstance(i,int) or not 0<=i<n: raise ValueError('invalid_label_index')
    return [float(j==i) for j in range(n)]
def probability(values):
    p=[float(v) for v in values]
    if not p or any(not math.isfinite(v) or v<0 for v in p) or abs(sum(p)-1)>2e-5:raise ValueError('invalid_target_distribution')
    return p
def question(state,instruction,kind,options,target,label='source_annotation',language='en',family=None,suffix='',semantics=None):
    if kind not in ('Choice','Boolean','Score') or not 2<=len(options)<=(10 if kind=='Score' else 32):raise ValueError('candidate_count_or_type')
    if len(target)!=len(options) or (kind=='Boolean' and len(options)!=2):raise ValueError('target_dimension')
    ids=[str(i) for i in range(len(options))] if kind!='Boolean' else ['false','true']
    return {'State':text(state),'Instruction':instruction,'Type':kind,'Candidates':[{'Id':i,'Text':text(t)} for i,t in zip(ids,options)],
            'Target':probability(target),'Language':language,'LabelKind':label,'LabelSemantics':semantics or label,'SourceFamily':family,'Suffix':suffix}
def sampled_class(state,label,names,instruction,family):
    if label not in names:raise ValueError('class_label_missing')
    # Gold-dependent candidate construction is a training-only derivation,
    # never a claim of evaluating the complete 77/150-class classification task.
    negatives=sorted((v for v in names if v!=label),key=lambda v:sha(family+'|negative|'+v))[:31]
    options=sorted([label]+negatives,key=lambda v:sha(family+'|position|'+v))
    q=question(state,instruction,'Choice',[v.replace('_',' ') for v in options],onehot(len(options),options.index(label)),family=family,
               semantics='original_class_label; training_only_gold_plus_up_to_31_negative_candidates')
    return q

class Budget:
    def __init__(self,a):self.a=a;self.start=time.monotonic();self.records=0
    def check(self):
        if time.monotonic()-self.start>=self.a.seconds or (self.a.cancel and pathlib.Path(self.a.cancel).exists()):raise TimeoutError('deadline_or_cancel')
        if self.records>=self.a.maximum_records:raise RuntimeError('record_bound_exceeded')

def source_rows(path,decoder,budget,maximum,logroot):
    budget.check()
    if path.suffix=='.parquet':
        seconds=min(900,max(1,int(budget.a.seconds-(time.monotonic()-budget.start))))
        err=(logroot/(sha(str(path))[:16]+'.decoder.log')).open('xb')
        proc=subprocess.Popen(['C:/Program Files/dotnet/dotnet.exe',decoder,'export-json',str(path),'-',str(maximum),str(seconds)],stdout=subprocess.PIPE,stderr=err)
        # Popen retains the owned process handle; no process-name or stale-PID kill.
        timer=threading.Timer(seconds+5,proc.terminate);timer.start()
        try:
            for i in range(maximum):
                budget.check();line=proc.stdout.readline(4*1024*1024+1)
                if not line:break
                if len(line)>4*1024*1024:raise ValueError('source_row_byte_bound')
                yield i+1,json.loads(line.decode('utf-8-sig'))
            if proc.stdout.readline(1):raise ValueError('source_row_item_bound')
            if proc.wait(timeout=10)!=0:raise RuntimeError('parquet_decode_failed')
        finally:
            timer.cancel()
            if proc.poll() is None:proc.terminate()
            try:proc.wait(timeout=10)
            except subprocess.TimeoutExpired:proc.kill();proc.wait(timeout=5)
            proc.stdout.close();err.close()
    elif str(path).endswith(('.jsonl','.jsonl.gz')):
        op=gzip.open if str(path).endswith('.gz') else open
        with op(path,'rt',encoding='utf-8') as f:
            for i in range(maximum):
                budget.check();line=f.readline(4*1024*1024+1)
                if not line:break
                if len(line)>4*1024*1024:raise ValueError('source_row_byte_bound')
                yield i+1,json.loads(line)
            else:
                if not budget.a.pilot_per_file and f.readline(1):raise ValueError('source_row_item_bound')
    elif path.suffix=='.csv':
        with path.open(encoding='utf-8',newline='') as f:
            reader=csv.DictReader(f)
            for i,row in enumerate(reader):
                budget.check()
                if i>=maximum:
                    if budget.a.pilot_per_file:break
                    raise ValueError('source_row_item_bound')
                yield i+1,row
    else:raise ValueError('unsupported_file_format')

def kaivoss(row):
    p=row['prompt'];m=re.fullmatch(r'<state>\n(.*?)\n</state>\n\nQuestion: (.*?)\nOptions:\n(.*?)\n\nAnswer with one letter\.(?:.*)',p,re.S)
    if not m:raise ValueError('unsupported_original_prompt_layout')
    letters=row['letters'];lines=m[3].splitlines()
    if len(lines)!=len(letters):raise ValueError('option_boundary')
    options=[]
    for letter,line in zip(letters,lines):
        if not line.startswith(letter+'. '):raise ValueError('option_identity')
        options.append(line[len(letter)+2:])
    kind={'choice':'Choice','noul':'Boolean','score':'Score'}.get(row['qtype'])
    if kind=='Boolean' and (len(options)!=2 or not options[0].lower().startswith('no - ') or not options[1].lower().startswith('yes - ')):raise ValueError('boolean_mapping')
    if kind=='Score' and any(not t.startswith(str(i+1)+' - ') for i,t in enumerate(options)):raise ValueError('ordinal_mapping_unreviewed')
    yield question(m[1],m[2],kind,options,row['target'],'teacher_distribution',family=str(row.get('state_id','')),
                   semantics='original_teacher_soft_targets_not_measured_correctness')

def adapt(source,row,names=None):
    if source=='kaivoss/system-one-270m-data':yield from kaivoss(row)
    elif source=='google/boolq':yield question(row['passage'],row['question'],'Boolean',['False','True'],onehot(2,int(row['answer'])),family=sha(row['passage']))
    elif source=='stanfordnlp/snli':yield question('Premise: '+row['premise']+'\nHypothesis: '+row['hypothesis'],'What relation does the hypothesis have to the premise?','Choice',['Entailed','Neutral','Contradicted'],onehot(3,row['label']),family=sha(row['premise']))
    elif source=='PolyAI/banking77':yield sampled_class(row['text'],row['category'],names,'Which banking intent matches this utterance?',sha(row['text']))
    elif source=='clinc/clinc_oos':yield sampled_class(row[0],row[1],names,'Which intent matches this utterance? Use out of scope if no supported intent applies.',sha(row[0]))
    elif source=='google/civil_comments':
        for attr in ('toxicity','severe_toxicity','obscene','threat','insult','identity_attack','sexual_explicit'):
            v=float(row[attr])
            if not 0<=v<=1:raise ValueError('annotation_fraction_range')
            yield question(row['text'],'Does this comment exhibit '+attr.replace('_',' ')+'?','Boolean',['False','True'],[1-v,v],family=sha(row['text']),suffix=attr,semantics='reported_human_annotation_fraction_not_calibrated_correctness')
    elif source=='google-research-datasets/go_emotions':
        if 'labels' not in row:raise ValueError('use_simplified_original_split_only')
        labels=set(row['labels'])
        for i,name in enumerate(EMOTIONS):yield question(row['text'],'Does this text express '+name+'?','Boolean',['False','True'],onehot(2,int(i in labels)),family=str(row['id']),suffix=name,semantics='multi_label_annotation_one_boolean_per_emotion')
    elif source in ('nvidia/HelpSteer','nvidia/HelpSteer2'):
        for attr in ('helpfulness','correctness','coherence','complexity','verbosity'):
            v=float(row[attr])
            if not 0<=v<=4 or not v.is_integer():raise ValueError('ordinal_scale_requires_integral_0_to_4')
            yield question('Prompt:\n'+row['prompt']+'\nResponse:\n'+row['response'],RUBRICS[attr],'Score',RATING_LEVELS[attr],onehot(5,int(v)),'human_ordinal_annotation',family=sha(row['prompt']),suffix=attr,semantics='source_'+attr+'_ordinal_0_to_4_not_correctness_probability')
    elif source=='Anthropic/hh-rlhf':
        chosen=row['chosen'];rejected=row['rejected'];sep='\n\nAssistant:'
        if sep not in chosen or sep not in rejected:raise ValueError('preference_dialogue_boundary')
        cp,ca=chosen.rsplit(sep,1);rp,ra=rejected.rsplit(sep,1)
        if cp!=rp or not ca.strip() or not ra.strip():raise ValueError('preference_context_differs')
        opts=[ca.strip(),ra.strip()];target=0
        if int(sha(cp)[:2],16)%2:opts.reverse();target=1
        yield question(cp,'Which response better satisfies the conversation under the original human preference annotation?','Choice',opts,onehot(2,target),'human_preference',family=sha(cp),semantics='pairwise_preference_not_objective_correctness')
    elif source=='samatv256/jev-decisions-v1':
        if row['question_type']!='choice':raise ValueError('clean50k_choice_only')
        # Only symmetric option definitions enter the input, not target arguments.
        opts=[compact({k:o.get(k) for k in ('type','label','description','schema_json')}) for o in row['answer_options']]
        s=row['state'];family=s.get('trajectory_id') or s.get('task_id') or sha(text(s)) if isinstance(s,dict) else sha(text(s))
        yield question(s,row['question'],'Choice',opts,onehot(len(opts),row['target_index']),'source_action_annotation',family=str(family),semantics='public_clean50k_reference_choice_not_calibrated_probability')
    elif source=='dwidlee/systemone-lite-phase2':
        keys=row['criteria_keys'];alias=row['label_alias'];meta=parsed(row['meta'])
        if not (meta.get('schema')=='choice' or (meta.get('schema') is None and meta.get('task_schema')=='staged_v1')) or alias not in keys:raise ValueError('phase2_non_choice_or_label')
        yield question(row['state'],row['instructions'],'Choice',row['criteria_values'],onehot(len(keys),keys.index(alias)),'source_engine_reference' if meta.get('label_source')=='stockfish' else 'synthetic_reference',family=sha(row['state']),semantics='upstream_reference_choice; stockfish_reference_when_declared; not_teacher_accuracy')
    elif source=='ZefanCai/Open-Jev':
        kind={'choice':'Choice','noul':'Boolean','boolean':'Boolean','score':'Score'}.get(row['kind'])
        metadata=parsed(row['metadata_json']);state=parsed(row['state_json']);language=metadata.get('language') or metadata.get('locale') or 'und'
        family=row.get('group_id') or metadata.get('document_family_id') or sha(text(state))
        yield question(state,row['question'],kind,row['options'],row['target'],'synthetic_reference',language,str(family),semantics='source_synthetic_target_preserved_not_real_accuracy')
    elif source=='LocalLLaMA/typed-decisions':
        qs=parsed(row['questions']);gold=parsed(row['gold'])
        for key,q in qs.items():
            g=gold[key];kind={'choice':'Choice','noul':'Boolean','score':'Score'}[q['type']]
            crit=q.get('criteria',{'false':'False','true':'True'} if kind=='Boolean' else None)
            if crit is None:raise ValueError('missing_criteria_non_boolean')
            ids=list(crit) if isinstance(crit,dict) else [str(i) for i in range(len(crit))]
            options=list(crit.values()) if isinstance(crit,dict) else crit
            if kind=='Boolean':
                if set(ids)!=set(('false','true')):raise ValueError('typed_boolean_mapping')
                ids=['false','true'];options=[crit[i] for i in ids]
            yield question(row['state'],q['instructions'],kind,options,[g['probabilities'][i] for i in ids],'synthetic_distribution',family=str(row['id']),suffix=key,semantics='synthetic_distribution_preserved_not_measured_correctness')
    elif source=='AmazonScience/massive':
        family='massive-original-id:'+str(row['id']);language=row['locale']
        scenarios,intents=names
        q=question(row['utt'],'Which scenario matches this utterance?','Choice',scenarios,onehot(len(scenarios),scenarios.index(row['scenario'])),language=language,family=family)
        q['SourceMember']=row['_member'];yield q
        q=sampled_class(row['utt'],row['intent'],intents,'Which intent matches this utterance?',family)
        q['Language']=language;q['SourceMember']=row['_member'];yield q
    elif source in ('allenai/quartz','allenai/qasc'):
        choices=row['choices'];labels=choices['label'];gold=row['answerKey']
        if gold not in labels or len(labels)!=len(choices['text']):raise ValueError('science_answer_key_mapping')
        state=row['para'] if source=='allenai/quartz' else 'Fact 1: '+row['fact1']+'\nFact 2: '+row['fact2']
        yield question(state,row['question'],'Choice',choices['text'],onehot(len(labels),labels.index(gold)),'source_annotation','en',str(row.get('para_id') or row.get('id')),semantics='original_training_answer_key;provided_science_facts;not_teacher_gold')
    elif source=='Team-ACE/ToolACE':
        marker='Here is a list of functions in JSON format that you can invoke:'
        system=row['system']
        if marker not in system:raise ValueError('tool_definitions_boundary')
        tools,_=json.JSONDecoder().raw_decode(system.split(marker,1)[1].lstrip())
        if not isinstance(tools,list) or not 2<=len(tools)<=32:raise ValueError('tool_candidates_bound')
        toolnames=[t['name'] for t in tools]
        if len(set(toolnames))!=len(toolnames):raise ValueError('duplicate_tool_name')
        context=[];produced=0
        turns=row['conversations']
        if not isinstance(turns,list) or len(turns)>256:raise ValueError('tool_turn_bound')
        for turn_index,turn in enumerate(turns):
            if turn['from']=='assistant' and context and context[-1]['from']=='user':
                value=turn['value'].strip()
                if value.startswith('[') and value.endswith(']'):
                    matches=[i for i,n in enumerate(toolnames) if re.search(r'(?:^\[|,\s*)'+re.escape(n)+r'\s*\(',value)]
                    if len(matches)==1:
                        # Arguments/future tool responses and the current reference
                        # answer never enter the state. This is tool selection only.
                        q=question(context,'Select the single tool used in the next reference call. Arguments are outside this task.','Choice',[compact(t) for t in tools],onehot(len(tools),matches[0]),'synthetic_tool_reference','und',sha(compact(row['conversations'][:1])),str(turn_index),semantics='single_reference_tool_selection_only;arguments_and_generation_unsupported')
                        yield q;produced+=1
            context.append(turn)
        if produced==0:raise ValueError('no_unambiguous_single_tool_choice_generation_not_converted')
    else:raise ValueError('adapter_not_implemented')

def massive_rows(path,budget,maximum,pilot):
    emitted=0
    with tarfile.open(path,'r:gz') as tar:
        for member_index,member in enumerate(tar):
            budget.check()
            if member_index>=512:raise ValueError('massive_member_bound')
            if not member.isfile() or not member.name.endswith('.jsonl'):continue
            if member.size>64*1024*1024:raise ValueError('massive_member_byte_bound')
            with tar.extractfile(member) as f:
                per_member=0
                for line_index in range(100000):
                    budget.check();line=f.readline(1024*1024+1)
                    if not line:break
                    if len(line)>1024*1024:raise ValueError('massive_line_byte_bound')
                    row=json.loads(line)
                    if row['partition']!='train':continue
                    row['_member']=member.name;emitted+=1;per_member+=1
                    if emitted>maximum:raise ValueError('massive_train_row_bound')
                    yield emitted,row
                    if pilot and per_member>=pilot:break
                else:raise ValueError('massive_member_row_bound')

EMOTIONS=['admiration','amusement','anger','annoyance','approval','caring','confusion','curiosity','desire','disappointment','disapproval','disgust','embarrassment','excitement','fear','gratitude','grief','joy','love','nervousness','optimism','pride','realization','relief','remorse','sadness','surprise','neutral']
RUBRICS={
 'helpfulness':'Rate helpfulness from 0 (not helpful) to 4 (highly helpful), using the original HelpSteer rubric.',
 'correctness':'Rate correctness from 0 (completely incorrect) to 4 (completely correct), using the original HelpSteer rubric.',
 'coherence':'Rate coherence from 0 (incoherent) to 4 (highly coherent), using the original HelpSteer rubric.',
 'complexity':'Rate complexity from 0 (basic language) to 4 (expert language), not answer quality.',
 'verbosity':'Rate verbosity from 0 (very brief) to 4 (very verbose), not answer quality.'}
RATING_LEVELS={k:[f'{i}: '+d for i,d in enumerate(v)] for k,v in {
 'helpfulness':['Not helpful','Slightly helpful','Moderately helpful','Helpful','Highly helpful'],
 'correctness':['Completely incorrect','Mostly incorrect','Partly correct','Mostly correct','Completely correct'],
 'coherence':['Incoherent','Mostly incoherent','Partly coherent','Mostly coherent','Highly coherent'],
 'complexity':['Basic','Simple','Intermediate','Advanced','Expert'],
 'verbosity':['Very brief','Brief','Moderate','Verbose','Very verbose']}.items()}

class Writer:
    def __init__(self,root,budget,prior=None):
        self.root=root;self.budget=budget;self.handles={};self.shards=[];self.counts=collections.Counter();self.rowcounts=collections.Counter()
        self.slots={}
        self.number_base=collections.Counter()
        self.db=sqlite3.connect(root/'dedup-index.sqlite');self.db.execute('PRAGMA journal_mode=WAL');self.db.execute('PRAGMA cache_size=-32768')
        if prior:
            if prior['status']!='PARTIAL_FAILED_PRESERVE_OUTPUTS':raise ValueError('Only failed bounded conversion may resume')
            self.shards=prior['shards'];budget.records=prior['records_written']
            self.counts.update({ast.literal_eval(k) if k.startswith('(') else k:v for k,v in prior['counts'].items()})
            for s in self.shards:
                key=(s['source'],s['split']);self.number_base[key]=max(self.number_base[key],int(pathlib.Path(s['path']).name.split('-')[1].split('.')[0])+1)
        else:self.db.execute('CREATE TABLE seen (key TEXT PRIMARY KEY, target TEXT NOT NULL, record_id TEXT NOT NULL, conflict INTEGER NOT NULL DEFAULT 0)')
        mode='a' if prior else 'x'
        self.exclusions=(root/'exclusions.jsonl').open(mode,encoding='utf-8');self.quarantine=(root/'quarantine.jsonl').open(mode,encoding='utf-8')
    def reject(self,source,path,row,reason):
        self.counts['quarantined']+=1;self.quarantine.write(compact({'source':source,'path':path,'row':row,'reason':reason})+'\n')
    def add(self,q,source,asset,row,row_hash):
        self.budget.check();state=unicodedata.normalize('NFC',q['State']).replace('\r\n','\n')
        if not state.strip() or not q['Instruction'].strip() or len(state)>262144 or len(q['Instruction'])>65536:raise ValueError('input_text_bounds')
        if len(set(c['Id'] for c in q['Candidates']))!=len(q['Candidates']):raise ValueError('candidate_identity')
        content=sha(compact([state,q['Instruction'],q['Type'],q['Candidates']]))
        target=compact(q['Target']);record_id='corpus-'+content;prior=self.db.execute('SELECT target,record_id,conflict FROM seen WHERE key=?',(content,)).fetchone()
        if prior:
            if prior[2] or prior[0]!=target:
                if prior[2]==0:self.exclusions.write(compact({'record_id':prior[1],'reason':'conflicting_original_targets_all_excluded'})+'\n');self.db.execute('UPDATE seen SET conflict=1 WHERE key=?',(content,));self.counts['conflicts']+=1
                self.reject(source,asset['path'],row,'conflicting_original_targets_all_excluded')
            else:self.counts['duplicates']+=1
            return
        declared_family=q.pop('SourceFamily',None)
        family=sha(source+'|'+str(declared_family or sha(state)))
        split='development' if int(family[:8],16)%10==0 else 'train'
        q.pop('Suffix',None)
        if q['Type']=='Choice':
            offset=int(content[:8],16)%len(q['Candidates'])
            q['Candidates']=q['Candidates'][offset:]+q['Candidates'][:offset]
            q['Target']=q['Target'][offset:]+q['Target'][:offset]
            q['CandidateTransform']='deterministic_cyclic_rotation_offset_'+str(offset)
        record={'SchemaVersion':1,'RecordId':record_id,'Family':family,'OriginalStateId':family,'Split':split,**q,
                'SourceLineSha256':row_hash,'SourceRow':row,
                'Source':{'Dataset':source,'Revision':asset.get('source_revision'),'Path':asset['path'],'FileSha256':asset['sha256'],'OriginalSplit':'unsplit' if source=='Team-ACE/ToolACE' else 'train','Row':row},
                'TrainingAdmission':'experimental_candidate_pending_length_and_source_review','HumanReview':'pending'}
        key=(source,split);number=self.number_base[key]+self.rowcounts[key]//50000
        slot=(source,split,number)
        if slot not in self.handles:
            if len(self.shards)>=4096:raise OSError('Shard item bound 4096')
            if shutil.disk_usage(self.root).free<12*1024**3:raise OSError('Reserved 12GiB free disk')
            directory=self.root/'shards'/source.replace('/','--');directory.mkdir(parents=True,exist_ok=True)
            p=directory/f'{split}-{number:05}.jsonl.gz';self.handles[slot]=gzip.open(p,'xt',encoding='utf-8',compresslevel=1)
            self.shards.append({'path':str(p.relative_to(self.root)),'source':source,'split':split,'records':0})
            self.slots[slot]=self.shards[-1]
        self.handles[slot].write(compact(record)+'\n');self.slots[slot]['records']+=1
        self.db.execute('INSERT INTO seen(key,target,record_id) VALUES(?,?,?)',(content,target,record_id));self.counts[(source,split,q['Type'])]+=1;self.rowcounts[key]+=1;self.budget.records+=1
        if self.budget.records%10000==0:self.db.commit();print('prepared=',self.budget.records,'seconds=',round(time.monotonic()-self.budget.start,1),flush=True)
        if self.rowcounts[key]%50000==0:self.handles.pop(slot).close()
    def close(self):
        for h in self.handles.values():h.close()
        self.db.commit();self.db.close();self.exclusions.close();self.quarantine.close()

def main():
    wait_for_guardian()
    ap=argparse.ArgumentParser();ap.add_argument('--dataset',required=True);ap.add_argument('--decoder',required=True);ap.add_argument('--output',required=True)
    ap.add_argument('--pilot-per-file',type=int,default=0);ap.add_argument('--maximum-records',type=int,default=30_000_000);ap.add_argument('--maximum-sources',type=int,default=18);ap.add_argument('--seconds',type=int,default=1700);ap.add_argument('--cancel')
    ap.add_argument('--resume',action='store_true')
    ap.add_argument('--rights-review')
    ap.add_argument('--source');ap.add_argument('--recover-ledger')
    a=ap.parse_args()
    if not 0<=a.pilot_per_file<=1000 or not 1<=a.maximum_records<=50_000_000 or not 1<=a.maximum_sources<=96 or not 1<=a.seconds<=1800:raise ValueError('Explicit bounds required')
    root=pathlib.Path(a.dataset).resolve();out=pathlib.Path(a.output).resolve()
    prior=None;stage=1
    if a.resume:
        if not (out/'OWNED_BY_SEZIKA_CORPUS').is_file() or (out/'corpus-bindings.json').exists():raise ValueError('Owned unbound failed corpus required')
        prior=json.loads((out/'manifest.json').read_text(encoding='utf-8'));stage=prior.get('stage',1)+1
        if stage>4 or prior['status']!='PARTIAL_FAILED_PRESERVE_OUTPUTS':raise ValueError('Maximum four resumable conversion stages')
        snapshot=out/f'stage-{stage-1:02}'
        snapshot.mkdir(exist_ok=False)
        for name in ('manifest.json','exclusions.jsonl','quarantine.jsonl'):shutil.copyfile(out/name,snapshot/name)
        (out/'manifest.json').unlink() # exact owned manifest archived above; source and closed shards unchanged
    else:
        if out.exists():raise ValueError('Preserve existing corpus')
        out.mkdir(parents=True);(out/'OWNED_BY_SEZIKA_CORPUS').write_text('Dedicated preparation outputs; immutable sources and closed shards; bounded stages.\n')
    decoder_logs=out/f'decoder-logs-stage-{stage:02}';decoder_logs.mkdir()
    assets=json.loads((root/'ASSET_MANIFEST.json').read_text(encoding='utf-8-sig'));rights_path=pathlib.Path(a.rights_review) if a.rights_review else root/'RIGHTS_MANIFEST.json'
    rights={x['dataset']:x for x in json.loads(rights_path.read_text(encoding='utf-8-sig'))}
    groups=collections.defaultdict(list)
    for asset in assets:
        s=asset['dataset'];p=asset['path']
        if a.source and s!=a.source:continue
        if rights.get(s,{}).get('decision')!='eligible_under_declared_terms_with_attribution':continue
        include='train' in pathlib.PurePosixPath(p).name and p.endswith(('.parquet','.jsonl','.jsonl.gz','.csv'))
        if s=='LocalLLaMA/typed-decisions':include=include and '/all/' not in p
        if s=='google-research-datasets/go_emotions':include=include and '/simplified/' in p
        if s=='clinc/clinc_oos':include=p.endswith('/data_full.json')
        if s=='samatv256/jev-decisions-v1':include=include and '/clean50k/' in p
        if s=='ZefanCai/Open-Jev':include=include and p.endswith('.parquet') # same frozen records' original JSON carrier retained as provenance
        if s=='AmazonScience/massive':include=p.endswith('/amazon-massive-dataset-1.1.tar.gz')
        if s=='Team-ACE/ToolACE':include=p.endswith('/data.json')
        if include:groups[s].append(asset)
    budget=Budget(a);writer=Writer(out,budget,prior);coverage=[] if not prior else prior['source_coverage'];status='COMPLETE_PREPARATION_ONLY';failure=None
    recovery_rows=None
    if a.recover_ledger:
        if a.source!='dwidlee/systemone-lite-phase2' or a.resume:raise ValueError('Explicit standalone phase2 recovery only')
        recovery_rows=set()
        with pathlib.Path(a.recover_ledger).open(encoding='utf-8') as f:
            for i,line in enumerate(f):
                if i>=2000000:raise ValueError('Recovery ledger item bound')
                budget.check();item=json.loads(line)
                if item['source']==a.source and item['reason']=='phase2_non_choice_or_label':recovery_rows.add(item['row'])
    try:
        chosen=list(sorted(groups.items()))[:a.maximum_sources]
        for source,files in chosen:
            budget.check();entry=next((x for x in coverage if x['source']==source),None)
            if entry is None:entry={'source':source,'files':[],'status':'processed'};coverage.append(entry)
            names=None
            if source=='PolyAI/banking77':
                with (root/next(x['path'] for x in files if x['path'].endswith('/train.csv'))).open(encoding='utf-8',newline='') as f:names=sorted({r['category'] for r in csv.DictReader(f)})
            for asset in sorted(files,key=lambda x:x['path']):
                if any(f['path']==asset['path'] for f in entry['files']):continue
                budget.check();p=(root/asset['path']).resolve()
                if not p.is_relative_to(root) or p.stat().st_size!=asset['bytes']:raise ValueError('Source path or byte count differs')
                h=hashlib.sha256()
                with p.open('rb') as f:
                    for j in range((asset['bytes']+1048575)//1048576+1):
                        budget.check();chunk=f.read(1048576)
                        if not chunk:break
                        h.update(chunk)
                if h.hexdigest()!=asset['sha256']:raise ValueError('Immutable source SHA differs')
                maximum=a.pilot_per_file or 5_000_000
                if source=='clinc/clinc_oos':
                    data=json.loads(p.read_text(encoding='utf-8'));rr=data['train']+data['oos_train'];names=sorted({r[1] for r in rr});rows=enumerate(rr[:maximum],1)
                    if len(rr)>maximum and not a.pilot_per_file:raise ValueError('clinc_train_row_bound')
                elif source=='AmazonScience/massive':
                    scenarios=set();intents=set()
                    for _,r in massive_rows(p,budget,5_000_000,0):scenarios.add(r['scenario']);intents.add(r['intent'])
                    names=(sorted(scenarios),sorted(intents));rows=massive_rows(p,budget,5_000_000,a.pilot_per_file)
                elif source=='Team-ACE/ToolACE':
                    if p.stat().st_size>64*1024*1024:raise ValueError('toolace_file_byte_bound')
                    rr=json.loads(p.read_text(encoding='utf-8'));rows=enumerate(rr[:maximum],1)
                    if len(rr)>maximum and not a.pilot_per_file:raise ValueError('toolace_row_bound')
                else:rows=source_rows(p,a.decoder,budget,maximum,decoder_logs)
                read=0;emitted_before=budget.records
                resume_row=entry.get('active_file',{}).get('rows_fully_processed',0) if entry.get('active_file',{}).get('path')==asset['path'] else 0
                entry['active_file']={'path':asset['path'],'rows_fully_processed':resume_row}
                for number,row in rows:
                    read+=1;budget.check()
                    if recovery_rows is not None and number not in recovery_rows:continue
                    if number<=resume_row:continue
                    try:
                        questions=list(adapt(source,row,names))
                        if len(questions)>256:raise ValueError('per_row_question_bound')
                        for q in questions:writer.add(q,source,asset,number,sha(compact(row)))
                    except (ValueError,KeyError,TypeError,IndexError) as error:writer.reject(source,asset['path'],number,str(error))
                    entry['active_file']['rows_fully_processed']=number
                entry['files'].append({'path':asset['path'],'sha256':asset['sha256'],'rows_read':read,'records_written':budget.records-emitted_before,'pilot_prefix':bool(a.pilot_per_file)})
                entry.pop('active_file',None)
                print(source,asset['path'],'read=',read,'written=',budget.records-emitted_before,flush=True)
        selected={s for s,_ in chosen}
        for source in sorted({x['dataset'] for x in assets}-selected):
            if not any(x['source']==source for x in coverage):coverage.append({'source':source,'status':'not_converted','reason':'evaluation_only_or_source_schema_requires_explicit_adapter','files':[]})
    except Exception as error:status='PARTIAL_FAILED_PRESERVE_OUTPUTS';failure=repr(error);raise
    finally:
        writer.close()
        final_deadline=time.monotonic()+90
        for shard in writer.shards:
            p=out/shard['path'];h=hashlib.sha256()
            with p.open('rb') as f:
                for j in range((p.stat().st_size+1048575)//1048576+1):
                    if time.monotonic()>final_deadline:raise TimeoutError('Final shard SHA deadline')
                    chunk=f.read(1048576)
                    if not chunk:break
                    h.update(chunk)
            shard.update(bytes=p.stat().st_size,sha256=h.hexdigest())
        report={'schema_version':1,'status':status,'stage':stage,'failure':failure,'pilot':bool(a.pilot_per_file),'formal_training_admitted':False,
                'token_length_admitted':False,'trainer_streaming_integration_accepted':False,'new_training':False,'teacher_called':False,
                'original_split':'train_only_except_ToolACE_original_unsplit_explicit_family_holdout','derived_split':'exact_family_sha_mod10_equals0_development_other_train; cross_source_semantic_family_review_pending',
                'source_row_hash_semantics':'SHA256 canonical sorted UTF8 JSON of decoded row; not original raw line bytes',
                'records_written':budget.records,'shards':writer.shards,'source_coverage':coverage,
                'counts':{str(k):v for k,v in writer.counts.items()},'exclusions_mandatory':'exclusions.jsonl','elapsed_seconds':time.monotonic()-budget.start}
        with (out/'manifest.json').open('x',encoding='utf-8') as f:json.dump(report,f,ensure_ascii=False,indent=2)
        print('status=',status,'records=',budget.records,'formal_training_admitted=false',flush=True)

if __name__=='__main__':main()
