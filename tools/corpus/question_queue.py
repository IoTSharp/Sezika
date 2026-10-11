"""Bounded bilingual teacher questions. Reference targets stay in a private ledger."""
import argparse, collections, gzip, hashlib, json, pathlib, time, unicodedata
from bounded_start import wait_for_guardian
from prepare_corpus import compact, sha, question
from question_templates import TEMPLATES, VARIANTS, specification

DOMAINS = [
 ('arithmetic','quantity','Quantity must be at least {t}.','数量必须至少达到 {t}。'),
 ('inventory','stock','Stock must be at least {t}.','库存必须至少达到 {t}。'),
 ('logistics','available_capacity','Available capacity must be at least {t}.','可用运力必须至少达到 {t}。'),
 ('invoice','matching_fields','The number of matching fields must be at least {t}.','匹配字段数量必须至少达到 {t}。'),
 ('scheduling','available_minutes','Available time must be at least {t} minutes.','可用时间必须至少达到 {t} 分钟。'),
 ('quality','passing_checks','Passing checks must be at least {t}.','通过检查的数量必须至少达到 {t}。'),
 ('authorization','verified_conditions','Verified authorization conditions must be at least {t}.','已核实的授权条件必须至少达到 {t}。'),
 ('network','successful_probes','Successful probes must be at least {t}.','成功探测次数必须至少达到 {t}。'),
 ('data_validation','valid_fields','Valid fields must be at least {t}.','有效字段数量必须至少达到 {t}。'),
 ('maintenance','completed_checks','Completed maintenance checks must be at least {t}.','已完成维护检查的数量必须至少达到 {t}。'),
 ('document_review','supporting_items','Supporting evidence items must be at least {t}.','支持证据条目必须至少达到 {t}。'),
 ('tool_routing','required_parameters','Supplied required parameters must be at least {t}.','已提供的必填参数数量必须至少达到 {t}。')]


class Budget:
    def __init__(self, out, seconds):
        self.started = time.monotonic(); self.out = out; self.seconds = seconds; self.items = 0

    def check(self):
        self.items += 1
        if self.items > 180000 or time.monotonic() - self.started >= self.seconds or (self.out / 'cancel.request').exists():
            raise TimeoutError('Question queue item/deadline/cancel bound')


def make_record(state, instruction, kind, options, target_index, language, family, template, variant, domain=None):
    answerable = target_index is not None
    target = [float(i == target_index) for i in range(len(options))] if answerable else None
    q = question(state, instruction, kind, options, target if answerable else [1 / len(options)] * len(options),
                 'owned_explicit_rule_reference', language, family,
                 semantics='synthetic_explicit_facts_and_rule; ordinal_Score_not_correctness_probability')
    q.pop('Target'); q.pop('SourceFamily'); q.pop('Suffix')
    rid = 'teacher-queue-' + sha(compact([state, instruction, kind, q['Candidates']]))
    record = {'RecordId': rid, 'Family': family,
              'Split': 'development' if int(family[:8], 16) % 10 == 0 else 'train', **q,
              'Domain': domain or template, 'Template': template, 'Variant': variant,
              'Source': 'owned_synthetic_explicit_rules_v2',
              'TemplateLimit': 'Parameterized synthetic cases; not independent general-purpose benchmark coverage'}
    reference = {'RecordId': rid, 'answerable': answerable, 'Target': target,
                 'Semantics': 'deterministic reference from supplied synthetic facts and explicit rule; not a human gold annotation'}
    return record, reference


def rich_rows(seeds, budget):
    for template in TEMPLATES:
        for seed in range(seeds):
            family = sha(f'owned-synthetic-v2:{template}:{seed}')
            for language in ('en', 'zh'):
                for variant in VARIANTS:
                    budget.check()
                    spec = specification(template, seed, language, variant)
                    for kind in ('Choice', 'Boolean', 'Score'):
                        item = spec[kind]
                        yield make_record(spec['state'], item['instruction'], kind, item['options'], item['index'], language, family, template, variant)
                    item = spec['Choice']; n = len(item['options']); offset = 1 + seed % (n - 1)
                    options = item['options'][offset:] + item['options'][:offset]
                    index = (item['index'] - offset) % n
                    yield make_record(spec['state'], item['instruction'], 'Choice', options, index, language, family, template, variant + '_candidate_permutation')


def threshold_rows(seeds, budget):
    for domain, field, en, zh in DOMAINS:
        for seed in range(seeds):
            family = sha(f'owned-synthetic-v1:{domain}:{seed}')
            threshold = 2 + seed % 13; value = threshold + (-2, -1, 0, 1, 2)[seed % 5]
            for language in ('en', 'zh'):
                for variant in ('base', 'counterfactual', 'negated', 'missing'):
                    budget.check()
                    actual = value if variant != 'counterfactual' else threshold - (value - threshold) - 1
                    state = {'scenario': domain, 'case': f'{domain}-{seed:03}',
                             'rule': (en if language == 'en' else zh).format(t=threshold),
                             'observations': {} if variant == 'missing' else {field: actual},
                             'scope': 'Judge only the supplied rule and observations; no action execution.' if language == 'en' else '仅按所给规则和观察作判断；不执行任何动作。'}
                    allowed = None if variant == 'missing' else actual >= threshold
                    options = ['Rule satisfied','Rule not satisfied','Insufficient evidence'] if language == 'en' else ['满足规则','不满足规则','证据不足']
                    instruction = 'Which conclusion follows from the supplied observations?' if language == 'en' else '根据所给观察，可以得出哪个结论？'
                    yield make_record(state, instruction, 'Choice', options, 2 if allowed is None else 0 if allowed else 1, language, family, 'threshold_rule', variant, domain)
                    negated = variant == 'negated'
                    options = ['False','True'] if language == 'en' else ['否','是']
                    instruction = ('Is the rule not satisfied?' if negated else 'Is the rule satisfied?') if language == 'en' else ('是否不满足规则？' if negated else '是否满足规则？')
                    yes = (not allowed if negated else allowed) if allowed is not None else None
                    yield make_record(state, instruction, 'Boolean', options, None if yes is None else int(yes), language, family, 'threshold_rule', variant, domain)


def score_pilot():
    rows = []; refs = {}
    for ti, template in enumerate(TEMPLATES):
        for k, (seed, variant) in enumerate(((0, 'base'), (2, 'counterfactual'), (4, 'reordered'))):
            language = 'zh' if (ti + k) % 2 else 'en'
            spec = specification(template, seed, language, variant); item = spec['Score']
            family = sha(f'owned-synthetic-v2:{template}:{seed}')
            row, reference = make_record(spec['state'], item['instruction'], 'Score', item['options'], item['index'], language, family, template, variant)
            rows.append(row); refs[row['RecordId']] = {k: v for k, v in reference.items() if k != 'RecordId'}
    for ti, template in enumerate(TEMPLATES[:3]):
        seed = 1; language = 'zh' if ti % 2 else 'en'
        spec = specification(template, seed, language, 'missing'); item = spec['Score']
        row, reference = make_record(spec['state'], item['instruction'], 'Score', item['options'], item['index'], language,
                                     sha(f'owned-synthetic-v2:{template}:{seed}'), template, 'missing')
        rows.append(row); refs[row['RecordId']] = {k: v for k, v in reference.items() if k != 'RecordId'}
    if len(rows) != 24 or len(refs) != 24 or sum(r['answerable'] for r in refs.values()) != 21:
        raise ValueError('Exact Score pilot contract differs')
    return rows, refs


def source_samples(corpus, budget, limit, allow_pilot, coverage):
    root = pathlib.Path(corpus).resolve(); manifest = json.loads((root / 'manifest.json').read_text(encoding='utf-8'))
    if manifest['status'] != 'COMPLETE_PREPARATION_ONLY' or bool(manifest.get('pilot')) != allow_pilot:
        raise ValueError('Source sampling requires closed complete preparation manifest')
    manifest_sha = hashlib.sha256((root / 'manifest.json').read_bytes()).hexdigest()
    excluded = set(); seen_sources = set(); seen_families = set()
    with (root / 'exclusions.jsonl').open(encoding='utf-8') as f:
        for i, line in enumerate(f):
            budget.check()
            if i >= 100000: raise ValueError('Exclusion item bound')
            excluded.add(json.loads(line)['record_id'])
    if len(manifest['shards']) > 4096: raise ValueError('Shard item bound')
    for shard in manifest['shards']:
        budget.check()
        if shard['split'] != 'train' or shard['source'] in seen_sources: continue
        if len(seen_sources) >= 16: break
        seen_sources.add(shard['source']); added = 0
        entry = {'corpus_root': str(root), 'manifest_sha256': manifest_sha, 'source': shard['source'],
                 'pilot_only_corpus': allow_pilot, 'sample_limit': limit, 'original_split': 'train_or_original_unsplit_ToolACE',
                 'derived_split_selected': 'train', 'sampled_records': 0, 'sampled_families': 0,
                 'formal_training_admitted': False, 'role': 'process_sample_only' if allow_pilot else 'sample_from_closed_complete_conversion'}
        coverage.append(entry)
        path = (root / shard['path']).resolve()
        if not path.is_relative_to(root): raise ValueError('Source shard path escapes corpus')
        digest = hashlib.sha256()
        with path.open('rb') as f:
            for i in range(4096):
                budget.check(); block = f.read(1024 * 1024)
                if not block: break
                digest.update(block)
            else: raise ValueError('Source shard byte bound')
        if path.stat().st_size != shard['bytes'] or digest.hexdigest() != shard['sha256']:
            raise ValueError('Closed source shard byte/SHA identity differs')
        with gzip.open(path, 'rt', encoding='utf-8') as f:
            for i in range(10000):
                budget.check(); line = f.readline(4 * 1024 * 1024 + 1)
                if not line: break
                if len(line) > 4 * 1024 * 1024: raise ValueError('Source row byte bound')
                row = json.loads(line)
                if row['RecordId'] in excluded or row['Family'] in seen_families: continue
                seen_families.add(row['Family']); target = row.pop('Target'); original = row['RecordId']
                row['RecordId'] = 'teacher-source-' + original
                row['SourceProvenance'] = row['Source']
                row['SourceCorpusManifestSha256'] = manifest_sha
                row['SourcePreparationPilotOnly'] = allow_pilot
                row['SourceRecordId'] = original; row['Source'] = 'canonical_corpus_sample:' + shard['source']
                row['Template'] = 'upstream_question'; row['Variant'] = 'original_train_sample'
                yield row, {'RecordId': row['RecordId'], 'answerable': True, 'Target': target,
                            'Semantics': 'upstream_reference_preserved_for_disagreement_review_only'}
                added += 1
                entry['sampled_records'] = added; entry['sampled_families'] = added
                if added >= limit: break


def merge_source_families(samples, budget):
    """Connect exact shared states and original source families before queue split use."""
    if len(samples) > 1024: raise ValueError('Source queue sample item bound')
    parents = {row['Family']: row['Family'] for row, _ in samples}; state_family = {}; mapping = []; conflicts = []
    def find(family):
        for _ in range(1024):
            budget.check()
            if parents[family] == family: return family
            family = parents[family]
        raise ValueError('Source family traversal item bound')
    for row, _ in samples:
        budget.check(); assert row['Split'] == 'train'
        normalized = unicodedata.normalize('NFC', row['State']).replace('\r\n', '\n')
        state_key = sha(normalized)
        if state_key in state_family:
            left = find(row['Family']); right = find(state_family[state_key])
            if left != right: parents[max(left, right)] = min(left, right)
        else: state_family[state_key] = row['Family']
    for row, _ in samples:
        budget.check(); original = row['Family']; canonical = find(original)
        row['OriginalFamily'] = original; row['OriginalDerivedSplit'] = row['Split']; row['Family'] = canonical
        mapping.append({'RecordId': row['RecordId'], 'SourceDataset': row['SourceProvenance']['Dataset'],
                        'SourceProvenance': row['SourceProvenance'],
                        'SourceCorpusManifestSha256': row['SourceCorpusManifestSha256'], 'OriginalFamily': original,
                        'QueueFamily': canonical, 'OriginalDerivedSplit': 'train', 'QueueSplit': 'train',
                        'MappingRule': 'connected_exact_state_and_original_family; semantic_family_review_pending'})
    first = {}; invalid = set()
    for row, ref in samples:
        budget.check(); rid = row['RecordId']
        if rid in first and (ref['Target'] != first[rid][1]['Target'] or ref['answerable'] != first[rid][1]['answerable']):
            invalid.add(rid); conflicts.append({'RecordId': rid, 'Reason': 'cross_corpus_reference_conflict_all_copies_excluded',
                                               'SourceProvenance': row['SourceProvenance']})
        else: first.setdefault(rid, (row, ref))
    return [(row,ref) for row,ref in samples if row['RecordId'] not in invalid], mapping, conflicts


def main():
    wait_for_guardian()
    ap = argparse.ArgumentParser(); ap.add_argument('--output', required=True); ap.add_argument('--corpus', action='append', default=[])
    ap.add_argument('--pilot-corpus', action='append', default=[])
    ap.add_argument('--seeds', type=int, default=40); ap.add_argument('--seconds', type=int, default=120); ap.add_argument('--pilot-only', action='store_true')
    a = ap.parse_args()
    if not 1 <= a.seeds <= 40 or not 15 <= a.seconds <= 180 or len(a.corpus) > 3 or len(a.pilot_corpus) > 1: raise ValueError('Preparation budget contract')
    out = pathlib.Path(a.output).resolve(); out.mkdir(exist_ok=False); budget = Budget(out, a.seconds)
    rows, refs = score_pilot(); counts = collections.Counter(); family_sets = collections.defaultdict(set)
    seen = {}; duplicates = 0; total = 0; incomplete = 0
    coverage = []; source_rows = []
    if not a.pilot_only:
        for corpus in a.corpus:
            source_rows.extend(source_samples(corpus, budget, 64, False, coverage))
        for corpus in a.pilot_corpus:
            source_rows.extend(source_samples(corpus, budget, 32, True, coverage))
    source_rows, family_map, conflicts = merge_source_families(source_rows, budget)
    with (out / 'source-family-map.jsonl').open('x',encoding='utf-8') as f:
        for item in family_map: budget.check(); f.write(compact(item)+'\n')
    with (out / 'source-reference-conflicts.jsonl').open('x',encoding='utf-8') as f:
        for item in conflicts: budget.check(); f.write(compact(item)+'\n')
    with (out / 'source-sampling-coverage.json').open('x',encoding='utf-8') as f: json.dump(coverage,f,ensure_ascii=False,indent=2)
    with (out / 'questions.jsonl').open('x', encoding='utf-8') as queue, (out / 'reference-gold.jsonl').open('x', encoding='utf-8') as gold, (out / 'exact-duplicates.jsonl').open('x', encoding='utf-8') as duplicate_ledger:
        streams = [((r, {'RecordId': r['RecordId'], **refs[r['RecordId']]}) for r in rows)] if a.pilot_only else [threshold_rows(a.seeds, budget), rich_rows(a.seeds, budget), iter(source_rows)]
        for stream in streams:
            for row, reference in stream:
                budget.check(); rid = row['RecordId']; signature = compact([row['State'], row['Instruction'], row['Type'], row['Candidates']])
                if rid in seen:
                    previous = seen[rid]
                    if previous != (signature, reference['Target'], reference['answerable'], row['Family']): raise ValueError('Repeated question ID/reference/family conflicts')
                    duplicate_ledger.write(compact({'RecordId': rid, 'Family': row['Family'], 'Variant': row['Variant'], 'Reason': 'exact_visible_question_duplicate'}) + '\n')
                    duplicates += 1; continue
                seen[rid] = (signature, reference['Target'], reference['answerable'], row['Family'])
                if total >= 20000: raise ValueError('Maximum 20000 question contract')
                queue.write(compact(row) + '\n'); gold.write(compact(reference) + '\n'); total += 1
                template = row['Template']; family_sets[template].add(row['Family'])
                counts[('template', template)] += 1; counts[('language', row['Language'])] += 1; counts[('type', row['Type'])] += 1
                counts[('split', row['Split'])] += 1; counts[('source', row['Source'])] += 1
                if not reference['answerable']: incomplete += 1
                if total % 2000 == 0: print('question queue written=', total, 'exact_duplicates_removed=', duplicates, flush=True)
    if not a.pilot_only and a.seeds >= 5 and any(r['RecordId'] not in seen for r in rows): raise ValueError('Pilot must occur in full queue')
    with (out / 'pilot-questions.json').open('x', encoding='utf-8') as f: json.dump(rows, f, ensure_ascii=False, indent=2)
    with (out / 'pilot-reference-private.json').open('x', encoding='utf-8') as f: json.dump(refs, f, ensure_ascii=False, indent=2)
    manifest = {'schema_version': 3, 'status': 'QUESTION_QUEUE_READY_TEACHER_NOT_CALLED', 'pilot_only': a.pilot_only,
                'total_questions': total, 'unique_record_ids': len(seen), 'exact_duplicates_removed': duplicates,
                'unique_families': len(set().union(*family_sets.values())),
                'families_by_template': {k: len(v) for k, v in sorted(family_sets.items())},
                'counts': {category: {name: value for (c, name), value in sorted(counts.items()) if c == category} for category in ('template','language','type','split','source')},
                'explicitly_unanswerable_questions': incomplete, 'languages': sorted(name for category,name in counts if category=='language'),
                'synthetic_template_structures': list(TEMPLATES) + ['threshold_rule'], 'seeds_per_structure_or_threshold_domain': a.seeds,
                'pilot_records': 24, 'pilot_type': 'Score', 'pilot_answerable': 21, 'pilot_abstentions_expected': 3,
                'gold_hidden_from_teacher': True, 'formal_training_admitted': False,
                'source_corpora': [str(pathlib.Path(c).resolve()) for c in a.corpus],
                'pilot_source_corpora': [str(pathlib.Path(c).resolve()) for c in a.pilot_corpus],
                'source_sampling': 'maximum64 per full converted source and32 per pilot source; original train or ToolACE unsplit only; derived train only; mandatory exclusions observed',
                'source_sampling_coverage': coverage, 'source_reference_conflict_exclusions': len({x['RecordId'] for x in conflicts}),
                'source_family_mapping': 'OriginalFamily and SourceProvenance preserved; exact shared states/source families connected; train split retained; semantic family review pending',
                'family_policy': 'translations/counterfactuals/negations/reordered_facts/candidate_permutations share one family and derived split',
                'template_limit': 'Seed and domain variants are parameterized exercises, not independently sourced or general-purpose reasoning coverage',
                'measured_student_accuracy': None, 'calibrated': False, 'elapsed_seconds': time.monotonic() - budget.started, 'files': []}
    for p in sorted(out.iterdir())[:16]:
        budget.check()
        if p.is_file():
            digest = hashlib.sha256()
            with p.open('rb') as f:
                for i in range(512):
                    budget.check(); block = f.read(1024 * 1024)
                    if not block: break
                    digest.update(block)
                else: raise ValueError('Output hash byte bound')
            manifest['files'].append({'path': p.name, 'bytes': p.stat().st_size, 'sha256': digest.hexdigest()})
    with (out / 'manifest.json').open('x', encoding='utf-8') as f: json.dump(manifest, f, ensure_ascii=False, indent=2)
    print('questions=', total, 'families=', manifest['unique_families'], 'Score_pilot=', len(rows), 'teacher_called=false', flush=True)


if __name__ == '__main__': main()
