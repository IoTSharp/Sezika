"""Bounded publisher metadata/download capture; no remote dataset code."""
import argparse, hashlib, json, pathlib, time, urllib.request, urllib.error
from bounded_start import wait_for_guardian

def main():
    wait_for_guardian();a=argparse.ArgumentParser();a.add_argument('--output',required=True);a=a.parse_args()
    root=pathlib.Path(a.output);root.mkdir(exist_ok=False);started=time.monotonic();attempts=[];assets=[];rights=[];coverage=[]
    def get(url,cap):
        for attempt in range(2):
            if time.monotonic()-started>=220 or (root/'cancel.request').exists():raise TimeoutError('Download phase deadline/cancel')
            item={'url':url,'route':'direct' if attempt==0 else 'proxy7890','success':False};attempts.append(item)
            try:
                proxy={} if attempt==0 else {'https':'http://127.0.0.1:7890','http':'http://127.0.0.1:7890'}
                opener=urllib.request.build_opener(urllib.request.ProxyHandler(proxy))
                with opener.open(urllib.request.Request(url,headers={'User-Agent':'Sezika-offline-corpus/1'}),timeout=min(20,220-(time.monotonic()-started))) as response:
                    raw=response.read(cap+1)
                if len(raw)>cap:raise ValueError('Download byte bound')
                item['success']=True;return raw
            except Exception as e:item['failure_type']=type(e).__name__;print('source request failed:',item['failure_type'],item['route'],flush=True)
        raise RuntimeError('Both source network routes failed')
    try:
        for source in ['allenai/qasc','allenai/quartz','allenai/quarel']:
            item={'source':source,'status':'not_downloaded'};coverage.append(item)
            try:
                raw=get('https://huggingface.co/api/datasets/'+source,4*1024*1024);meta=json.loads(raw);revision=meta['sha'];license=meta.get('cardData',{}).get('license')
                if len(revision)!=40:raise ValueError('Unpinned revision')
                folder=root/'raw'/source;folder.mkdir(parents=True);(folder/'metadata.json').write_bytes(raw)
                siblings=[x['rfilename'] for x in meta['siblings']]
                chosen=[x for x in siblings if ('train' in pathlib.PurePosixPath(x).name and x.endswith(('.parquet','.jsonl'))) or x=='README.md']
                if len(chosen)>8 or not any('train' in x for x in chosen):raise ValueError('Review source file layout')
                for name in chosen:
                    path=folder/name
                    if not path.resolve().is_relative_to(root.resolve()):raise ValueError('Source path escaped output')
                    wire=get('https://huggingface.co/datasets/'+source+'/resolve/'+revision+'/'+name,64*1024*1024)
                    path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(wire)
                    assets.append({'dataset':source,'source_revision':revision,'path':str(path.relative_to(root)).replace('\\','/'),'bytes':len(wire),'sha256':hashlib.sha256(wire).hexdigest(),'declared_license':license})
                decision='eligible_under_declared_terms_with_attribution' if license in ('apache-2.0','cc-by-4.0') else 'pending_license_review'
                rights.append({'dataset':source,'revision':revision,'card_license':license,'decision':decision,'training_admitted':False,'reason':'Publisher card captured; no test/validation files consumed; source label and family review still required'})
                item.update(status='downloaded_original_training_only',revision=revision,declared_license=license)
                print(source,item['status'],flush=True)
            except Exception as e:item['failure_type']=type(e).__name__;print(source,'retained failure=',item['failure_type'],flush=True)
    finally:
        for name,value in [('ASSET_MANIFEST.json',assets),('RIGHTS_MANIFEST.json',rights),('download-report.json',{'coverage':coverage,'attempts':attempts,'formal_training_admitted':False,'elapsed_seconds':time.monotonic()-started})]:
            with (root/name).open('x',encoding='utf-8') as f:json.dump(value,f,ensure_ascii=False,indent=2)

if __name__=='__main__':main()
