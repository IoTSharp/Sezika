"""Bounded inventory-only audit of frozen sources. Never treats rows as unique tasks."""
import argparse, collections, csv, hashlib, json, pathlib, time
from bounded_start import wait_for_guardian


def read_json(path):
    if path.stat().st_size > 8 * 1024 * 1024:
        raise ValueError('Metadata byte bound exceeded')
    return json.loads(path.read_text(encoding='utf-8-sig'))


OVERLAP = {
    'mteb/amazon_massive_intent': 'MASSIVE 意图派生镜像；与 AmazonScience/massive 按原始 id/语言去重',
    'mteb/amazon_massive_scenario': 'MASSIVE 场景派生镜像；与 AmazonScience/massive 按原始 id/语言去重',
    'mteb/banking77': 'Banking77 镜像；优先 PolyAI/banking77，不计为新题',
    'C-MTEB/AFQMC': '与 clue/clue 的 afqmc 同源；需按句对内容核对跨载体重复',
    'C-MTEB/CMNLI': '与 clue/clue 的 cmnli 同源；本镜像只有评测资产',
    'C-MTEB/OCNLI': '与 clue/clue 的 ocnli 同源；本镜像只有评测资产',
    'C-MTEB/IFlyTek-classification': '与 clue/clue 的 iflytek 同源；完整类别超过32，需显式任务设计',
    'C-MTEB/PAWSX': '与 google-research-datasets/paws-x 中文部分同源；需核对句对内容',
    'C-MTEB/STSB': '相似度连续标签；需显式 Score 语义及分桶策略',
    'nyu-mll/glue': 'MRPC/QQP/MNLI/RTE/QNLI/STS-B/SST-2 等配置分别审查；与独立 MNLI 等来源存在同源配置',
    'aps/super_glue': 'BoolQ 与 google/boolq 同源；其余配置分别审查',
    'facebook/xnli': 'all_languages 与各语言配置复用题目；跨语言按官方题号分组',
    'cais/mmlu': 'all 与学科配置复用题目；保留作评测，不归入训练',
    'google-research-datasets/go_emotions': 'raw 与 simplified 有内容复用；当前仅 simplified train',
    'LocalLLaMA/typed-decisions': 'all 聚合与各类配置复用；当前排除 all 聚合',
    'ZefanCai/Open-Jev': '配置及原 JSON/Parquet 载体可能复用；当前内容去重，语义跨来源分组待审',
    'samatv256/jev-decisions-v1': 'full 与 clean50k 父子子集；当前仅 clean50k train',
    'allenai/openbookqa': 'main 与 additional 同题；选择一种载体，不累加两套4957行',
    'allenai/winogrande': 'train_xs/s/m/l/xl 为嵌套训练子集，debiased 另需按题号去重',
    'hotchpotch/s1mb-dataset': '题型/领域/聚合配置可能复用；评测保留',
    'HIT-TMG/JevEmbed-Data': '混合来源 typed corpus；来源组件许可与跨来源重复待审',
    'C-Tianyu/NanoJev-Data': '混合来源 typed corpus；来源组件许可与跨来源重复待审',
    'hotchpotch/bekko-system-one-dataset-v0': '多配置 typed corpus；组件许可及重叠待审',
}
RESERVED = {
    'allenai/reward-bench', 'Muennighoff/xwinograd', 'TIGER-Lab/MMLU-Pro',
    'cais/mmlu', 'gorilla-llm/Berkeley-Function-Calling-Leaderboard',
    'hotchpotch/s1mb-dataset', 'hotchpotch/S1MB', 'little-g-ai/Sys1Cal-v1',
    'Praveenrajus/jev-bench',
}
CONVERTIBLE = {
    'PolyAI/banking77', 'clinc/clinc_oos', 'AmazonScience/massive', 'google/boolq',
    'stanfordnlp/snli', 'google-research-datasets/go_emotions', 'Anthropic/hh-rlhf',
    'samatv256/jev-decisions-v1', 'LocalLLaMA/typed-decisions', 'kaivoss/system-one-270m-data',
    'dwidlee/systemone-lite-phase2', 'ZefanCai/Open-Jev', 'nvidia/HelpSteer2',
    'nvidia/HelpSteer', 'google/civil_comments', 'Team-ACE/ToolACE',
}


def main():
    wait_for_guardian()
    parser = argparse.ArgumentParser()
    parser.add_argument('--metadata', required=True)
    parser.add_argument('--task-root', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--report', required=True)
    parser.add_argument('--maximum-items', type=int, default=5000)
    parser.add_argument('--seconds', type=int, default=90)
    parser.add_argument('--cancel')
    args = parser.parse_args()
    if not 1 <= args.maximum_items <= 5000 or not 1 <= args.seconds <= 120:
        raise ValueError('Explicit audit bounds required')
    start = time.monotonic()
    def check():
        if time.monotonic() - start >= args.seconds or (args.cancel and pathlib.Path(args.cancel).exists()):
            raise TimeoutError('Audit deadline or cancellation')
    metadata = pathlib.Path(args.metadata).resolve()
    names = ['source-catalog.json', 'source-completeness.json', 'download-inventory.json', 'redistribution-review.json']
    inputs = {name: read_json(metadata / name) for name in names}
    inventory = inputs['download-inventory.json']
    catalog = inputs['source-catalog.json']
    selected = inputs['source-completeness.json']['sources']
    if len(inventory) > args.maximum_items or len(selected) > 96 or len(catalog) > 128:
        raise ValueError('Inventory/source bound exceeded')
    rights = {x['dataset']: x for x in inputs['redistribution-review.json']}
    catalog_index = {x['id']: x for x in catalog}
    grouped, sha_groups = collections.defaultdict(list), collections.defaultdict(list)
    for index, asset in enumerate(inventory):
        check()
        if asset.get('dataset'):
            grouped[asset['dataset']].append(asset)
        if asset.get('rows') is not None:
            sha_groups[asset['sha256']].append(asset)
        if index and index % 1000 == 0:
            print('inventory reviewed:', index, flush=True)
    task_root = pathlib.Path(args.task_root).resolve()
    conversion = {}
    manifest = task_root / 'full-reviewed-v1' / 'manifest.json'
    if manifest.is_file():
        value = read_json(manifest)
        conversion = {x['source']: x for x in value.get('source_coverage', [])}
        conversion_status = value['status']
    else:
        conversion_status = 'manifest_not_available_at_audit_snapshot'
    arc_manifest_path = task_root / 'arc-full-v1' / 'manifest.json'
    arc_manifest = read_json(arc_manifest_path) if arc_manifest_path.is_file() else None
    rows = []
    for index, complete in enumerate(selected):
        check()
        source = complete['dataset']
        assets = grouped[source]
        data = [x for x in assets if x.get('rows') is not None]
        training = [x for x in data if x.get('split_hint') == 'source-train']
        evaluation = [x for x in data if x.get('split_hint') in ('source-development', 'source-test-evaluation')]
        other = [x for x in data if x not in training and x not in evaluation]
        license_value = rights.get(source, {}).get('card_license', catalog_index.get(source, {}).get('license'))
        declaration = json.dumps(license_value, ensure_ascii=False) if license_value else '未声明'
        eligible = rights.get(source, {}).get('decision') == 'eligible_under_declared_terms_with_attribution'
        if source in RESERVED:
            scope = '评测/校准保留'
        elif not complete['complete']:
            scope = '下载未完整；不接入'
        elif source in CONVERTIBLE and eligible:
            entry = conversion.get(source)
            scope = '适配训练部分；' + ('转换状态=' + entry['status'] if entry else '转换终态待主任务清单')
        elif eligible:
            scope = '许可初审通过；格式/拆分待适配'
        else:
            scope = '许可/组件条款及格式待审核'
        if source == 'allenai/ai2_arc' and arc_manifest and arc_manifest['status'] == 'COMPLETE_PREPARATION_ONLY':
            scope = '追加完成原 train 私人整备；CC-BY-SA-4.0 声明/归因已保留；正式准入待验'
        duplicate_bytes = sum(x['bytes'] for x in data if len(sha_groups[x['sha256']]) > 1)
        rows.append({
            'source': source, 'download_complete': complete['complete'],
            'planned_files': complete['planned_files'], 'downloaded_files': complete['downloaded_files'],
            'downloaded_bytes': complete['downloaded_bytes'], 'license_declared': license_value,
            'rights_decision': rights.get(source, {}).get('decision', 'not_reviewed'),
            'scope': scope, 'train_physical_rows': sum(x['rows'] for x in training),
            'holdout_physical_rows': sum(x['rows'] for x in evaluation),
            'other_physical_rows': sum(x['rows'] for x in other),
            'rows_are_unique_questions': False,
            'exact_duplicate_file_bytes_in_row_counted_assets': duplicate_bytes,
            'overlap_note': OVERLAP.get(source, '跨来源语义重复未审；不承诺独立题目数'),
            'revision': catalog_index.get(source, {}).get('revision'),
        })
    report = {
        'schema_version': 1, 'audit_utc': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
        'audit_kind': 'inventory_snapshot_and_conversion_manifest_only',
        'inputs': {name: {'path': str(metadata / name), 'sha256': hashlib.sha256((metadata / name).read_bytes()).hexdigest()} for name in names},
        'selected_sources': len(rows), 'download_complete_sources': sum(x['download_complete'] for x in rows),
        'inventory_assets': len(inventory), 'inventory_physical_rows': sum(x['rows'] for x in inventory if x.get('rows') is not None),
        'catalog_records': len(catalog), 'rights_review_records': len(rights),
        'rights_eligible_selected_sources': sum(x['rights_decision'] == 'eligible_under_declared_terms_with_attribution' for x in rows),
        'adapter_selected_sources': sum(x['source'] in CONVERTIBLE and x['rights_decision'] == 'eligible_under_declared_terms_with_attribution' for x in rows),
        'conversion_manifest_status': conversion_status,
        'additional_private_preparations': [] if not arc_manifest else [{'source':'allenai/ai2_arc','status':arc_manifest['status'],'source_rows_read':arc_manifest['source_rows_read'],'records_written':arc_manifest['records_written']}],
        'formal_training_admitted': False,
        'distinct_questions': None,
        'exact_same_byte_row_counted_file_groups': sum(len(v) > 1 for v in sha_groups.values()),
        'exact_same_byte_extra_physical_rows': sum(sum(x['rows'] for x in group[1:]) for group in sha_groups.values()),
        'scope_note': 'Collection completeness is all selected files, not full original-training semantic acceptance; overlaps and held-out splits remain separate.',
        'sources': rows,
    }
    output = pathlib.Path(args.output).resolve()
    output.mkdir(exist_ok=False, parents=True)
    (output / 'coverage.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    fields = ['source','download_complete','downloaded_files','train_physical_rows','holdout_physical_rows','other_physical_rows','rights_decision','scope','overlap_note']
    with (output / 'coverage.csv').open('w', encoding='utf-8-sig', newline='') as file:
        writer = csv.DictWriter(file, fieldnames=fields, extrasaction='ignore')
        writer.writeheader(); writer.writerows(rows)
    lines = [
        '# 全来源训练覆盖审查（2026-10-11）', '',
        f"本审查核对 {len(rows)} 个已选来源；{report['download_complete_sources']} 个完成原计划下载。原目录有 {len(inventory):,} 个资产，记录的物理行总数为 {report['inventory_physical_rows']:,}，含镜像、聚合配置和不同载体；独立题目数尚未测定。",
        '',
        f"已有许可初审通过来源 {report['rights_eligible_selected_sources']} 个，其中 {report['adapter_selected_sources']} 个在当前转换适配范围。转换清单快照状态：`{conversion_status}`。本表不授予正式训练资格，也不把下载完成解释为每条标签可用。",
        '',
        '原始 train 可整理为训练/开发候选；原始 validation/test、评测专用来源和校准集保留。MASSIVE、CLINC 的原始归档中有内部分区，ToolACE 原文件未分区，不能仅凭文件名判定训练行数。表中行数来自既有 inventory，0 表示未在该字段统计到，不能解释为源数据为空。', '',
        '| 来源 | 文件完成 | 原 train 物理行 | 保留评测物理行 | 其他物理行 | 声明许可 | 当前覆盖范围 | 重叠/适配备注 |',
        '|---|---:|---:|---:|---:|---|---|---|',
    ]
    for row in rows:
        check()
        license_display = ', '.join(str(x) for x in row['license_declared']) if isinstance(row['license_declared'],list) else str(row['license_declared'] or '未声明')
        lines.append(f"| {row['source']} | {row['downloaded_files']}/{next(x['planned_files'] for x in selected if x['dataset']==row['source'])} | {row['train_physical_rows']:,} | {row['holdout_physical_rows']:,} | {row['other_physical_rows']:,} | {license_display} | {row['scope']} | {row['overlap_note']} |")
    lines += ['', '机器可读覆盖文件：`' + str(output / 'coverage.json') + '` 和 `' + str(output / 'coverage.csv') + '`。本表只读固定元数据，未重扫训练内容、未运行教师模型、未训练、未发布。']
    lines += ['', f"完全相同字节的计行文件共有 {report['exact_same_byte_row_counted_file_groups']} 组，额外重复物理行 {report['exact_same_byte_extra_physical_rows']:,}；仅按文件 SHA 去重仍不足以识别 main/additional、镜像和语义近似题。"]
    review_path = task_root / 'source-schema-review' / 'review.json'
    if review_path.is_file():
        review = read_json(review_path)
        lines += ['', '## 四个候选来源的实际格式审查', '',
                  '6 个固定原 train 文件各检查首 10 行，共 60 行候选映射通过；这是格式试验，未把抽样成功解释为整套标签正确。原始样本仅保留在本机私有 artifact。']
        for source in review['sources'][:4]:
            check()
            lines += ['', '`' + source['source'] + '`：revision `' + source['revision'] + '`；声明许可 `' + json.dumps(source['card_license'],ensure_ascii=False) + '`；审核状态 `' + source['rights_status'] + '`。']
            for asset in source['train_assets'][:8]:
                check()
                lines += ['- 文件：`' + asset['path'] + '`；SHA-256 `' + asset['sha256'] + '`；原始行数 `' + str(asset['rows']) + '`。']
        lines += ['', '适配建议：ARC 使用 `question`、`choices.{label,text}`、`answerKey`，按 label 身份匹配答案，不能假定字母标签。OpenBookQA 使用 `question_stem`；additional 可提供 `fact1`，但 main/additional 须二选一，`humanScore`、`clarity`、`turkIdAnonymized` 不送入输入。AFQMC/LCQMC 使用句对和二值 `score`，0→否、1→是；保留评测，负值/无标签排除，跨载体同句对需统一 family。', '',
                  'ARC 已核对固定卡片的 CC-BY-SA-4.0 声明，归因、许可链接、变更说明和派生数据同许可条件已写入私人整备 notice。卡片正文的 Licensing Information 仍标 More Information Needed，因此只确认声明与文件身份，未独立证明所有上游题目权利。OpenBookQA 卡片声明 unknown；两份 C-MTEB 卡片未声明许可，继续待审。']
    if arc_manifest:
        lines += ['', '## ARC 完整原训练整理结果', '',
                  f"原 ARC-Challenge/train {1119:,} 行和 ARC-Easy/train {2251:,} 行全部读取；输出 {arc_manifest['records_written']:,} 物理规范记录。完全重复、冲突和隔离以 `arc-full-v1/manifest.json` 计数为准；原 validation/test 未进入输出。输出状态 `{arc_manifest['status']}`，formal/token-length/GPU 均未验收。", '',
                  '题号或规范题干相同的记录归为同一连通 family，确保两配置的同题不跨 train/development；候选按文本确定性排序并保留原候选映射，再使用共同 Writer 的循环旋转。完全重复只保留一份，冲突标签通过强制 exclusions 账本排除。', '',
                  '字节保留上游 README、归因/变更 notice、source-rights、source-assets、manifest、exclusions 及 corpus-bindings 已保存；这批资产保持私有，未发布。']
    pathlib.Path(args.report).write_text('\n'.join(lines) + '\n', encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k not in ('sources','inputs')}, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    main()
