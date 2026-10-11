import argparse, json, pathlib, tarfile, time
from bounded_start import wait_for_guardian

def main():
    wait_for_guardian()
    a=argparse.ArgumentParser();a.add_argument('--dataset',required=True);a.add_argument('--output',required=True);a=a.parse_args()
    start=time.monotonic();root=pathlib.Path(a.dataset);result={}
    with tarfile.open(root/'raw/upstream/massive/amazon-massive-dataset-1.1.tar.gz','r:gz') as tar:
        members=[]
        for i,m in enumerate(tar):
            if i>=512 or time.monotonic()-start>30: raise TimeoutError('Tar inspection bound')
            if not m.isfile() or not m.name.endswith('.jsonl'):continue
            members.append({'name':m.name,'bytes':m.size})
            if len(members)==1:
                with tar.extractfile(m) as f:result['massive_first']=json.loads(f.readline(1024*1024))
        result['massive_members']=members
    p=root/'raw/hf/Team-ACE/ToolACE/data.json'
    if p.stat().st_size>64*1024*1024:raise ValueError('ToolACE inspection byte bound')
    data=json.loads(p.read_text(encoding='utf-8'));result['toolace_type']=type(data).__name__;result['toolace_count']=len(data)
    result['toolace_first']=data[0] if isinstance(data,list) else next(iter(data.items()))
    with pathlib.Path(a.output).open('x',encoding='utf-8') as f:json.dump(result,f,ensure_ascii=False,indent=2)
    print('MASSIVE locales=',len(members),'ToolACE rows=',len(data),flush=True)

if __name__=='__main__':main()
