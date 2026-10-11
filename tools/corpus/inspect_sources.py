"""Bounded offline schema inspection; no model inference or dataset loader execution."""
import argparse, gzip, hashlib, json, pathlib, subprocess, time
from bounded_start import wait_for_guardian

def main():
    wait_for_guardian()
    ap = argparse.ArgumentParser()
    ap.add_argument('--dataset', required=True)
    ap.add_argument('--decoder', required=True)
    ap.add_argument('--output', required=True)
    ap.add_argument('--maximum-sources', type=int, default=18)
    ap.add_argument('--seconds', type=int, default=180)
    ap.add_argument('--all-typed-configs',action='store_true')
    a = ap.parse_args()
    started = time.monotonic()
    root = pathlib.Path(a.dataset).resolve()
    output = pathlib.Path(a.output).resolve()
    if output.exists(): raise ValueError('Preserve existing inspection')
    assets = json.loads((root/'ASSET_MANIFEST.json').read_text(encoding='utf-8-sig'))
    rights = {x['dataset']:x for x in json.loads((root/'RIGHTS_MANIFEST.json').read_text(encoding='utf-8-sig'))}
    groups = {}
    for x in assets:
        p = x['path']
        if ('train' in p and p.endswith(('.parquet','.jsonl','.jsonl.gz'))) or p.endswith('/data_full.json') or p.endswith('/train.csv'):
            groups.setdefault(x['dataset'], []).append(x)
    result = []
    for source, files in list(sorted(groups.items()))[:a.maximum_sources]:
        if time.monotonic()-started >= a.seconds: raise TimeoutError('Inspection deadline')
        x = sorted(files, key=lambda v:v['path'])[0]
        p = (root/x['path']).resolve()
        if not p.is_relative_to(root) or p.stat().st_size != x['bytes']: raise ValueError('Source path/size differs')
        record = {'dataset':source,'path':x['path'],'revision':x.get('source_revision'),'sha256':x['sha256'],'rights':rights.get(source)}
        if str(p).endswith('.parquet'):
            call = subprocess.run(['C:/Program Files/dotnet/dotnet.exe',a.decoder,'export-json',str(p),'-','1','20'],capture_output=True,timeout=25,check=False)
            record['exit_code']=call.returncode
            record['stderr']=call.stderr.decode('utf-8',errors='replace')[-3000:]
            if call.returncode==0: record['row']=json.loads(call.stdout.decode('utf-8-sig').splitlines()[0])
        elif str(p).endswith(('.jsonl','.jsonl.gz')):
            op = gzip.open if str(p).endswith('.gz') else open
            with op(p,'rt',encoding='utf-8') as f: record['row']=json.loads(f.readline(4*1024*1024))
        elif p.name=='data_full.json':
            data=json.loads(p.read_text(encoding='utf-8')); record['keys']=list(data);record['row']=data['train'][0]
        else:
            with open(p,encoding='utf-8') as f:record['first_lines']=[f.readline(8192),f.readline(8192)]
        result.append(record)
        if a.all_typed_configs and source=='LocalLLaMA/typed-decisions':
            for extra in sorted(files,key=lambda v:v['path'])[1:8]:
                if time.monotonic()-started>=a.seconds:raise TimeoutError('Extra schema deadline')
                call=subprocess.run(['C:/Program Files/dotnet/dotnet.exe',a.decoder,'export-json',str(root/extra['path']),'-','1','20'],capture_output=True,timeout=25,check=True)
                result.append({'dataset':source,'path':extra['path'],'row':json.loads(call.stdout.decode('utf-8-sig').splitlines()[0])})
        print(source, 'keys=',list(record.get('row',{})) if isinstance(record.get('row'),dict) else type(record.get('row')).__name__,flush=True)
    output.parent.mkdir(parents=True,exist_ok=True)
    with output.open('x',encoding='utf-8') as f:json.dump({'status':'SCHEMA_INSPECTION_ONLY','elapsed_seconds':time.monotonic()-started,'sources':result},f,ensure_ascii=False,indent=2)

if __name__=='__main__':main()
