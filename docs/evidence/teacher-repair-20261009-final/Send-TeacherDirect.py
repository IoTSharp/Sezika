import sys, sqlite3, time, json, tomllib, urllib.request, urllib.error, re
from pathlib import Path
from datetime import datetime, timezone

root = Path(__file__).parent
batch, seconds = sys.argv[1], int(sys.argv[2])
if not re.fullmatch(r'(repair|review)(0[0-9]|1[0-3])', batch) or not 1 <= seconds <= 240:
    raise RuntimeError('Bounded repair batch scope differs')
plan = json.loads((root/'plan.json').read_text(encoding='utf-8-sig'))
deadline = datetime.fromisoformat(plan['api_phase_started_utc']).timestamp()+plan['maximum_phase_seconds']
directory = root/batch
wire = (directory/'request.json').read_bytes()
data = json.loads(wire)
if len(wire)>131072 or data['model']!='gpt-6.1-sol' or data['reasoning']['effort']!='max':
    raise RuntimeError('Wire identity/size differs')
class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None

with sqlite3.connect('file:C:/Users/mysti/.cc-switch/cc-switch.db?mode=ro',uri=True,timeout=2) as db:
    before=time.monotonic(); db.set_progress_handler(lambda:int(time.monotonic()-before>5),1000)
    selected=db.execute("select id,name,settings_config from providers where app_type='codex' and is_current=1 limit 2").fetchall()
    if len(selected)!=1 or selected[0][1]!='sonnet_vip': raise RuntimeError('Selected provider differs')
    settings=json.loads(selected[0][2]); config=tomllib.loads(settings['config'])
    provider=config['model_providers'][config['model_provider']]
    if provider['base_url']!='https://sonnet.vip': raise RuntimeError('Selected provider host differs')
    key=settings.get('auth',{}).get('OPENAI_API_KEY') or provider.get('experimental_bearer_token')
    if not key: raise RuntimeError('Selected credential absent')

endpoint='https://sonnet.vip/v1/responses'; started=time.monotonic(); attempts=[]; success=False
try:
    for index in range(2):
        remaining=min(seconds-(time.monotonic()-started),deadline-datetime.now(timezone.utc).timestamp()-2)
        if remaining<2 or (root/'cancel.request').exists(): break
        timeout=min(180 if index==0 else 240,remaining)
        item={'attempt':index+1,'route':'direct' if index==0 else 'proxy7890','started_utc':datetime.now(timezone.utc).isoformat(),'sent':False,'http_status':None,'failure_type':None}
        try:
            proxy={} if index==0 else {'http':'http://127.0.0.1:7890','https':'http://127.0.0.1:7890'}
            opener=urllib.request.build_opener(urllib.request.ProxyHandler(proxy),NoRedirect())
            req=urllib.request.Request(endpoint,data=wire,headers={'Authorization':'Bearer '+key,'Content-Type':'application/json','Idempotency-Key':'sezika-repair-20261009-'+batch},method='POST')
            item['sent']=True
            try: response=opener.open(req,timeout=timeout)
            except urllib.error.HTTPError as error: response=error
            with response:
                item['http_status']=response.status; raw=response.read(2097153)
            if len(raw)>2097152: raise RuntimeError('Response byte cap')
            if key.encode() in raw: raise RuntimeError('Credential reflected; raw bytes not saved')
            with (directory/f'response-attempt{index+1}.json').open('xb') as f: f.write(raw)
            if item['http_status']==200:
                with (directory/'response.json').open('xb') as f: f.write(raw)
                success=True
        except Exception as error:
            item['failure_type']=type(error).__name__
        finally:
            item['finished_utc']=datetime.now(timezone.utc).isoformat();attempts.append(item)
            print(json.dumps(item),flush=True)
        if success: break
finally:
    key=None
    receipt={'endpoint':endpoint,'provider_id':selected[0][0],'credential_source':'current CC Switch selected sonnet_vip provider; memory only','success_http200':success,'attempts':attempts,'maximum_http_requests':2,'maximum_transport_seconds':seconds,'automatic_redirects':False,'failed_attempt_upstream_billing':'unknown'}
    with (directory/'transport-result.json').open('x',encoding='utf-8') as f: json.dump(receipt,f,indent=2)
sys.exit(0 if success else 1)
