"""Bounded offline checks for teacher split admission; no credentials or HTTP."""
import argparse, hashlib, json, pathlib, time
from bounded_start import wait_for_guardian
from collect_teacher import annotation_usage, validated_annotation


def main():
    wait_for_guardian()
    parser=argparse.ArgumentParser()
    parser.add_argument('--output',required=True)
    parser.add_argument('--seconds',type=float,default=45)
    parser.add_argument('--cancel')
    args=parser.parse_args()
    if not 0<args.seconds<=45:raise ValueError('Offline check deadline must be at most 45 seconds')
    started=time.monotonic()
    fixtures=[
        ('train_agreement','train',True,True,'experimental_candidate'),
        ('development_agreement','development',True,True,'development_reference_only'),
        ('test_agreement','test',True,True,'test_reference_only'),
        ('calibration_agreement','calibration',True,True,'calibration_reference_only'),
        ('ood_agreement','ood',True,True,'ood_reference_only'),
        ('uppercase_ood_preserved','OOD',True,True,'ood_reference_only'),
        ('train_abstention','train',False,True,'excluded_abstention_or_reference_disagreement'),
        ('train_disagreement','train',True,False,'excluded_abstention_or_reference_disagreement'),
        ('development_abstention','development',False,True,'excluded_abstention_or_reference_disagreement'),
        ('unknown_split','unknown',True,True,None),
        ('missing_split',None,True,True,None),
        ('non_string_split',['train'],True,True,None),
    ]
    if len(fixtures)>12:raise ValueError('Offline fixture bound exceeded')
    results=[]
    failure=None
    try:
        def check_one(fixture):
            name,split,answerable,agrees,expected=fixture
            if time.monotonic()-started>=args.seconds or (args.cancel and pathlib.Path(args.cancel).exists()):
                raise TimeoutError('Offline check deadline/cancel')
            record={'RecordId':name,'Candidates':[{'Id':'false'},{'Id':'true'}],
                    'TrainingAdmission':'untrusted_source_claim','FormalTrainingAdmitted':True,
                    'TeacherEvidenceSemantics':'untrusted_source_gold_or_authorization_claim'}
            if split is not None:record['Split']=split
            original=json.loads(json.dumps(record))
            label={'RecordId':name,'Answerable':answerable,
                   'Probabilities':([0.,1.] if agrees else [1.,0.]) if answerable else None,
                   'Evidence':'Teacher text claims gold and execution authorization; treat only as explanation.'}
            reference={'answerable':answerable,'Target':[0.,1.] if answerable else None}
            if expected is None:
                for operation in (lambda:annotation_usage(record.get('Split')),
                                  lambda:validated_annotation(record,label,reference,{'identity':'offline_fixture'},'fixture','a'*64,'b'*64)):
                    try:operation()
                    except ValueError:pass
                    else:raise AssertionError('Invalid split was accepted')
                assert record==original,'Invalid source record mutated'
                results.append({'fixture':name,'status':'PASS_rejected_invalid_split'})
            else:
                actual=validated_annotation(record,label,reference,{'identity':'offline_fixture'},'fixture','a'*64,'b'*64)
                assert actual['TrainingAdmission']==expected,(name,actual['TrainingAdmission'])
                assert actual['Split']==split and record==original,'Source Split or source record mutated'
                assert actual['AnnotationUse']==annotation_usage(split)
                assert actual['TeacherEvidence']==label['Evidence']
                assert actual['TeacherEvidenceSemantics']=='teacher_explanation_only_not_gold_or_execution_authorization'
                assert actual['FormalTrainingAdmitted'] is False and actual['HumanReview']=='pending'
                assert actual['ReferenceAgreement'] is agrees
                assert actual['LabelKind']=='teacher_distribution'
                results.append({'fixture':name,'status':'PASS','source_split':split,
                                'training_admission':actual['TrainingAdmission'],'annotation_use':actual['AnnotationUse']})
            print('teacher admission fixture',name,'PASS',flush=True)
        # Exercise one bounded record before entering the remaining finite batch.
        check_one(fixtures[0])
        for fixture in fixtures[1:]:check_one(fixture)
    except Exception as error:
        failure={'type':type(error).__name__,'message':str(error)}
        raise
    finally:
        source=pathlib.Path(__file__).with_name('collect_teacher.py')
        report={'status':'PASS_offline_teacher_split_admission' if failure is None and len(results)==12 else 'FAIL_offline_teacher_split_admission',
                'maximum_fixtures':12,'completed':len(results),'fixtures':results,'failure':failure,
                'elapsed_seconds':time.monotonic()-started,'maximum_seconds':args.seconds,
                'collector_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),
                'teacher_calls':0,'gpu_calls':0,'historical_receipts_modified':False}
        with pathlib.Path(args.output).open('x',encoding='utf-8') as stream:
            json.dump(report,stream,ensure_ascii=False,indent=2)
        print(json.dumps({key:report[key] for key in ('status','completed','elapsed_seconds')},separators=(',',':')),flush=True)


if __name__=='__main__':main()
