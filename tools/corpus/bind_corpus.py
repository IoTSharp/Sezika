"""Bind immutable preparation manifest, exclusions and source rights; no admission."""
import argparse,hashlib,json,pathlib,time
from bounded_start import wait_for_guardian

def main():
    wait_for_guardian();p=argparse.ArgumentParser();p.add_argument('--corpus',required=True);p.add_argument('--dataset',required=True);p.add_argument('--rights-review');a=p.parse_args()
    root=pathlib.Path(a.corpus);source=pathlib.Path(a.dataset);started=time.monotonic()
    def digest(p):
        h=hashlib.sha256()
        with p.open('rb') as f:
            for _ in range((p.stat().st_size+1048575)//1048576+1):
                if time.monotonic()-started>=30:raise TimeoutError('Binding hash deadline')
                data=f.read(1048576)
                if not data:break
                h.update(data)
        return h.hexdigest()
    rights=(pathlib.Path(a.rights_review) if a.rights_review else source/'RIGHTS_MANIFEST.json').read_bytes();target=root/'source-rights.json'
    with target.open('xb') as f:f.write(rights)
    source_assets=source/'ASSET_MANIFEST.json'
    if source_assets.stat().st_size>16*1024*1024:raise ValueError('Source manifest byte bound')
    with (root/'source-assets.json').open('xb') as f:f.write(source_assets.read_bytes())
    binding={'ManifestSha256':digest(root/'manifest.json'),'ExclusionsSha256':digest(root/'exclusions.jsonl'),'SourceRightsSha256':digest(target),'SourceAssetsSha256':digest(root/'source-assets.json'),'SourceAssetsPath':'source-assets.json','SourceRoot':str(source.resolve()),'FormalTrainingAdmitted':False}
    with (root/'corpus-bindings.json').open('x',encoding='utf-8') as f:json.dump(binding,f,indent=2)
    print('manifest_sha=',binding['ManifestSha256'],'bindings_sha=',digest(root/'corpus-bindings.json'),flush=True)

if __name__=='__main__':main()
