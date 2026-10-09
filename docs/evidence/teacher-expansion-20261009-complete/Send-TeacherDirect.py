import sys, sqlite3, time, json, tomllib, urllib.request, urllib.error
from pathlib import Path
from datetime import datetime, timezone

root=Path(__file__).parent
class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None
batch=sys.argv[1]; seconds=int(sys.argv[2]); directory=root/batch
if batch not in ['batch%02d'%n for n in range(7,33)]+['recovery01','recovery02','retry03','retry04','retry05','retry06','final05','final06','final05v2','final06v2'] or not 1<=seconds<=240:raise RuntimeError('Scope differs')
selection_name=sys.argv[3] if len(sys.argv)==4 else 'model-selection-recovery-20261009.json'
if selection_name not in ['model-selection-recovery-20261009.json','model-selection-final16-20261009.json']:raise RuntimeError('Selection differs')
selection=json.loads((root/selection_name).read_text(encoding='utf-8-sig'))
if batch in ['final05','final06','final05v2','final06v2']:
    if selection_name!='model-selection-final16-20261009.json' or selection['maximum_requests']!=2 or selection['maximum_phase_seconds']!=600:raise RuntimeError('Final16 scope differs')
elif selection_name!='model-selection-recovery-20261009.json':raise RuntimeError('Recovery selection differs')
phase=(datetime.now(timezone.utc)-datetime.fromisoformat(selection['api_phase_started_utc'])).total_seconds()
if phase+seconds>selection['maximum_phase_seconds'] or (root/'cancel.request').exists():raise RuntimeError('Phase deadline/cancellation')
wire=(directory/'request.json').read_bytes();request_data=json.loads(wire)
if request_data['model']!='gpt-6.1-sol' or request_data['reasoning']['effort']!='max':raise RuntimeError('Request identity differs')
with sqlite3.connect('file:C:/Users/mysti/.cc-switch/cc-switch.db?mode=ro',uri=True,timeout=2) as db:
    before=time.monotonic();db.set_progress_handler(lambda:int(time.monotonic()-before>5),1000)
    rows=db.execute("select id,name,settings_config from providers where app_type='codex' and is_current=1 limit 2").fetchall()
    if len(rows)!=1 or rows[0][1]!='sonnet_vip':raise RuntimeError('Selected provider differs')
    settings=json.loads(rows[0][2]);config=tomllib.loads(settings['config']);provider=config['model_providers'][config['model_provider']]
    if provider['base_url']!='https://sonnet.vip':raise RuntimeError('Selected host differs')
    key=settings.get('auth',{}).get('OPENAI_API_KEY') or provider.get('experimental_bearer_token')
    if not key:raise RuntimeError('Selected credential absent')
endpoint='https://sonnet.vip/v1/responses';status=None;failure=None;sent=False
try:
    opener=urllib.request.build_opener(urllib.request.ProxyHandler({}),NoRedirect())
    request=urllib.request.Request(endpoint,data=wire,headers={'Authorization':'Bearer '+key,'Content-Type':'application/json','Idempotency-Key':'sezika-teacher-expansion-20261008-'+batch},method='POST')
    sent=True
    try:
        response=opener.open(request,timeout=seconds)
    except urllib.error.HTTPError as error:response=error
    with response:
        status=response.status; raw=response.read(2*1024*1024+1)
    if len(raw)>2*1024*1024:raise RuntimeError('Response byte cap')
    if key.encode() in raw:raise RuntimeError('Credential present in response; not saved')
    with (directory/'response.json').open('xb') as handle:handle.write(raw)
except Exception as error:
    failure=type(error).__name__
finally:
    key=None
    receipt={'http_status':status,'failure_type':failure,'request_sent':sent,'endpoint':endpoint,'provider_id':rows[0][0],'credential_source':'current CC Switch selected sonnet_vip provider; memory only','maximum_http_requests':1,'maximum_seconds':seconds,'direct_no_proxy':True}
    with (directory/'transport-result.json').open('x',encoding='utf-8') as handle:json.dump(receipt,handle,indent=2)
    print(json.dumps(receipt))
sys.exit(0 if failure is None else 1)
