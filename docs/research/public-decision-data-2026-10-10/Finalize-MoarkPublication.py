"""Publish a receipt for a verified ancestor; verify only the added metadata."""
import hashlib
import json
import os
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
import psutil

root = Path(r'D:\source\Sezika\.artifacts\public-decision-data-20261010')
research = Path(__file__).resolve().parent
data_root = Path(r'C:\Users\mysti\Documents\SezikaData\public-decision-2026-10-10')
repo = data_root / 'upload/moark-git-repository'
anonymous = data_root / 'upload/moark-anonymous-verification-v2'
url = 'https://moark.com/maikebing/public-decision-data'
ready = json.loads((root / 'publish-prepare-v2.json').read_text(encoding='utf8'))
proof = json.loads((root / 'verify-v2.json').read_text(encoding='utf8'))
started = time.monotonic()
state = {'pid': os.getpid(), 'parent_pid': os.getppid(), 'max_seconds': 240,
         'max_commands': 18, 'commands': [], 'secret_output': False}
git = r'C:\Program Files\Git\cmd\git.exe'
helper = '!"' + sys.executable.replace('\\', '/') + '" "' + str(root / 'dataset_git_credential.py').replace('\\', '/') + '"'
env = {k:v for k,v in os.environ.items() if not k.startswith('GIT_TRACE') and k != 'GIT_CURL_VERBOSE'}
env.update(GIT_TERMINAL_PROMPT='0', GIT_LFS_SKIP_SMUDGE='1')

def check():
    if time.monotonic()-started >= 230 or (data_root/'CANCEL').exists():
        raise TimeoutError('Final receipt deadline or cancellation')

def run(args, public=False, timeout=35):
    check()
    if len(state['commands']) >= 18:
        raise TimeoutError('Final receipt command boundary')
    child_env = env.copy()
    argv = [git, '-c', 'credential.helper=', '-c', 'core.autocrlf=false',
            '-c', 'lfs.standalonetransferagent=', '-c', 'lfs.basictransfersonly=true',
            '-c', 'lfs.concurrenttransfers=1', '-c', 'lfs.transfer.maxretries=0']
    if public:
        child_env.update(GIT_CONFIG_NOSYSTEM='1', GIT_CONFIG_GLOBAL=os.devnull,
                         GIT_CONFIG_SYSTEM=os.devnull)
        argv += ['-c', 'core.askPass=', '-c', 'http.extraHeader=']
    else:
        argv += ['-c', 'credential.helper='+helper, '-c', 'credential.useHttpPath=true']
    child = subprocess.Popen(argv+args, cwd=anonymous if public else repo,
                             env=child_env, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    p=psutil.Process(child.pid)
    row={'args': args, 'anonymous':public, 'pid':p.pid, 'created':p.create_time(),
         'argv':p.cmdline(), 'parent_pid':p.ppid()}
    state['commands'].append(row)
    (root/'publication-finalize-running.json').write_text(json.dumps(state,indent=2),encoding='utf8')
    try:
        out,err=child.communicate(timeout=min(timeout,max(1,230-(time.monotonic()-started))))
    except subprocess.TimeoutExpired:
        child.kill();child.wait(timeout=4)
        raise TimeoutError('Final receipt Git timeout; guardian owns descendant cleanup')
    row.update(exit_code=child.returncode,stdout_sha256=hashlib.sha256(out).hexdigest(),
               stderr=err.decode('utf8',errors='replace')[-2000:])
    print(json.dumps({'command':args,'anonymous':public,'exit_code':child.returncode}),flush=True)
    if child.returncode:
        raise RuntimeError('Final receipt Git command failed')
    return out

try:
    if proof.get('status') != 'PASS_anonymous_all_assets_full_content_sha256' or proof.get('revision') != ready['revision']:
        raise ValueError('Missing independent full-content verification')
    if run(['rev-parse','HEAD']).decode().strip() != ready['revision'] or run(['status','--porcelain']).strip():
        raise ValueError('Verified publication checkout differs')
    if run(['config','--get','remote.origin.url']).decode().strip() != url:
        raise ValueError('Publication remote differs')
    if not run(['ls-remote','origin','refs/heads/master']).decode().startswith(ready['revision']+'\t'):
        raise ValueError('Verified remote commit differs')
    manifest_bytes=(repo/'ASSET_MANIFEST.json').read_bytes()
    if hashlib.sha256(manifest_bytes).hexdigest() != proof['manifest_sha256']:
        raise ValueError('Verified asset manifest differs')
    assets=json.loads(manifest_bytes.decode('utf-8-sig'))
    receipt={'platform':'Moark','namespace':'maikebing','repository_url':url,
             'dataset_page':'https://moark.com/datasets/maikebing/public-decision-data',
             'published':True,'round_trip_verified':True,'verified_data_commit':ready['revision'],
             'verified_utc':datetime.now(timezone.utc).isoformat(),
             'asset_manifest_sha256':proof['manifest_sha256'],
             'source_repositories':sorted({r['dataset'] for r in assets}),
             'asset_files':proof['asset_files'],'asset_bytes':proof['asset_bytes'],
             'lfs_paths':proof['lfs_objects'],'missing_lfs_objects':0,
             'verification':'Anonymous Git clone, exact path set, manifest and metadata; every native asset and every LFS path checked against full-content SHA256 and byte length.',
             'download_method':'Git clone plus standard Git LFS; no account token required.',
             'formal_training_admitted':False,
             'whole_local_collection_published':False,
             'scope':'Reviewed sources only; preserve source licenses, splits and family overlap boundaries. Physical rows are not unique training examples.'}
    encoded=(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n').encode('utf8')
    (repo/'MOARK_PUBLICATION.json').write_bytes(encoded)
    run(['diff','--check','--','MOARK_PUBLICATION.json'])
    run(['add','--','MOARK_PUBLICATION.json'])
    run(['commit','-m','Record anonymous full-content verification of public decision assets','--','MOARK_PUBLICATION.json'])
    final_revision=run(['rev-parse','HEAD']).decode().strip()
    run(['push','origin','master'],timeout=75)
    if not run(['ls-remote','origin','refs/heads/master']).decode().startswith(final_revision+'\t'):
        raise ValueError('Final receipt remote ref differs')
    run(['fetch','--depth','2','origin','master'],public=True,timeout=50)
    if run(['rev-parse','FETCH_HEAD'],public=True).decode().strip() != final_revision:
        raise ValueError('Anonymous final receipt ref differs')
    if run(['rev-parse',final_revision+'^'],public=True).decode().strip() != ready['revision']:
        raise ValueError('Anonymous final receipt parent differs')
    changed=run(['diff','--name-only',ready['revision'],final_revision],public=True).decode().splitlines()
    if changed != ['MOARK_PUBLICATION.json']:
        raise ValueError('Final receipt unexpectedly changed data or other metadata')
    if run(['show',final_revision+':MOARK_PUBLICATION.json'],public=True) != encoded:
        raise ValueError('Anonymous public receipt content differs')
    if hashlib.sha256(run(['show',final_revision+':ASSET_MANIFEST.json'],public=True)).hexdigest() != proof['manifest_sha256']:
        raise ValueError('Anonymous final manifest differs')
    receipt['publication_commit']=final_revision
    receipt['publication_receipt_anonymous_readback']='PASS; receipt-only child of verified data commit'
    # Keep the task-owned checkout on the independently verified transfer path.
    run(['config','lfs.standalonetransferagent',''])
    run(['config','lfs.basictransfersonly','true'])
    (research/'publication-receipt.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    (data_root/'upload/moark-public-decision-v2/MOARK_PUBLICATION.json').write_bytes(encoded)
    state.update(status='PASS_verified_publication_and_receipt',publication_commit=final_revision,
                 data_commit=ready['revision'],asset_files=proof['asset_files'],asset_bytes=proof['asset_bytes'],sources=len(receipt['source_repositories']))
except Exception as e:
    state.update(status='FAIL_or_PARTIAL',error_type=type(e).__name__,error=str(e)[:1000])
finally:
    state['elapsed_seconds']=time.monotonic()-started
    (root/'publication-finalize.json').write_text(json.dumps(state,indent=2),encoding='utf8')
print(json.dumps({k:state.get(k) for k in ['status','publication_commit','data_commit','sources','asset_files','asset_bytes','error','elapsed_seconds']}),flush=True)
raise SystemExit(0 if state['status'].startswith('PASS') else 1)
