"""Independently audit the bounded Score teacher pilot without reading credentials."""
import argparse, collections, hashlib, json, pathlib, time
from bounded_start import wait_for_guardian


def main():
    wait_for_guardian(); ap=argparse.ArgumentParser();ap.add_argument('--task-root',required=True);ap.add_argument('--output',required=True);a=ap.parse_args()
    task=pathlib.Path(a.task_root).resolve();started=time.monotonic();deadline=started+30;cancel=task/'teacher-score-pilot-v2'/'cancel.request'
    def check():
        if time.monotonic()>=deadline or cancel.exists():raise TimeoutError('Receipt deadline/cancel')
    def data(path):
        check(); assert path.resolve().is_relative_to(task) and path.stat().st_size<=3*1024*1024
        return json.loads(path.read_text(encoding='utf-8'))
    teacher=task/'teacher-score-pilot-v2';queue=task/'teacher-queue-v2';rows=data(queue/'pilot-questions.json');gold=data(queue/'pilot-reference-private.json');report=data(teacher/'report.json');input_identity=data(task/'teacher-score-call-input-identity-v2.json')
    assert len(rows)==len(gold)==24 and report['validated']==24 and report['maximum_http_requests']==10
    expected={r['RecordId']:r for r in rows};assert len(expected)==24 and all(r['Type']=='Score' for r in rows)
    assert hashlib.sha256((queue/'manifest.json').read_bytes()).hexdigest()==input_identity['queue_manifest_sha256']
    labels=[];request_ids=set();response_models=collections.Counter();files=[]
    for batch_index in range(5):
        check();directory=teacher/f'batch-{batch_index:02}';request_path=directory/'request.json';response_path=directory/'response.json';request=data(request_path);response=data(response_path);validated=data(directory/'validated-labels.json')
        visible=json.loads(request['input'][1]['content'])['questions']
        assert request['model']=='gpt-6.1-sol' and request['reasoning']['effort']=='max' and request['store'] is False
        assert all(set(r)=={'RecordId','State','Instruction','Type','Candidates'} for r in visible)
        assert {r['RecordId'] for r in visible}=={r['RecordId'] for r in validated}
        request_sha=hashlib.sha256(request_path.read_bytes()).hexdigest();response_sha=hashlib.sha256(response_path.read_bytes()).hexdigest()
        for r in validated:
            check();rid=r['RecordId'];assert rid in expected and rid not in request_ids;request_ids.add(rid)
            assert r['RequestSha256']==request_sha and r['ResponseSha256']==response_sha
            assert r['State']==expected[rid]['State'] and r['Candidates']==expected[rid]['Candidates'] and r['Type']=='Score'
            assert [c['Text'] for c in r['Candidates']]==['0','1','2','3','4']
            reference=gold[rid];assert r['TeacherAnswerable']==reference['answerable'] and r['ReferenceAgreement'] is True
            if r['TeacherAnswerable']:
                assert len(r['Target'])==5 and abs(sum(r['Target'])-1)<1e-6
                assert max(range(5),key=r['Target'].__getitem__)==max(range(5),key=reference['Target'].__getitem__)
                assert r['TrainingAdmission']=='experimental_candidate'
            else:
                assert r['Target'] is None and reference['Target'] is None and r['TrainingAdmission']=='excluded_abstention_or_reference_disagreement'
            response_models[str(r['GatewayReportedModel'])]+=1;labels.append(r)
        batch_files=list(directory.iterdir());assert len(batch_files)<=8
        for path in batch_files:
            check();assert path.is_file() and path.stat().st_size<=3*1024*1024
            files.append({'path':str(path.relative_to(task)),'bytes':path.stat().st_size,'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
    assert request_ids==set(expected) and len(labels)==24 and sum(r['TeacherAnswerable'] for r in labels)==21
    assert len(report['attempts'])==5 and all(r['status']==200 and r['route']=='direct' for r in report['attempts'])
    actual_source=pathlib.Path(__file__).resolve().parent/'collect_teacher.py'
    assert hashlib.sha256(actual_source.read_bytes()).hexdigest()==input_identity['script_sha256']
    receipt={'status':'PASS_independent_Score_teacher_request_response_audit','requested':24,'validated':24,'answerable':21,'abstained':3,'reference_agreement':24,
             'training_candidates':21,'http_requests':5,'failed_http_attempts':0,'visible_request_fields':['RecordId','State','Instruction','Type','Candidates'],
             'reference_hidden_from_request':True,'request_and_response_sha_verified':True,'collector_source_sha256':input_identity['script_sha256'],
             'gateway_reported_models':dict(response_models),'teacher_identity_semantics':'gateway declaration only; not independent upstream attestation',
             'reference_agreement_semantics':'answerability equality; answerable cases use ordinal argmax equality; not measured student accuracy or probability calibration',
             'formal_training_admitted':False,'measured_student_accuracy':None,'calibrated':False,'elapsed_seconds':time.monotonic()-started,'files':files}
    output=pathlib.Path(a.output).resolve()
    with output.open('x',encoding='utf-8') as f:json.dump(receipt,f,ensure_ascii=False,indent=2)
    print(json.dumps({k:v for k,v in receipt.items() if k!='files'},ensure_ascii=False),flush=True)


if __name__=='__main__':main()
