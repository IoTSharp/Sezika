"""Independent fact-based checks for queue semantics, ordinals and family splits."""
import argparse, collections, datetime, hashlib, json, math, pathlib, time
from bounded_start import wait_for_guardian
from question_templates import TEMPLATES, VARIANTS, specification
from question_queue import score_pilot, make_record
from prepare_corpus import compact, sha


def independent_indices(template, variant, state):
    complete = variant != 'missing'
    if template == 'date_order':
        dates = {e['event']: e.get('date') for e in state['events']}
        gap = None if not all(dates.values()) else (datetime.date.fromisoformat(dates['B']) - datetime.date.fromisoformat(dates['A'])).days
        choice = 3 if gap is None else 0 if gap > 0 else 1 if gap < 0 else 2
        boolean = None if gap is None else gap > 0
        score = None if gap is None else next(i for i, limit in enumerate((0, 2, 6, 13, 10000)) if gap <= limit)
    elif template == 'relation_chain':
        complete = state['edge_list_complete']; graph = collections.defaultdict(list)
        for edge in state['edges']:
            graph[edge['before']].append(edge['after'])
        todo = [(state['start'], 0, {state['start']})]; longest = 0
        for _ in range(64):
            if not todo: break
            current, hops, visited = todo.pop()
            if current == state['end']: longest = max(longest, hops)
            for neighbor in graph[current]:
                if neighbor not in visited: todo.append((neighbor, hops + 1, visited | {neighbor}))
        else: raise AssertionError('Independent graph traversal bound')
        choice = 2 if not complete else 0 if longest else 1
        boolean = None if not complete else bool(longest)
        score = None if not complete else min(4, longest)
    elif template == 'evidence_conflict':
        complete = state['observation_list_complete']; statuses = [o['status'] for o in state['observations']]
        assert len({o['source'] for o in state['observations']}) == len(statuses)
        active = statuses.count('active'); inactive = statuses.count('inactive')
        choice = 3 if not complete else 0 if active == len(statuses) else 1 if inactive == len(statuses) else 2
        boolean = None if not complete or active and inactive else bool(active)
        score = min(4, active) if complete else None
    elif template == 'tool_routing':
        complete = state['request_complete']; routes = {r['intent']: r['tool'] for r in state['routes']}
        actual = routes.get(state['request'].get('intent'))
        choice = 4 if not complete else ['lookup_record','reserve_slot','cancel_slot','manual_review'].index(actual)
        boolean = None if not complete else actual == 'reserve_slot'
        score = None if not complete else len(set(state['required_fields']) & set(state['request']['fields']))
    elif template == 'budget_arithmetic':
        quantities = {q['name']: q.get('value') for q in state['quantities']}
        complete = all(q is not None for q in quantities.values()); margin = quantities['budget'] - quantities['cost'] if complete else None
        choice = 2 if not complete else 0 if margin >= 0 else 1
        boolean = margin >= 0 if complete else None
        score = None if not complete else 0 if margin < 0 else 1 if margin == 0 else next(i for i, limit in ((2,5),(3,10),(4,100000)) if margin <= limit)
    elif template == 'schedule_overlap':
        complete = all('start' in i and 'end' in i for i in state['intervals'])
        overlap = max(0, min(i['end'] for i in state['intervals']) - max(i['start'] for i in state['intervals'])) if complete else None
        choice = 2 if not complete else 0 if overlap else 1
        boolean = bool(overlap) if complete else None
        score = None if not complete else next(i for i, limit in enumerate((0,14,29,59,10000)) if overlap <= limit)
    elif template == 'document_evidence':
        complete = state['document_complete']; facts = {f['field']: f['value'] for f in state['document']}
        choice = 2 if not complete else ['Mira','Nico'].index(facts['owner'])
        boolean = None if not complete else facts['owner'] == 'Mira'
        score = None if not complete else sum(facts.get(k) == v for k,v in state['required_propositions'].items())
    else: raise AssertionError('Unknown independent evaluator')
    if variant == 'negated' and boolean is not None: boolean = not boolean
    return {'Choice': choice, 'Boolean': None if boolean is None else int(boolean), 'Score': score}


def check_templates(deadline, cancel):
    checked = 0
    for template in TEMPLATES:
        for seed in range(40):
            for variant in VARIANTS:
                targets = []
                for language in ('en','zh'):
                    if time.monotonic() >= deadline or cancel.exists(): raise TimeoutError('Semantic check deadline/cancel')
                    spec = specification(template, seed, language, variant)
                    expected = independent_indices(template, variant, spec['state'])
                    for kind in ('Choice','Boolean','Score'):
                        assert spec[kind]['index'] == expected[kind], (template,seed,variant,language,kind,spec[kind]['index'],expected[kind])
                        checked += 1
                    targets.append(expected)
                    assert spec['Score']['options'] == ['0','1','2','3','4']
                    item = spec['Choice']; n = len(item['options']); offset = 1 + seed % (n - 1)
                    permuted = item['options'][offset:] + item['options'][:offset]
                    assert permuted[(item['index'] - offset) % n] == item['options'][item['index']]
                assert targets[0] == targets[1], 'Translations changed the explicit decision'
    # Three semantic edge cases that would otherwise be easy to silently mishandle.
    equal = specification('date_order',1,'en','base'); assert equal['Choice']['index'] == 2 and equal['Score']['index'] == 0
    touching = specification('schedule_overlap',0,'en','base'); assert touching['Boolean']['index'] == 0 and touching['Score']['index'] == 0
    conflict = specification('evidence_conflict',2,'en','base'); assert conflict['Choice']['index'] == 2 and conflict['Boolean']['index'] is None
    return checked


def check_queue(root, deadline, cancel):
    manifest = json.loads((root / 'manifest.json').read_text(encoding='utf-8')); references = {}; ids = set(); families = {}; counts = collections.Counter(); approximate_distributions = 0; maximum_sum_error = 0.0
    with (root / 'reference-gold.jsonl').open(encoding='utf-8') as f:
        for index,line in enumerate(f):
            if index >= 30000 or time.monotonic() >= deadline or cancel.exists(): raise TimeoutError('Reference bound')
            reference = json.loads(line); assert reference['RecordId'] not in references; references[reference['RecordId']] = reference
    with (root / 'questions.jsonl').open(encoding='utf-8') as f:
        for index,line in enumerate(f):
            if index >= 30000 or time.monotonic() >= deadline or cancel.exists(): raise TimeoutError('Queue bound')
            row = json.loads(line); rid = row['RecordId']; assert rid not in ids; ids.add(rid)
            assert 'Target' not in row and 'gold' not in row
            reference = references[rid]; target = reference['Target']
            assert reference['answerable'] == (target is not None)
            if target is not None:
                assert len(target) == len(row['Candidates']) and all(math.isfinite(v) and v >= 0 for v in target), (rid,'target_dimensions_or_values')
                error = abs(sum(target)-1); maximum_sum_error = max(maximum_sum_error,error)
                # Preserve upstream serialized distributions under the same explicit
                # 2e-5 numeric contract as prepare_corpus.probability; do not relabel.
                assert error <= 2e-5, (rid,'target_sum_outside_canonical_tolerance',error)
                if error >= 1e-6: approximate_distributions += 1
            if row['Type'] == 'Score' and row['Source'] == 'owned_synthetic_explicit_rules_v2': assert [c['Text'] for c in row['Candidates']] == ['0','1','2','3','4']
            if row['Type'] == 'Score': assert 2 <= len(row['Candidates']) <= 10
            if row['Type'] == 'Boolean': assert [c['Id'] for c in row['Candidates']] == ['false','true']
            if row['Family'] in families: assert families[row['Family']] == row['Split'], 'Family crossed split'
            families[row['Family']] = row['Split']; counts[row['Template']] += 1
            if row['Source'] == 'owned_synthetic_explicit_rules_v2':
                state = json.loads(row['State']); seed = int(state['case'].rsplit('-',1)[1])
                expected_family = sha(f"owned-synthetic-v1:{row['Domain']}:{seed}") if row['Template'] == 'threshold_rule' else sha(f"owned-synthetic-v2:{row['Template']}:{seed}")
                assert expected_family == row['Family']
                assert row['Split'] == ('development' if int(row['Family'][:8],16) % 10 == 0 else 'train')
    assert ids == set(references) and len(ids) == manifest['total_questions'] and len(families) == manifest['unique_families']
    assert dict(counts) == manifest['counts']['template']
    if manifest['schema_version'] >= 3:
        family_map = {}; state_families = {}
        with (root / 'source-family-map.jsonl').open(encoding='utf-8') as f:
            for index,line in enumerate(f):
                if index >= 4096 or time.monotonic() >= deadline or cancel.exists(): raise TimeoutError('Family map check bound')
                mapping = json.loads(line); key = (mapping['SourceDataset'],mapping['OriginalFamily'])
                assert mapping['OriginalDerivedSplit'] == mapping['QueueSplit'] == 'train'
                assert key not in family_map or family_map[key] == mapping['QueueFamily'], 'Same source/family was remapped inconsistently'
                assert mapping['SourceProvenance']['Dataset'] == mapping['SourceDataset']
                family_map[key] = mapping['QueueFamily']
        with (root / 'questions.jsonl').open(encoding='utf-8') as f:
            for index,line in enumerate(f):
                if index >= 30000 or time.monotonic() >= deadline or cancel.exists(): raise TimeoutError('Source identity check bound')
                row = json.loads(line)
                if row['Template'] != 'upstream_question': continue
                assert row['Split'] == row['OriginalDerivedSplit'] == 'train'
                key = (row['SourceProvenance']['Dataset'],row['OriginalFamily'])
                assert row['Family'] == family_map[key]
                state_key = sha(row['State'].replace('\r\n','\n'))
                assert state_key not in state_families or state_families[state_key] == row['Family'], 'Exact source state crossed families'
                state_families[state_key] = row['Family']
        for entry in manifest['source_sampling_coverage']:
            assert entry['sampled_records'] == entry['sampled_families'] <= (32 if entry['pilot_only_corpus'] else 64)
            assert entry['formal_training_admitted'] is False and entry['derived_split_selected'] == 'train'
    pilot = json.loads((root / 'pilot-questions.json').read_text(encoding='utf-8')); gold = json.loads((root / 'pilot-reference-private.json').read_text(encoding='utf-8'))
    assert len(pilot) == len(gold) == 24 and all(r['Type']=='Score' and r['RecordId'] in ids for r in pilot)
    assert sum(r['answerable'] for r in gold.values()) == 21
    for asset in manifest['files']:
        if time.monotonic() >= deadline or cancel.exists(): raise TimeoutError('Asset check deadline/cancel')
        path = root / asset['path']; assert path.stat().st_size == asset['bytes'] and path.stat().st_size <= 100 * 1024 * 1024
        assert hashlib.sha256(path.read_bytes()).hexdigest() == asset['sha256']
    return {'records': len(ids), 'families': len(families), 'templates': dict(counts), 'pilot_records': len(pilot), 'pilot_expected_abstentions': 3,
            'target_sum_tolerance':2e-5, 'upstream_serialized_distributions_above_1e_6_error':approximate_distributions, 'maximum_target_sum_error':maximum_sum_error}


def main():
    wait_for_guardian(); ap = argparse.ArgumentParser(); ap.add_argument('--queue',required=True); ap.add_argument('--output',required=True); a = ap.parse_args()
    root = pathlib.Path(a.queue).resolve(); output = pathlib.Path(a.output).resolve(); started = time.monotonic(); deadline = started + 90
    cancel = root / 'cancel.request'; count = check_templates(deadline,cancel); evidence = check_queue(root,deadline,cancel)
    report = {'status':'PASS_offline_question_semantics_and_family_contract','semantic_label_checks':count, **evidence,
              'elapsed_seconds':time.monotonic()-started,'formal_training_admitted':False,'measured_student_accuracy':None,
              'scope':'Independent supplied-fact reference recomputation, bilingual equality, candidate permutation label mapping, same-family split, hidden gold, file SHA; no student inference'}
    with output.open('x',encoding='utf-8') as f: json.dump(report,f,ensure_ascii=False,indent=2)
    print(compact(report),flush=True)


if __name__ == '__main__': main()
