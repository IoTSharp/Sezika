"""Inspect four frozen candidate sources; keep schemas and labels private.

This is an offline adapter review, not a training conversion or permission gate.
"""
import argparse, hashlib, json, pathlib, subprocess, time
from bounded_start import wait_for_guardian


SOURCES = ('allenai/ai2_arc', 'allenai/openbookqa', 'C-MTEB/AFQMC', 'C-MTEB/LCQMC')


def candidate(row, source):
    if source in ('allenai/ai2_arc', 'allenai/openbookqa'):
        labels = [str(x) for x in row['choices']['label']]
        texts = row['choices']['text']
        answer = str(row['answerKey'])
        if not 2 <= len(labels) <= 32 or len(labels) != len(texts) or len(set(labels)) != len(labels) or answer not in labels:
            raise ValueError('Invalid choice identity/answer')
        question = row['question'] if source == 'allenai/ai2_arc' else row['question_stem']
        state = question
        if source == 'allenai/openbookqa' and row.get('fact1'):
            state = 'Provided fact: ' + row['fact1'] + '\nQuestion: ' + question
        return {'State': state, 'Instruction': 'Choose the correct answer to the question.',
                'Type': 'Choice', 'Candidates': [{'Id': key, 'Text': text} for key, text in zip(labels, texts)],
                'Target': [float(x == answer) for x in labels], 'Language': 'en',
                'Family': str(row['id']), 'LabelKind': 'original_training_answer_key'}
    if source in ('C-MTEB/AFQMC', 'C-MTEB/LCQMC'):
        if 'label' in row:
            label = row['label']
        else:
            label = row['score']
        if not isinstance(label, (int, float)) or label not in (0, 1):
            raise ValueError('Binary gold label expected; unlabelled heldout remains excluded')
        state = '句子一：' + row['sentence1'] + '\n句子二：' + row['sentence2']
        # Sentence-pair identity is symmetric for these equivalence tasks.
        pair = sorted([row['sentence1'], row['sentence2']])
        return {'State': state, 'Instruction': '这两句话是否表达相同的意图或语义？', 'Type': 'Boolean',
                'Candidates': [{'Id': 'false', 'Text': '否'}, {'Id': 'true', 'Text': '是'}],
                'Target': [float(label == 0), float(label == 1)], 'Language': 'zh',
                'Family': hashlib.sha256(json.dumps(pair, ensure_ascii=False).encode('utf-8')).hexdigest(),
                'LabelKind': 'original_binary_equivalence_annotation'}
    raise ValueError('Unsupported review source')


def self_test():
    base = {'id':'x','question':'q','choices':{'label':['2','1'],'text':['two','one']},'answerKey':1}
    assert candidate(base, 'allenai/ai2_arc')['Target'] == [0.0, 1.0]
    base['answerKey'] = 'unknown'
    try:
        candidate(base, 'allenai/ai2_arc')
    except ValueError:
        pass
    else:
        raise AssertionError('Unmatched answer accepted')
    a = {'sentence1':'甲','sentence2':'乙','label':0}
    b = {'sentence1':'乙','sentence2':'甲','score':1}
    assert candidate(a, 'C-MTEB/AFQMC')['Target'] == [1.0,0.0]
    assert candidate(b, 'C-MTEB/LCQMC')['Target'] == [0.0,1.0]
    assert candidate(a, 'C-MTEB/AFQMC')['Family'] == candidate(b, 'C-MTEB/LCQMC')['Family']
    b['score'] = -1
    try:
        candidate(b, 'C-MTEB/LCQMC')
    except ValueError:
        pass
    else:
        raise AssertionError('Unlabelled heldout accepted')


def main():
    wait_for_guardian()
    ap = argparse.ArgumentParser()
    ap.add_argument('--inventory', required=True)
    ap.add_argument('--catalog', required=True)
    ap.add_argument('--decoder', required=True)
    ap.add_argument('--output', required=True)
    ap.add_argument('--seconds', type=int, default=180)
    ap.add_argument('--cancel')
    a = ap.parse_args()
    if not 1 <= a.seconds <= 240:
        raise ValueError('Explicit review deadline required')
    start = time.monotonic()
    def check():
        if time.monotonic() - start >= a.seconds or (a.cancel and pathlib.Path(a.cancel).exists()):
            raise TimeoutError('Review deadline/cancel')
    self_test()
    inventory_path = pathlib.Path(a.inventory)
    if inventory_path.stat().st_size > 8 * 1024 * 1024:
        raise ValueError('Inventory bytes exceeded')
    inventory = json.loads(inventory_path.read_text(encoding='utf-8-sig'))
    catalog = json.loads(pathlib.Path(a.catalog).read_text(encoding='utf-8-sig'))
    if len(inventory) > 5000 or len(catalog) > 128:
        raise ValueError('Inventory/source item bound exceeded')
    output = pathlib.Path(a.output).resolve()
    output.mkdir(parents=True, exist_ok=False)
    reviews = []
    for source in SOURCES:
        check()
        item = next(x for x in catalog if x['id'] == source)
        train_assets = [x for x in inventory if x.get('dataset') == source and x.get('split_hint') == 'source-train' and x['path'].endswith('.parquet')]
        if not 1 <= len(train_assets) <= 8:
            raise ValueError('Review train asset item bound exceeded')
        card_assets = [x for x in inventory if x.get('dataset') == source and x['path'].endswith('/README.md'.replace('/', '\\'))]
        if not card_assets:
            card_assets = [x for x in inventory if x.get('dataset') == source and pathlib.Path(x['path']).name == 'README.md']
        samples, assets = [], []
        for asset in train_assets:
            check()
            path = pathlib.Path(asset['path'])
            if path.stat().st_size > 100 * 1024 * 1024:
                raise ValueError('Per-asset sample review bytes bound exceeded')
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            if digest != asset['sha256']:
                raise ValueError('Frozen source SHA differs')
            command = ['C:/Program Files/dotnet/dotnet.exe', a.decoder, 'export-json', str(path), '-', '10', '20']
            proc = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            try:
                stdout, stderr = proc.communicate(timeout=min(30, max(1,a.seconds - (time.monotonic()-start))))
                if proc.returncode != 0:
                    raise RuntimeError('Decoder failed: ' + stderr.decode('utf-8-sig', errors='replace')[-2000:])
                if len(stdout) > 4 * 1024 * 1024 or len(stderr) > 1024 * 1024:
                    raise ValueError('Sample decoder output bytes exceeded')
            finally:
                if proc.poll() is None:
                    proc.terminate()
                    try:
                        proc.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        proc.kill(); proc.wait(timeout=5)
                proc.stdout.close();proc.stderr.close()
            decoded = [json.loads(line) for line in stdout.decode('utf-8-sig').splitlines()]
            if len(decoded) > 10:
                raise ValueError('Sample row item bound exceeded')
            accepted, rejected = [], []
            for number, row in enumerate(decoded, start=1):
                check()
                try:
                    accepted.append({'row':number,'canonical_proposal':candidate(row, source)})
                except (ValueError, KeyError, TypeError) as error:
                    rejected.append({'row':number,'error':str(error)})
            samples.append({'path':str(path),'schema_fields':list(decoded[0]) if decoded else [],'raw_first_sample':decoded[0] if decoded else None,
                            'sample_rows':len(decoded),'accepted':accepted,'rejected':rejected})
            assets.append({k:asset.get(k) for k in ('path','sha256','source_revision','source_url','rows','split_hint')})
            print(source, pathlib.Path(asset['path']).name, 'sample=',len(decoded),'accepted=',len(accepted),'rejected=',len(rejected),flush=True)
        reviews.append({'source':source,'revision':item['revision'],'card_license':item.get('license'),'card_evidence':card_assets,
                        'train_assets':assets,'samples':samples,'formal_training_admitted':False,
                        'rights_status':'declared_cc_by_sa_4_0_requires_attribution_sharealike_derivation_review' if source=='allenai/ai2_arc' else 'unresolved_original_or_component_rights',
                        'unique_question_count':None,'self_tests_passed':True})
    (output/'review.json').write_text(json.dumps({'sources':reviews,'self_tests_passed':True,'review_rows_per_asset':10,'formal_training_admitted':False},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('Reviewed sources:',len(reviews),'self-tests=passed; no data downloaded or trained',flush=True)


if __name__ == '__main__':
    main()
