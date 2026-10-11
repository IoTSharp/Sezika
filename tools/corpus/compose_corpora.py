"""Compose closed private corpora; offline data only, no features or training.

The primary gzip shards are immutable hardlinks. Supplemental rows are buffered
within explicit bounds until exact duplicates/conflicts have been resolved.
Source identities and original notice bytes are archived component by component.
"""
import argparse, gzip, hashlib, itertools, json, math, os, pathlib, re, sqlite3, time, unicodedata
from bounded_start import wait_for_guardian

MAX_ROWS=50_000_000
MAX_EXTRA=20_000
MAX_SHARDS=4096
MAX_META=64*1024*1024
MAX_BUFFER=256*1024*1024

def compact(value):return json.dumps(value,ensure_ascii=False,sort_keys=True,separators=(',',':'))
def text_hash(value):return hashlib.sha256(value.encode('utf-8')).hexdigest()
def hex_sha(value):return isinstance(value,str) and bool(re.fullmatch('[0-9a-f]{64}',value))

class Budget:
    def __init__(self,seconds,cancel=None):self.started=time.monotonic();self.seconds=seconds;self.cancel=pathlib.Path(cancel) if cancel else None
    def check(self):
        if time.monotonic()-self.started>=self.seconds or (self.cancel and self.cancel.exists()):raise TimeoutError('composition deadline or cancellation')

def within(root,relative):
    path=pathlib.Path(relative)
    if path.is_absolute() or '..' in path.parts:raise ValueError('unsafe relative path')
    result=root/path
    started=time.monotonic();ancestry=(result,*result.parents)
    if len(ancestry)>64:raise ValueError('path depth bound64')
    for current in ancestry:
        if time.monotonic()-started>=5:raise TimeoutError('path ancestry deadline')
        if current.is_symlink() or (hasattr(current,'is_junction') and current.is_junction()):raise ValueError('linked path rejected')
    return result

def digest(path,budget,maximum=64*1024**3):
    before=path.stat();size=before.st_size
    if size>maximum:raise ValueError('hash file byte bound')
    result=hashlib.sha256()
    with path.open('rb') as reader:
        for _ in range((size+1048575)//1048576+1):
            budget.check();chunk=reader.read(1048576)
            if not chunk:break
            result.update(chunk)
        else:raise ValueError('hash block bound')
    after=path.stat()
    if (before.st_size,before.st_mtime_ns,before.st_ino)!=(after.st_size,after.st_mtime_ns,after.st_ino):raise ValueError('input changed while hashing')
    return result.hexdigest()

def read_json(path,budget,maximum=16*1024*1024):
    budget.check()
    if path.stat().st_size>maximum:raise ValueError('metadata byte bound')
    return json.loads(path.read_text(encoding='utf-8-sig'))

def write_json(path,value,budget):
    budget.check()
    with path.open('x',encoding='utf-8',newline='\n') as writer:json.dump(value,writer,ensure_ascii=False,indent=2);writer.write('\n');writer.flush();os.fsync(writer.fileno())

def exclusions(root,budget):
    result={}
    with within(root,'exclusions.jsonl').open(encoding='utf-8') as reader:
        for _ in range(1_000_000):
            budget.check();line=reader.readline(8193)
            if not line:break
            if len(line)>8192:raise ValueError('exclusion row character bound')
            row=json.loads(line);identity=row['record_id']
            if not isinstance(identity,str) or not identity or identity in result:raise ValueError('duplicate/invalid input exclusion')
            result[identity]=row
        else:
            if reader.read(1):raise ValueError('exclusion item bound')
    return result

def frozen(entry,budget):
    root=pathlib.Path(entry['root']).absolute()
    if not root.is_dir():raise ValueError('input corpus root absent')
    manifest_path=within(root,'manifest.json');binding_path=within(root,'corpus-bindings.json')
    for path,key in ((manifest_path,'manifest_sha256'),(binding_path,'bindings_sha256')):
        if not hex_sha(entry.get(key)) or digest(path,budget)!=entry[key]:raise ValueError('pinned input '+key+' differs')
    manifest=read_json(manifest_path,budget);bindings=read_json(binding_path,budget)
    if manifest.get('schema_version')!=1 or manifest.get('status')!='COMPLETE_PREPARATION_ONLY' or manifest.get('formal_training_admitted') or bindings.get('FormalTrainingAdmitted'):
        raise ValueError('closed preparation-only corpus required; partial/admission cannot be promoted')
    shards=manifest['shards'];rows=manifest['records_written']
    if not isinstance(rows,int) or not 1<=rows<=MAX_ROWS or not 1<=len(shards)<=MAX_SHARDS or sum(s['records'] for s in shards)!=rows or len({s['path'] for s in shards})!=len(shards):raise ValueError('input shard/physical denominator')
    rights_path=within(root,'source-rights.json')
    asset_path=within(root,bindings['SourceAssetsPath']) if bindings.get('SourceAssetsPath') else within(root,'source-assets.json')
    if not asset_path.is_file() and bindings.get('SourceRoot'):asset_path=pathlib.Path(bindings['SourceRoot']).absolute()/'ASSET_MANIFEST.json'
    paths={'manifest.json':manifest_path,'corpus-bindings.json':binding_path,'source-rights.json':rights_path,'source-assets.json':asset_path,'exclusions.jsonl':within(root,'exclusions.jsonl')}
    expected={'ManifestSha256':'manifest.json','ExclusionsSha256':'exclusions.jsonl','SourceRightsSha256':'source-rights.json','SourceAssetsSha256':'source-assets.json'}
    for name,file in expected.items():
        if not hex_sha(bindings.get(name)) or digest(paths[file],budget)!=bindings[name]:raise ValueError('bound '+name+' differs')
    rights=read_json(rights_path,budget,MAX_META);assets=read_json(asset_path,budget,MAX_META)
    if not isinstance(rights,list) or not isinstance(assets,list):raise ValueError('explicit per-source rights/assets arrays required')
    for shard in shards:
        budget.check()
        if shard['split'] not in ('train','development') or not 1<=shard['records']<=50000 or not isinstance(shard['source'],str) or not shard['source']:raise ValueError('shard source/split/record bounds')
        path=within(root,shard['path'])
        if path.stat().st_size!=shard['bytes'] or digest(path,budget)!=shard['sha256']:raise ValueError('closed input shard SHA/size differs')
    hashes={'manifest.json':entry['manifest_sha256'],'corpus-bindings.json':entry['bindings_sha256'],**{file:bindings[name] for name,file in expected.items()}}
    return {'root':root,'manifest':manifest,'bindings':bindings,'entry':entry,'paths':paths,'metadata_hashes':hashes,'rights':rights,'assets':assets,'excluded':exclusions(root,budget)}

def canonical(row):
    kind=row['Type'];candidates=row['Candidates'];target=[float(v) for v in row['Target']]
    if kind not in ('Choice','Boolean','Score') or not 2<=len(candidates)<=(10 if kind=='Score' else 32) or len(target)!=len(candidates) or any(not math.isfinite(v) or v<0 for v in target) or abs(sum(target)-1)>2e-5:raise ValueError('invalid supplemental question mapping')
    if len({v['Id'] for v in candidates})!=len(candidates) or any(not v['Id'] or not v['Text'] for v in candidates):raise ValueError('candidate IDs/text')
    transform=row.get('CandidateTransform')
    if transform:
        match=re.fullmatch('deterministic_cyclic_rotation_offset_([0-9]+)',transform)
        if kind!='Choice' or not match or not 0<=int(match[1])<len(candidates):raise ValueError('unreviewed candidate transform')
        offset=int(match[1])
        if offset:candidates=candidates[-offset:]+candidates[:-offset];target=target[-offset:]+target[:-offset]
    if kind=='Boolean' and [v['Id'] for v in candidates]!=['false','true']:raise ValueError('Boolean false/true order')
    if kind=='Score' and [v['Id'] for v in candidates]!=[str(i) for i in range(len(candidates))]:raise ValueError('Score ascending IDs')
    state=unicodedata.normalize('NFC',row['State']).replace('\r\n','\n');instruction=row['Instruction']
    if not state.strip() or not instruction.strip() or len(state)>262144 or len(instruction)>65536:raise ValueError('input text bounds')
    key=text_hash(compact([state,instruction,kind,candidates]))
    if row['RecordId']!='corpus-'+key:raise ValueError('canonical unrotated record identity differs')
    if not hex_sha(row['Family']) or not hex_sha(row['SourceLineSha256']) or not isinstance(row['SourceRow'],int) or row['SourceRow']<1 or row['Split'] not in ('train','development'):raise ValueError('supplemental provenance/family bounds')
    return key,compact(target)

def supplemental(component,budget):
    for shard in component['manifest']['shards']:
        budget.check()
        with gzip.open(within(component['root'],shard['path']),'rt',encoding='utf-8') as reader:
            for _ in range(shard['records']):
                budget.check();line=reader.readline(4*1024*1024+1)
                if not line or len(line)>4*1024*1024:raise ValueError('truncated/overlarge supplemental row')
                row=json.loads(line)
                if row['Split']!=shard['split']:raise ValueError('row/shard split differs')
                yield row,shard['source'],len(line.encode('utf-8'))
            if reader.read(1):raise ValueError('supplemental physical denominator differs')

def archive(component,number,out,budget):
    folder=within(out,f'components/root-{number:02}');folder.mkdir(parents=True,exist_ok=False);receipt={}
    files=dict(component['paths']);missing=[];upstream_expected={};source_names={s['source'] for s in component['manifest']['shards']}
    for relative,expected in component['manifest'].get('source_notice_bindings',{}).items():
        if not hex_sha(expected) or digest(within(component['root'],relative),budget)!=expected:raise ValueError('source notice binding differs')
        files[relative]=within(component['root'],relative)
    # Only named small notice directories, at most two levels and 256 files.
    for name in ('source-notices','notices'):
        directory=within(component['root'],name)
        if not directory.is_dir():continue
        pending=[(directory,0)]
        for _ in range(257):
            budget.check()
            if not pending:break
            current,depth=pending.pop()
            children=list(itertools.islice(current.iterdir(),257))
            if len(children)>256:raise ValueError('notice directory item bound')
            for path in children:
                budget.check();relative=str(path.relative_to(component['root']));within(component['root'],relative)
                if path.is_dir():
                    if depth>=2:raise ValueError('notice depth bound2')
                    pending.append((path,depth+1))
                elif path.is_file():files[relative]=path
                else:raise ValueError('unsupported notice filesystem item')
                if len(files)>272:raise ValueError('notice file bound256')
        else:raise ValueError('notice directory traversal bound')
    for name in ('SOURCE-README.md','SOURCE-NOTICE.txt'):
        path=within(component['root'],name)
        if path.is_file():files[name]=path
    source_root=component['bindings'].get('SourceRoot')
    if source_root:
        source_root=pathlib.Path(source_root).absolute();assets=component['assets']
        if len(assets)>50000:raise ValueError('source asset metadata item bound50000')
        by_path={a.get('path'):a for a in assets if a.get('dataset') in source_names and isinstance(a.get('path'),str)}
        selected={}
        for asset in by_path.values():
            budget.check();name=pathlib.Path(asset['path']).name.upper()
            if name.startswith(('README','LICENSE','NOTICE','COPYING')) or name=='CITATION.CFF':selected[asset['path']]=asset
            evidence=asset.get('license_evidence')
            if evidence:
                if isinstance(evidence,str) and evidence in by_path:selected[evidence]=by_path[evidence]
                else:missing.append({'component':number,'dataset':asset.get('dataset'),'evidence':evidence,'reason':'license_evidence_not_bound_to_same_component_source_asset'})
        if len(selected)>128:raise ValueError('upstream notice item bound128')
        total=0
        for relative,asset in selected.items():
            budget.check();path=within(source_root,relative)
            if not path.is_file():missing.append({'component':number,'dataset':asset.get('dataset'),'path':relative,'reason':'bound_upstream_notice_file_absent'});continue
            if not hex_sha(asset.get('sha256')) or not isinstance(asset.get('bytes'),int) or path.stat().st_size!=asset['bytes'] or digest(path,budget,16*1024*1024)!=asset['sha256']:raise ValueError('upstream source notice bytes/SHA differs')
            total+=asset['bytes']
            if total>16*1024*1024:raise ValueError('upstream notices total byte bound16MiB')
            archive_path='source-notices/upstream/'+relative;files[archive_path]=path;upstream_expected[archive_path]=asset['sha256']
        for source in sorted(source_names):
            if not any(a.get('dataset')==source for a in selected.values()):missing.append({'component':number,'dataset':source,'reason':'no_bound_upstream_notice_asset_found'})
    elif not component['manifest'].get('source_notice_bindings') and not any(name.startswith('source-notices/') for name in files):
        missing.append({'component':number,'datasets':sorted(source_names),'reason':'no_SourceRoot_or_locally_bound_notice_files'})
    for relative,source in files.items():
        budget.check();destination=within(folder,relative);destination.parent.mkdir(parents=True,exist_ok=True)
        before=digest(source,budget,MAX_META)
        expected=component['metadata_hashes'].get(relative) or upstream_expected.get(relative)
        if expected and before!=expected:raise ValueError('closed component metadata changed before archival')
        with source.open('rb') as reader,destination.open('xb') as writer:
            for _ in range((source.stat().st_size+1048575)//1048576+1):
                budget.check();chunk=reader.read(1048576)
                if not chunk:break
                writer.write(chunk)
            writer.flush();os.fsync(writer.fileno())
        if digest(destination,budget,MAX_META)!=before:raise ValueError('archived byte identity differs')
        receipt[str(destination.relative_to(out))]=before
    return receipt,missing

def compose(entries,out,budget):
    if not 1<=len(entries)<=4:raise ValueError('input root bound4')
    if out.exists():raise ValueError('fresh output required; failed outputs are retained')
    components=[frozen(entry,budget) for entry in entries]
    if len({str(c['root']).casefold() for c in components})!=len(components):raise ValueError('duplicate input roots')
    primary=components[0];extras=sum(c['manifest']['records_written'] for c in components[1:])
    if extras>MAX_EXTRA:raise ValueError('supplemental physical row bound20000')
    index=within(primary['root'],'dedup-index.sqlite')
    if not index.is_file() or any(within(primary['root'],'dedup-index.sqlite'+suffix).exists() for suffix in ('-wal','-journal')):raise ValueError('closed primary dedup index without WAL/journal required')
    index_sha=digest(index,budget)
    if entries[0].get('dedup_sha256') and entries[0]['dedup_sha256']!=index_sha:raise ValueError('pinned primary dedup index SHA differs')
    out.mkdir(parents=True,exist_ok=False);(out/'OWNED_BY_SEZIKA_CORPUS_COMPOSITION').write_text('offline preparation only\n',encoding='utf-8')
    db=sqlite3.connect(index.as_uri()+'?mode=ro&immutable=1',uri=True,timeout=1)
    db.set_progress_handler(lambda: int(time.monotonic()-budget.started>=budget.seconds or bool(budget.cancel and budget.cancel.exists())),1000)
    retained={};blocked=set();excluded=dict(primary['excluded']);duplicates=conflicts=skipped=seen=buffer_bytes=0
    try:
        count=db.execute('SELECT COUNT(*) FROM seen').fetchone()[0]
        if count!=primary['manifest']['records_written']:raise ValueError('primary dedup/physical denominator mismatch')
        for component in components[1:]:
            for row,source,size in supplemental(component,budget):
                budget.check();seen+=1
                if seen>MAX_EXTRA:raise ValueError('supplemental row bound20000')
                if row['RecordId'] in component['excluded']:skipped+=1;continue
                key,target=canonical(row);prior=db.execute('SELECT target,record_id,conflict FROM seen WHERE key=?',(key,)).fetchone()
                if prior:
                    if prior[1]!='corpus-'+key:raise ValueError('primary dedup identity differs')
                    if prior[2] and prior[1] not in primary['excluded']:raise ValueError('primary index conflict lacks mandatory exclusion')
                    if prior[2] or prior[1] in excluded:skipped+=1
                    elif prior[0]!=target:excluded[prior[1]]={'record_id':prior[1],'reason':'cross_root_conflicting_original_targets_all_excluded'};conflicts+=1
                    else:duplicates+=1
                elif key in blocked:skipped+=1
                elif key in retained:
                    if retained[key][2]!=target:
                        buffer_bytes-=retained[key][3];del retained[key];blocked.add(key);conflicts+=1
                    else:duplicates+=1
                else:
                    buffer_bytes+=size
                    if buffer_bytes>MAX_BUFFER:raise ValueError('supplemental in-memory byte bound256MiB')
                    retained[key]=(row,source,target,size)
                if seen%1000==0:print('compose supplemental=',seen,'retained=',len(retained),'duplicates=',duplicates,'conflicts=',conflicts,'seconds=',round(time.monotonic()-budget.started,1),flush=True)
        db.close();db=None
        if digest(index,budget)!=index_sha:raise ValueError('primary read-only index changed')
        shards=[];notice_bindings={};notice_missing=[]
        for number,component in enumerate(components):
            pinned,missing=archive(component,number,out,budget);notice_bindings.update(pinned);notice_missing.extend(missing)
        for number,shard in enumerate(primary['manifest']['shards']):
            budget.check();relative=f'shards/primary/{number:05}.jsonl.gz';destination=within(out,relative);destination.parent.mkdir(parents=True,exist_ok=True);source=within(primary['root'],shard['path'])
            os.link(source,destination)
            if not os.path.samefile(source,destination) or destination.stat().st_size!=shard['bytes'] or digest(destination,budget)!=shard['sha256']:raise ValueError('hardlink inode/byte/SHA gate failed')
            shards.append({**shard,'path':relative})
            if number%20==0:print('hardlinked primary shards=',number+1,'/',len(primary['manifest']['shards']),flush=True)
        groups={}
        for row,source,target,size in retained.values():groups.setdefault((source,row['Split']),[]).append(row)
        for number,((source,split),rows) in enumerate(sorted(groups.items())):
            budget.check();relative=f'shards/supplemental/{number:05}.jsonl.gz';path=within(out,relative);path.parent.mkdir(parents=True,exist_ok=True)
            with gzip.open(path,'xt',encoding='utf-8',compresslevel=1) as writer:
                for row in rows:budget.check();writer.write(compact(row)+'\n')
            shards.append({'path':relative,'source':source,'split':split,'records':len(rows),'bytes':path.stat().st_size,'sha256':digest(path,budget)})
        physical=sum(s['records'] for s in shards)
        if physical>MAX_ROWS or len(shards)>MAX_SHARDS or len(excluded)>1_000_000:raise ValueError('composed corpus explicit bounds')
        with (out/'exclusions.jsonl').open('x',encoding='utf-8',newline='\n') as writer:
            for identity in sorted(excluded):budget.check();writer.write(compact(excluded[identity])+'\n')
            writer.flush();os.fsync(writer.fileno())
        rights=[row for component in components for row in component['rights']];assets=[row for component in components for row in component['assets']]
        write_json(out/'source-rights.json',rights,budget);write_json(out/'source-assets.json',assets,budget)
        if (out/'source-rights.json').stat().st_size>MAX_META or (out/'source-assets.json').stat().st_size>MAX_META:raise ValueError('composed source metadata bound64MiB')
        manifest={'schema_version':1,'status':'COMPLETE_PREPARATION_ONLY','pilot':any(c['manifest'].get('pilot') for c in components),'formal_training_admitted':False,'token_length_admitted':False,'trainer_streaming_integration_accepted':False,
                  'records_written':physical,'shards':shards,'exclusions_mandatory':'exclusions.jsonl','source_notice_bindings':notice_bindings,
                  'components':[{'root':str(c['root']),**{k:v for k,v in c['entry'].items() if k!='root'},'records_written':c['manifest']['records_written'],'pilot':c['manifest'].get('pilot',False)} for c in components],
                  'composition':{'algorithm':'closed_primary_hardlinks_unrotated_exact_key_overlay_v1','primary_dedup_sha256':index_sha,'supplemental_physical_rows':seen,'supplemental_retained':len(retained),'supplemental_excluded_skipped':skipped,'exact_duplicates':duplicates,'target_conflicts':conflicts,'new_conflicts_without_written_row':len(blocked),
                                 'family_review':'original supplied Family retained; unsplit/semantic/cross-source family review pending','admission':'component-specific decisions preserved; no new rights or quality admission','source_notice_missing_evidence':notice_missing,'future_composition':'no complete merged dedup index; cannot use this output as a primary without rebuilding/binding an index'},
                  'elapsed_seconds':time.monotonic()-budget.started,'gpu_training_run':False,'teacher_called':False}
        write_json(out/'manifest.json',manifest,budget)
        binding={'ManifestSha256':digest(out/'manifest.json',budget),'ExclusionsSha256':digest(out/'exclusions.jsonl',budget),'SourceRightsSha256':digest(out/'source-rights.json',budget),'SourceAssetsSha256':digest(out/'source-assets.json',budget),'SourceAssetsPath':'source-assets.json','FormalTrainingAdmitted':False}
        pending=out/'corpus-bindings.pending';write_json(pending,binding,budget);binding_sha=digest(pending,budget);budget.check();pending.rename(out/'corpus-bindings.json')
        print('composition complete physical=',physical,'excluded=',len(excluded),'manifest_sha=',binding['ManifestSha256'],'bindings_sha=',binding_sha,flush=True)
        return manifest,binding
    except Exception as error:
        with (out/'COMPOSITION_FAILED.json').open('x',encoding='utf-8') as writer:json.dump({'status':'FAILED_PRESERVE_ALL_OUTPUTS','failure':repr(error),'elapsed_seconds':time.monotonic()-budget.started,'gpu_training_run':False},writer)
        raise
    finally:
        if db is not None:db.close()

def self_test(out,budget):
    if out.exists():raise ValueError('fresh self-test directory required')
    out.mkdir(parents=True);checks=0
    def row(name,label):
        value={'Type':'Choice','State':name,'Instruction':'Which option?','Candidates':[{'Id':'0','Text':'zero'},{'Id':'1','Text':'one'}],'Target':[float(label==0),float(label==1)],'Family':text_hash(name),'OriginalStateId':text_hash(name),'Split':'train','Language':'en','SourceLineSha256':text_hash('source-'+name),'SourceRow':1,'CandidateTransform':'deterministic_cyclic_rotation_offset_0'}
        value['RecordId']='corpus-'+text_hash(compact([name,value['Instruction'],value['Type'],value['Candidates']]))
        return value
    def root(name,rows,exclude=(),index=False):
        directory=out/name;directory.mkdir();path=directory/'train.jsonl.gz'
        with gzip.open(path,'xt',encoding='utf-8') as writer:
            for value in rows:budget.check();writer.write(compact(value)+'\n')
        upstream=directory/'upstream';upstream.mkdir();(upstream/'README.md').write_bytes('Synthetic notice bytes. 中文。\n'.encode('utf-8'))
        write_json(directory/'source-rights.json',[{'dataset':name,'decision':'synthetic_test_only','formal_training_admitted':False}],budget)
        write_json(directory/'source-assets.json',[{'dataset':name,'path':'upstream/README.md','sha256':digest(upstream/'README.md',budget),'bytes':(upstream/'README.md').stat().st_size,'license_evidence':'upstream/README.md'}],budget)
        with (directory/'exclusions.jsonl').open('x',encoding='utf-8') as writer:
            for identity in exclude:writer.write(compact({'record_id':identity,'reason':'synthetic_conflict'})+'\n')
        manifest={'schema_version':1,'status':'COMPLETE_PREPARATION_ONLY','pilot':True,'formal_training_admitted':False,'records_written':len(rows),'shards':[{'path':'train.jsonl.gz','source':name,'split':'train','records':len(rows),'bytes':path.stat().st_size,'sha256':digest(path,budget)}]}
        write_json(directory/'manifest.json',manifest,budget)
        binding={'ManifestSha256':digest(directory/'manifest.json',budget),'ExclusionsSha256':digest(directory/'exclusions.jsonl',budget),'SourceRightsSha256':digest(directory/'source-rights.json',budget),'SourceAssetsSha256':digest(directory/'source-assets.json',budget),'SourceRoot':str(directory),'FormalTrainingAdmitted':False}
        write_json(directory/'corpus-bindings.json',binding,budget)
        if index:
            with sqlite3.connect(directory/'dedup-index.sqlite') as db:
                db.execute('CREATE TABLE seen(key TEXT PRIMARY KEY,target TEXT NOT NULL,record_id TEXT NOT NULL,conflict INTEGER NOT NULL DEFAULT 0)')
                for value in rows:
                    key,target=canonical(value);db.execute('INSERT INTO seen(key,target,record_id) VALUES(?,?,?)',(key,target,value['RecordId']))
        return {'root':str(directory),'manifest_sha256':binding['ManifestSha256'],'bindings_sha256':digest(directory/'corpus-bindings.json',budget)}
    first=root('primary',[row('a',0),row('b',0)],index=True);excluded=row('d',0)
    second=root('supplement',[row('a',0),row('b',1),row('c',1),excluded],exclude=[excluded['RecordId']])
    write_json(out/'inputs.json',[first,second],budget)
    manifest,binding=compose([first,second],out/'composed',budget)
    if manifest['records_written']!=3 or manifest['composition']['exact_duplicates']!=1 or manifest['composition']['target_conflicts']!=1:raise ValueError('dedup/conflict fixture mismatch')
    checks+=1
    for number,entry in enumerate((first,second)):
        if (pathlib.Path(entry['root'])/'upstream/README.md').read_bytes()!=(out/f'composed/components/root-{number:02}/source-notices/upstream/upstream/README.md').read_bytes():raise ValueError('bound upstream notice bytes changed')
    if manifest['composition']['source_notice_missing_evidence']:raise ValueError('synthetic notice evidence unexpectedly missing')
    checks+=1
    ledger=exclusions(out/'composed',budget)
    if set(ledger)!={row('b',0)['RecordId']}:raise ValueError('mandatory unique exclusion denominator differs')
    checks+=1
    if not os.path.samefile(out/'primary/train.jsonl.gz',out/'composed/shards/primary/00000.jsonl.gz'):raise ValueError('primary was copied rather than hardlinked')
    checks+=1
    values=list(supplemental({'root':out/'composed','manifest':manifest},budget))
    if len(values)!=3 or len({value[0]['RecordId'] for value in values})!=3:raise ValueError('same record written twice')
    checks+=1
    changed=False
    try:compose([{**first,'manifest_sha256':'0'*64},second],out/'changed-sha-rejected',budget)
    except ValueError:changed=True
    if not changed:raise ValueError('changed pinned input admitted')
    checks+=1
    rotated=row('e',1);rotated['Candidates']=rotated['Candidates'][1:]+rotated['Candidates'][:1];rotated['Target']=rotated['Target'][1:]+rotated['Target'][:1];rotated['CandidateTransform']='deterministic_cyclic_rotation_offset_1'
    if canonical(rotated)!=canonical(row('e',1)):raise ValueError('unrotated comparison differs')
    checks+=1
    for number,entry in enumerate((first,second)):
        for name in ('manifest.json','corpus-bindings.json','source-rights.json','source-assets.json','exclusions.jsonl'):
            budget.check()
            if (pathlib.Path(entry['root'])/name).read_bytes()!=(out/f'composed/components/root-{number:02}'/name).read_bytes():raise ValueError('original component bytes were rewritten')
    checks+=1
    if manifest['formal_training_admitted'] or binding['FormalTrainingAdmitted'] or manifest['composition']['family_review']!='original supplied Family retained; unsplit/semantic/cross-source family review pending':raise ValueError('composition elevated admission or semantic family review')
    checks+=1
    write_json(out/'self-test.json',{'status':'PASS_tiny_closed_synthetic_composition_only','checks':checks,'physical_fixture_rows':6,'gpu_training_run':False,'real_sources_composed':False},budget)
    print('self tests=',checks,flush=True)

def main():
    wait_for_guardian();parser=argparse.ArgumentParser();parser.add_argument('--inputs');parser.add_argument('--output',required=True);parser.add_argument('--seconds',type=int,default=600);parser.add_argument('--cancel');parser.add_argument('--self-test',action='store_true');args=parser.parse_args()
    if not 1<=args.seconds<=600:raise ValueError('wallclock bound1..600')
    budget=Budget(args.seconds,args.cancel);out=pathlib.Path(args.output).absolute()
    if args.self_test:self_test(out,budget)
    elif args.inputs:compose(read_json(pathlib.Path(args.inputs),budget),out,budget)
    else:raise ValueError('--inputs or --self-test required')

if __name__=='__main__':main()
