"""Explicit synthetic decision tasks, never real-world facts or model inference.

Each seed defines one family. Translations, counterfactuals, negations, order
changes and candidate permutations must remain in that same family.
"""
import datetime

TEMPLATES = ('date_order', 'relation_chain', 'evidence_conflict', 'tool_routing',
             'budget_arithmetic', 'schedule_overlap', 'document_evidence')
VARIANTS = ('base', 'counterfactual', 'negated', 'missing', 'reordered')


def bilingual(en, zh, language):
    return zh if language == 'zh' else en


def specification(template, seed, language, variant):
    """Return visible task state and reference indices derived from explicit facts."""
    if template not in TEMPLATES or language not in ('en', 'zh') or variant not in VARIANTS:
        raise ValueError('unknown_template_language_or_variant')
    negated = variant == 'negated'
    missing = variant == 'missing'
    counter = variant == 'counterfactual'
    reorder = variant == 'reordered'
    b = lambda en, zh: bilingual(en, zh, language)
    state = {'case': f'{template}-{seed:03}', 'scenario': template,
             'scope': b('Synthetic case: judge only the supplied rules and facts; do not execute actions.',
                        '合成案例：仅按所给规则和事实作判断，不执行任何动作。')}

    if template == 'date_order':
        first = datetime.date(2026, 1, 15) + datetime.timedelta(days=seed * 3)
        gap = (-4, 0, 1, 2, 4, 7, 14, 21)[seed % 8]
        second = first + datetime.timedelta(days=gap)
        if counter:
            first, second = second, first
            gap = -gap
        events = [{'event': 'A', 'date': first.isoformat()}, {'event': 'B', 'date': second.isoformat()}]
        if missing:
            events[1].pop('date')
        if reorder:
            events.reverse()
        state.update(events=events, calendar='Gregorian', date_format='YYYY-MM-DD', timezone='UTC')
        choice = [b('A occurs earlier', 'A 先发生'), b('B occurs earlier', 'B 先发生'),
                  b('Same date', '同一天'), b('Insufficient evidence', '证据不足')]
        ci = 3 if missing else 0 if gap > 0 else 1 if gap < 0 else 2
        truth = None if missing else gap > 0
        score = None if missing else 0 if gap <= 0 else 1 if gap <= 2 else 2 if gap <= 6 else 3 if gap <= 13 else 4
        score_instruction = b('Rate the lead time of A before B: 0 if A is not earlier; 1 for 1-2 days; 2 for 3-6; 3 for 7-13; 4 for at least 14. Use only the dates.',
                              '给 A 领先 B 的天数评级：A 没有更早为 0；1 至 2 天为 1；3 至 6 天为 2；7 至 13 天为 3；至少 14 天为 4。仅使用所给日期。')
        proposition = b('Does A occur earlier than B?', 'A 是否比 B 更早发生？')
        inverse = b('Is it false that A occurs earlier than B?', 'A 比 B 更早发生这一判断是否为假？')

    elif template == 'relation_chain':
        hops = 1 + seed % 4
        nodes = [f'N{i}' for i in range(hops + 1)]
        edges = [{'before': nodes[i], 'after': nodes[i + 1]} for i in range(hops)]
        if counter:
            edges[seed % hops] = {'before': edges[seed % hops]['after'], 'after': edges[seed % hops]['before']}
        if reorder:
            edges.reverse()
        state.update(nodes=nodes, edges=edges, start=nodes[0], end=nodes[-1], edge_list_complete=not missing,
                     rule=b('An edge means its before-node precedes its after-node. Precedence is transitive. The listed edges form a directed graph. Only if edge_list_complete is true can absence of a path establish no path. Score the longest verified path from start to end, capped at four edges; score zero if no path exists in a complete graph.',
                            '每条边表示 before 节点先于 after 节点，先后关系可传递。所列边组成有向图。只有 edge_list_complete 为 true，找不到路径才表示不存在路径。给 start 到 end 最长已核实路径的边数评分，最多为 4；完整图不存在路径则为 0。'))
        # An incomplete edge list must not inherit hidden original edges.
        if missing:
            state['edges'] = edges[:-1]
        choice = [b('A path is established', '已建立路径'), b('No path in the complete graph', '完整图中不存在路径'), b('Insufficient evidence', '证据不足')]
        ci = 2 if missing else 1 if counter else 0
        truth = None if missing else not counter
        score = None if missing else 0 if counter else hops
        score_instruction = b('Using the supplied graph rule, rate the longest verified start-to-end path with the ordinal levels 0, 1, 2, 3, 4.',
                              '根据所给图规则，按 0、1、2、3、4 的有序等级，给已核实 start 到 end 的最长路径评分。')
        proposition = b('Does a directed path from start to end exist?', '是否存在 start 到 end 的有向路径？')
        inverse = b('Is there no directed path from start to end?', '是否不存在 start 到 end 的有向路径？')

    elif template == 'evidence_conflict':
        active = seed % 5
        observations = [{'source': f'S{i}', 'status': 'active' if i < active else 'inactive'} for i in range(4)]
        if counter:
            i = seed % 4
            observations[i]['status'] = 'inactive' if observations[i]['status'] == 'active' else 'active'
        if reorder:
            observations.reverse()
        if missing:
            observations = observations[:2]
        count = sum(o['status'] == 'active' for o in observations)
        state.update(observations=observations, observation_list_complete=not missing,
                     rule=b('Sources have equal authority and refer to the same object at the same time. All active reports establish active; all inactive reports establish inactive; mixed reports are conflicting. An incomplete list does not establish either Boolean status. Score the number of distinct listed sources reporting active only when the list is complete, capped at four. This count is not a probability.',
                            '各来源权威相等，描述同一时刻同一对象。全部报告 active 才确定为 active；全部 inactive 才确定为 inactive；同时出现两种报告表示证据冲突。列表不完整时不确定布尔状态。仅在列表完整时，按报告 active 的不同来源数量评分，最多为 4。这是计数，不是概率。'))
        choice = [b('Active established', '确定为 active'), b('Inactive established', '确定为 inactive'),
                  b('Evidence is conflicting', '证据冲突'), b('Insufficient evidence', '证据不足')]
        ci = 3 if missing else 0 if count == 4 else 1 if count == 0 else 2
        truth = None if missing or 0 < count < 4 else count == 4
        score = None if missing else count
        score_instruction = b('Apply the supplied counting rule and rate the number of independent sources reporting active from 0 to 4.',
                              '按所给计数规则，给独立来源报告 active 的数量按 0 至 4 评分。')
        proposition = b('Is active established by the evidence?', '所给证据是否确定对象为 active？')
        inverse = b('Is inactive established by the evidence?', '所给证据是否确定对象为 inactive？')
        # Conflict is indeterminate for both positive and negative propositions.
        negated = False
        if variant == 'negated':
            proposition = inverse
            truth = None if truth is None else not truth

    elif template == 'tool_routing':
        routes = [{'intent': 'lookup', 'tool': 'lookup_record'}, {'intent': 'reserve', 'tool': 'reserve_slot'},
                  {'intent': 'cancel', 'tool': 'cancel_slot'}, {'intent': 'other', 'tool': 'manual_review'}]
        intent = ('lookup', 'reserve', 'cancel', 'other')[seed % 4]
        if counter:
            intent = ('lookup', 'reserve', 'cancel', 'other')[(seed + 1) % 4]
        names = ('record_id', 'requester_id', 'date', 'time')
        present = seed % 5
        fields = {k: f'synthetic-value-{i}' for i, k in enumerate(names[:present])}
        state.update(routes=routes[::-1] if reorder else routes,
                     request={} if missing else {'intent': intent, 'fields': fields},
                     request_complete=not missing, required_fields=list(names),
                     rule=b('Select the tool mapped to request.intent; the mapping is exhaustive. This is a route recommendation, not permission to execute. Score completeness as the number of the four required fields present in a complete request, from 0 to 4. If request_complete is false, both intent and field completeness are undetermined.',
                            '选择与 request.intent 映射对应的工具；映射是完整的。这是路由建议，不是执行授权。请求完整时，按四个必填字段中实际已提供的数量评 0 至 4 分。request_complete 为 false 时，意图与字段完整度均不确定。'))
        choice = [r['tool'] for r in routes] + [b('Insufficient evidence', '证据不足')]
        ci = 4 if missing else next(i for i, r in enumerate(routes) if r['intent'] == intent)
        truth = None if missing else intent == 'reserve'
        score = None if missing else present
        score_instruction = b('Rate required-field completeness using the exact 0-4 count specified in the supplied rule.',
                              '根据所给规则，按必填字段已提供数量的精确 0 至 4 等级给完整度评分。')
        proposition = b('Does the mapping select reserve_slot?', '所给映射是否选择 reserve_slot？')
        inverse = b('Does the mapping select a tool other than reserve_slot?', '所给映射是否选择 reserve_slot 以外的工具？')

    elif template == 'budget_arithmetic':
        budget = 30 + seed
        margin = (-8, 0, 1, 4, 7, 10, 11, 20)[seed % 8]
        cost = budget - margin
        if counter:
            budget, cost = cost, budget
            margin = -margin
        quantities = [{'name': 'budget', 'value': budget}, {'name': 'cost', 'value': cost}]
        if missing:
            quantities[1].pop('value')
        if reorder:
            quantities.reverse()
        state.update(quantities=quantities, unit='synthetic-unit',
                     rule=b('Permitted means cost is at most budget. Margin equals budget minus cost. Rate margin: 0 for negative; 1 for zero; 2 for 1-5; 3 for 6-10; 4 for at least 11. The result does not authorize a payment.',
                            'cost 不超过 budget 时规则允许。余量等于 budget 减 cost。余量为负评 0；为零评 1；1 至 5 评 2；6 至 10 评 3；至少 11 评 4。判断结果不授权支付。'))
        choice = [b('Permitted by the rule', '规则允许'), b('Not permitted by the rule', '规则不允许'), b('Insufficient evidence', '证据不足')]
        ci = 2 if missing else 0 if margin >= 0 else 1
        truth = None if missing else margin >= 0
        score = None if missing else 0 if margin < 0 else 1 if margin == 0 else 2 if margin <= 5 else 3 if margin <= 10 else 4
        score_instruction = b('Compute margin and apply the exact ordinal margin scale in the supplied rule.',
                              '计算余量，再按所给规则中的精确有序余量等级评分。')
        proposition = b('Does cost satisfy the supplied budget rule?', 'cost 是否满足所给预算规则？')
        inverse = b('Does cost violate the supplied budget rule?', 'cost 是否违反所给预算规则？')

    elif template == 'schedule_overlap':
        length = (30, 45, 60, 90, 120)[seed % 5]
        offset = (length, length - 10, length - 20, length - 40, 0)[seed % 5]
        intervals = [{'event': 'A', 'start': 600, 'end': 600 + length},
                     {'event': 'B', 'start': 600 + offset, 'end': 600 + offset + length}]
        if counter:
            intervals[1]['start'] = 600 + length + 5
            intervals[1]['end'] = 600 + 2 * length + 5
        overlap = max(0, min(i['end'] for i in intervals) - max(i['start'] for i in intervals))
        if missing:
            intervals[1].pop('end')
        if reorder:
            intervals.reverse()
        state.update(intervals=intervals, unit='minutes-after-midnight', timezone='UTC', same_date='2026-10-11',
                     rule=b('Intervals are half-open [start,end). Touching at an endpoint is not overlap. Rate overlap duration: 0 for zero; 1 for 1-14 minutes; 2 for 15-29; 3 for 30-59; 4 for at least 60.',
                            '时间区间采用左闭右开 [start,end)。仅端点相接不算重叠。重叠 0 分钟评 0；1 至 14 分钟评 1；15 至 29 分钟评 2；30 至 59 分钟评 3；至少 60 分钟评 4。'))
        choice = [b('Intervals overlap', '时间区间重叠'), b('Intervals do not overlap', '时间区间不重叠'), b('Insufficient evidence', '证据不足')]
        ci = 2 if missing else 0 if overlap > 0 else 1
        truth = None if missing else overlap > 0
        score = None if missing else 0 if overlap == 0 else 1 if overlap <= 14 else 2 if overlap <= 29 else 3 if overlap <= 59 else 4
        score_instruction = b('Compute the overlap duration and use the exact 0-4 ordinal scale in the supplied rule.',
                              '计算重叠持续时间，再按所给规则中的精确 0 至 4 有序等级评分。')
        proposition = b('Do the supplied intervals overlap?', '所给时间区间是否重叠？')
        inverse = b('Are the supplied intervals non-overlapping?', '所给时间区间是否不重叠？')

    else:  # document_evidence: fictional records only, no external knowledge labels.
        expected = {'owner': 'Mira', 'phase': 'review', 'team': 'Blue', 'document_date': '2026-10-01'}
        keys = list(expected)
        values = {'owner': 'Nico', 'phase': 'draft', 'team': 'Green', 'document_date': '2026-10-02'}
        for k in keys[:seed % 5]:
            values[k] = expected[k]
        if counter:
            values['owner'] = 'Nico' if values['owner'] == 'Mira' else 'Mira'
        facts = [{'field': k, 'value': v} for k, v in values.items()]
        if missing:
            facts.pop()
        if reorder:
            facts.reverse()
        count = sum(values[k] == expected[k] for k in keys)
        state.update(document=facts, document_complete=not missing, required_propositions=expected,
                     rule=b('All names and facts are fictional. Use only this document. A complete document gives exactly one value for every required field. Score how many of the four required propositions the document supports, 0-4. If document_complete is false, overall support count and owner are not established.',
                            '所有名称与事实均为虚构，仅使用此文档。完整文档为每个必需字段给出唯一值。按文档支持四项 required_propositions 的数量评 0 至 4 分。document_complete 为 false 时，支持总数与所有者均不确定。'))
        choice = ['Mira', 'Nico', b('Insufficient evidence', '证据不足')]
        ci = 2 if missing else 0 if values['owner'] == 'Mira' else 1
        truth = None if missing else values['owner'] == 'Mira'
        score = None if missing else count
        score_instruction = b('Rate how many of the four required propositions are supported, using the supplied exact count scale 0-4.',
                              '按所给精确计数等级 0 至 4，给文档支持四项必需命题的数量评分。')
        proposition = b('Does the supplied document establish Mira as owner?', '所给文档是否确定所有者为 Mira？')
        inverse = b('Does the supplied document establish an owner other than Mira?', '所给文档是否确定所有者不是 Mira？')

    if negated:
        truth = None if truth is None else not truth
    return {'state': state,
            'Choice': {'instruction': b('Which conclusion follows from the supplied facts and rules?', '根据所给事实和规则，可以得出哪个结论？'), 'options': choice, 'index': ci},
            'Boolean': {'instruction': inverse if negated else proposition, 'options': [b('False', '否'), b('True', '是')], 'index': None if truth is None else int(truth)},
            'Score': {'instruction': score_instruction, 'options': [str(i) for i in range(5)], 'index': score}}
