"""Bounded source-by-source task/answer review from frozen metadata and prior samples."""
import argparse, collections, hashlib, json, pathlib, time
from bounded_start import wait_for_guardian


# Fit expresses task shape only. Every entry retains its separate rights/admission decision.
RULES = {
 'allenai/ai2_arc':('finite_choice','Choice','question+choices.label/text→answerKey；按标签身份匹配，已追加完整私人整备'),
 'allenai/openbookqa':('finite_choice','Choice','question_stem+choices→answerKey；main/additional同题选一份；fact1可作已提供事实'),
 'allenai/reward-bench':('evaluation_reserved','Choice','偏好基准 chosen/rejected；默认保留测评，不能以评测题扩充训练来提高同题分数'),
 'allenai/social_i_qa':('finite_choice','Choice','社会情境context/question/answerA-C与标签可转3选1；先补查当前原train资产是否齐全'),
 'allenai/winogrande':('finite_choice','Choice','掩码sentence+option1/2→answer1/2；训练子集嵌套，选明确配置并按题号去重'),
 'AmazonScience/massive':('finite_with_derivation','Choice','utt→18场景可直接选择；60意图需明确候选派生；归档partition与跨语言id保留'),
 'Anthropic/hh-rlhf':('pair_preference','Choice','chosen/rejected共享Human前缀后2选1；仅偏好监督，不代表回复事实正确'),
 'aps/super_glue':('mixed_configuration','Choice/Boolean','BoolQ/WiC/RTE/CB/COPA等配置分别映射；MultiRC多答案、ReCoRD实体选择不能直接混作同一题型'),
 'C-MTEB/AFQMC':('finite_boolean','Boolean','sentence1/sentence2→二值score；0否1是；与CLUE AFQMC同源，先统一family'),
 'C-MTEB/CMNLI':('holdout_only_snapshot','Choice','中文句对三关系；本次目录只有test载体，禁止改写为train；许可与标签身份待核'),
 'C-MTEB/IFlyTek-classification':('finite_with_derivation','Choice/Boolean','中文应用分类121类，超过32候选上限；需分层或明确训练候选派生'),
 'C-MTEB/LCQMC':('finite_boolean','Boolean','sentence1/sentence2→二值score；0否1是；句对对称family，维持原train/validation/test'),
 'C-MTEB/OCNLI':('holdout_only_snapshot','Choice','中文自然语言推断三关系；本次目录只有test载体，禁止纳入训练'),
 'C-MTEB/PAWSX':('finite_boolean','Boolean','中文句对→释义等价二值score；与PAWS-X中文部分跨载体核对'),
 'C-MTEB/STSB':('continuous_score_adapter','Score','句对相似度为连续score；不能把连续标注强制整数one-hot，需要显式分桶/插值语义'),
 'C-Tianyu/NanoJev-Data':('typed_adapter_pending','Choice/Boolean/Score','hard/soft决策标签、游戏与合成来源；只取原train，calibration/test/ood保留，逐题型与组件条款待审'),
 'cais/mmlu':('evaluation_reserved','Choice','学科多选题；all与分学科复用，现任务保留评测，不能混入训练再报告同基准提升'),
 'cardiffnlp/tweet_eval':('mixed_configuration','Choice/Boolean','sentiment/emotion/emoji/stance等有限类别；逐配置固定标签名字、保留原split'),
 'cardiffnlp/tweet_sentiment_multilingual':('finite_choice','Choice','各语言tweet→负/中性/正3类；固定标签映射，跨语言及复用tweet分组待核'),
 'clinc/clinc_oos':('finite_with_derivation','Choice','150意图+OOS超过32；仅train/oos_train，当前为含正确类与负类的训练派生，非全类别测评'),
 'clue/clue':('mixed_configuration','Choice/Boolean/Score','AFQMC/NLI/TNEWS/WSC/CSL可有限决策；C3/CHID需上下文候选；CMRC/DRCD抽取答案需另设计有限任务'),
 'dair-ai/emotion':('finite_choice','Choice','情绪text→有限label；采用原标签字典和原train，single_label监督不能当多标签概率'),
 'dwidlee/systemone-lite-phase2':('typed_supported_subset','Choice','state/instructions/criteria_keys+values→label_alias；已实现选择题映射，合成引用非人类gold'),
 'facebook/anli':('finite_choice','Choice','adversarial context/hypothesis→三关系label；R1/R2/R3 train可用，保留各轮dev/test'),
 'facebook/xnli':('finite_choice','Choice','多语言premise/hypothesis→三关系label；all_languages与逐语言配置去重，翻译同题统一family'),
 'fancyzhx/ag_news':('finite_choice','Choice','新闻text→4个topic label；保留类别名字，题目是分类而非开放域问答'),
 'fancyzhx/dbpedia_14':('finite_choice','Choice','百科title/content→14个ontology类；有限分类，维持原split'),
 'GhostScientist/jev-decisions-v1':('typed_adapter_pending','Choice/Boolean/Score','决策类state/questions与引用答案需按真实字段逐任务审核；不可仅凭jev名字假定可训练'),
 'google-research-datasets/go_emotions':('multilabel_boolean','Boolean','simplified labels为多标签；每种情绪二值问题，不能把多个情绪one-hot成单选；raw复用不再累计'),
 'google-research-datasets/paws':('finite_boolean','Boolean','sentence1/sentence2→paraphrase二值label；词面高重叠不是标签依据；原作条款card other待审'),
 'google-research-datasets/paws-x':('finite_boolean','Boolean','多语言句对→paraphrase二值label；跨语言同源题号分组，避免C-MTEB中文副本加权'),
 'google/boolq':('finite_boolean','Boolean','passage+question→answer布尔值；false/true固定顺序；validation保留'),
 'google/civil_comments':('annotation_fraction_boolean','Boolean','7个属性为报告的人类标注比例；保留[1-p,p]，不宣称是已校准正确率'),
 'gorilla-llm/Berkeley-Function-Calling-Leaderboard':('evaluation_reserved','Choice/structured','函数调用基准；保留评测与无关调用语义，不能训练后仍称未见测评'),
 'HIT-TMG/JevEmbed-Data':('typed_adapter_pending','Choice/Boolean/Score','多来源typed监督；source组件、答案结构与聚合复用待逐配置审核'),
 'hotchpotch/bekko-system-one-dataset-v0':('typed_adapter_pending','Choice/Boolean/Score','多配置state/questions/probability类目标；聚合、组件来源及硬/软标签语义待核'),
 'hotchpotch/S1MB':('evaluation_reserved','unknown','主要benchmark/代码资料；当前无可确认原train监督，不能把资料文件计作题目'),
 'hotchpotch/s1mb-dataset':('evaluation_reserved','Choice/Boolean/Score','System1 benchmark；按题型/领域聚合去重，现任务保留测评'),
 'HuggingFaceH4/ultrafeedback_binarized':('pair_preference','Choice','chosen/rejected消息序列→偏好2选1；须验证共享输入，保留教师偏好来源，不称人类正确答案'),
 'kaivoss/system-one-270m-data':('typed_supported_subset','Choice/Boolean/Score','原prompt+letters+target教师分布；Choice/Boolean/有序Score边界已适配，概率不是实测正确率'),
 'little-g-ai/Sys1Cal-v1':('evaluation_reserved','unknown','校准用途名称且元数据未可确认；保留研究/校准，不把未知载体纳入训练'),
 'loaiabdalslam/VEJI-Synth-v2-200K':('typed_adapter_pending','unknown','合成VEJI题目；须检验消息输出是否有限决策及真实答案字段，未适配且许可未知'),
 'LocalLLaMA/typed-decisions':('typed_supported_subset','Choice/Boolean/Score','state+questions.criteria+gold.probabilities；排除all聚合，引用分布保持来源语义'),
 'mteb/amazon_counterfactual':('finite_boolean','Boolean','评论句子→是否反事实类别；标签id须匹配原字典，不能把情感极性当反事实答案'),
 'mteb/amazon_massive_intent':('mirror_duplicate','Choice','MASSIVE意图视图；复用AmazonScience/massive，不能计作新增题'),
 'mteb/amazon_massive_scenario':('mirror_duplicate','Choice','MASSIVE场景视图；复用AmazonScience/massive，不能计作新增题'),
 'mteb/banking77':('mirror_duplicate','Choice','Banking77分类镜像；优先PolyAI原载体，来源同题不双倍训练'),
 'mteb/mtop_intent':('finite_with_derivation','Choice','多语言MTOP意图类别可能超过32；核原label字典后设计分层/受限候选任务'),
 'mteb/tweet_sentiment_extraction':('classification_view_pending','Choice/structured','若当前MTEB视图提供情感label可转分类；原抽取情感span目标需要生成/抽取能力，不能混为单选'),
 'Muennighoff/xwinograd':('evaluation_reserved','Choice','多语言Winograd评测题；保留原评测分区与跨语言同题family'),
 'nvidia/HelpSteer':('ordinal_score','Score','prompt+response→5属性各0..4；复杂度/冗长是描述量，不等同回复好坏'),
 'nvidia/HelpSteer2':('ordinal_score','Score','prompt+response→5属性各0..4；每属性独立题目及明确rubric，原dev保留'),
 'nyu-mll/glue':('mixed_configuration','Choice/Boolean/Score','SST-2/MRPC/QQP/MNLI/RTE/QNLI有限标签；STS-B连续评分另适配；原test无标签不能作为监督'),
 'nyu-mll/multi_nli':('finite_choice','Choice','premise/hypothesis→entailment/neutral/contradiction；与GLUE MNLI同源，matched/mismatched保留'),
 'ought/raft':('mixed_configuration','Choice/Boolean','11个少样本分类任务，逐任务标签及候选数审核；原test无标签保留，银行77类需派生'),
 'PKU-Alignment/BeaverTails':('multilabel_boolean','Boolean','prompt+response安全标注和类别可多标签Boolean；30k/330k子集复用；CC-BY-NC许可需单独范围审查'),
 'PolyAI/banking77':('finite_with_derivation','Choice','77意图超过32；当前训练含正确意图和最多31负类，不能宣称77类完整测评'),
 'Praveenrajus/jev-bench':('evaluation_reserved','unknown','jev-bench混合引用；目前隔离/评测保留，未知答案语义与组件许可不能自动准入'),
 'Rowan/hellaswag':('finite_choice','Choice','ctx+4个endings→label；这是有限续写选择题，原test标签缺失须保留'),
 'Salesforce/xlam-function-calling-60k':('tool_derivation_and_generation_unsupported','Choice/structured','单工具有限候选可派生；arguments/多调用/文本生成当前不适配；原数据文件未完整下载'),
 'samatv256/jev-decisions-v1':('typed_supported_subset','Choice','clean50k state+answer_options→target_index；只取clean train，不能加上其父full重复；动作仅预测不授权'),
 'SetFit/20_newsgroups':('finite_choice','Choice','新闻组text→20类；固定标签字典，保持原train/test'),
 'SetFit/SentEval-CR':('finite_choice','Choice','客户评论text→正负类别；原划分和小样本重复核查，不当开放式问答'),
 'SetFit/sst5':('ordinal_score','Score','评论text→5档情感强度；有序情感任务，不当客观事实正确率'),
 'SetFit/subj':('finite_boolean','Boolean','句子text→主观/客观类别；先核原label名字与布尔映射，避免正负反转'),
 'SetFit/TREC-QC':('finite_with_derivation','Choice','问题text→粗/细问题类型；粗6类可选，细粒度若>32则另设计'),
 'stanfordnlp/SHP':('pair_preference','Choice','history+human_ref_A/B→投票偏好label；核label=哪一边及投票筛选，偏好非事实正确性'),
 'stanfordnlp/snli':('finite_choice','Choice','premise/hypothesis→3关系；负label未标注行隔离，前提family保证开发不复用'),
 'takala/financial_phrasebank':('finite_choice','Choice','金融句子→3档情感；不同annotator-agreement配置嵌套，选一份或显式分歧分布，不能累加'),
 'tau/commonsense_qa':('finite_choice','Choice','question+5个choices→answerKey；固定id映射，测试无gold不作训练'),
 'Team-ACE/ToolACE':('tool_derivation_and_generation_unsupported','Choice/structured','只派生有唯一reference工具且候选2..32的单工具选择；参数/多调用/生成隔离，原unsplit须family留出'),
 'TIGER-Lab/MMLU-Pro':('evaluation_reserved','Choice','多学科10候选增强基准；当前保留评测，不拿validation/test做训练'),
 'Yelp/yelp_review_full':('ordinal_score','Score','评论text→5档星级label；source ordinal不当单纯正确率，保持原train/test'),
 'ZefanCai/Open-Jev':('typed_supported_subset','Choice/Boolean/Score','state_json+question/options/target；不把record_json/metadata中的隐藏答案当输入；跨配置内容去重'),
 'zeroshot/twitter-financial-news-sentiment':('finite_choice','Choice','金融tweet→3档情感label；核固定label名字与编号，保持validation'),
}


def main():
    wait_for_guardian()
    ap=argparse.ArgumentParser();ap.add_argument('--metadata',required=True);ap.add_argument('--task-root',required=True)
    ap.add_argument('--output',required=True);ap.add_argument('--report',required=True);ap.add_argument('--seconds',type=int,default=180);ap.add_argument('--cancel')
    a=ap.parse_args()
    if not 1<=a.seconds<=180:raise ValueError('Explicit semantics deadline required')
    started=time.monotonic();budget_bytes=0
    def check():
        if time.monotonic()-started>=a.seconds or (a.cancel and pathlib.Path(a.cancel).exists()):raise TimeoutError('Semantics deadline/cancel')
    def read(path,limit=8*1024*1024):
        nonlocal budget_bytes
        check();size=path.stat().st_size
        if size>limit:raise ValueError('Per-file metadata bound')
        budget_bytes+=size
        if budget_bytes>64*1024*1024:raise ValueError('Aggregate metadata byte bound')
        return json.loads(path.read_text(encoding='utf-8-sig'))
    root=pathlib.Path(a.metadata).resolve();task=pathlib.Path(a.task_root).resolve()
    inventory=read(root/'download-inventory.json');catalog=read(root/'source-catalog.json');complete=read(root/'source-completeness.json')
    rights=read(root/'redistribution-review.json')
    if len(inventory)>5000 or len(complete['sources'])>75 or len(catalog)>128 or len(rights)>128:raise ValueError('Source/item bound exceeded')
    selected=sorted(x['dataset'] for x in complete['sources'])
    if set(selected)-RULES.keys():raise ValueError('Explicit source rule missing')
    cat={x['id']:x for x in catalog};rts={x['dataset']:x for x in rights};cmp={x['dataset']:x for x in complete['sources']}
    grouped=collections.defaultdict(list)
    for asset in inventory:
        check()
        if asset.get('dataset'):grouped[asset['dataset']].append(asset)
    prior_samples={}
    schema_path=task/'source-schemas-v2.json'
    if schema_path.exists():
        for sample in read(schema_path)['sources'][:32]:
            if isinstance(sample.get('row'),dict):prior_samples[sample['dataset']]={'fields':sorted(sample['row']),'path':sample['path'],'sha256':sample.get('sha256')}
    actual_path=task/'source-schema-review'/'review.json'
    if actual_path.exists():
        for item in read(actual_path)['sources'][:4]:
            prior_samples[item['source']]={'fields':sorted(set(v for s in item['samples'] for v in s['schema_fields'])),'path':str(actual_path),'sha256':hashlib.sha256(actual_path.read_bytes()).hexdigest()}
    results=[]
    for index,source in enumerate(selected,1):
        check();assets=grouped[source];meta_assets=[x for x in assets if x['kind']=='hf-metadata']
        info=None;evidence=[]
        if len(meta_assets)>2:raise ValueError('Metadata carrier item bound')
        for asset in meta_assets:
            path=pathlib.Path(asset['path']).resolve();metadata=read(path,4*1024*1024)
            data=path.read_bytes();actual=hashlib.sha256(data).hexdigest()
            if actual!=asset['sha256']:raise ValueError('Frozen metadata identity differs')
            evidence.append({'path':str(path),'sha256':actual,'kind':'frozen_publisher_metadata'})
            if isinstance(metadata.get('cardData'),dict):info=metadata['cardData']
        fields=set();label_fields=[];class_labels={};configuration_count=0;splits=set()
        if info:
            configurations=info.get('dataset_info',[])
            if isinstance(configurations,dict):configurations=[configurations]
            if not isinstance(configurations,list) or len(configurations)>256:raise ValueError('Config item bound')
            configuration_count=len(configurations)
            for configuration in configurations:
                check();features=configuration.get('features',[])
                if not isinstance(features,list) or len(features)>256:raise ValueError('Feature item bound')
                for feature in features:
                    name=feature.get('name');fields.add(str(name))
                    if name and any(token in name.lower() for token in ('label','answer','target','gold','score','chosen','rejected','correct','safe','toxic','probab')):label_fields.append(name)
                    dtype=feature.get('dtype')
                    if isinstance(dtype,dict) and isinstance(dtype.get('class_label'),dict):
                        names=dtype['class_label'].get('names')
                        if names:class_labels[str(configuration.get('config_name','default'))+'|'+str(name)]=names
                for split in configuration.get('splits',[])[:32]:splits.add(split.get('name'))
            for config in info.get('configs',[])[:256]:
                for file in config.get('data_files',[])[:64]:
                    if isinstance(file,dict):splits.add(file.get('split'))
        if source in prior_samples:
            fields.update(prior_samples[source]['fields']);evidence.append({**prior_samples[source],'kind':'prior_bounded_decoded_sample_schema'})
        fit,prediction,reason=RULES[source]
        declared=rts.get(source,{}).get('card_license',cat.get(source,{}).get('license'))
        license_text=json.dumps(declared,ensure_ascii=False).lower()
        decision=rts.get(source,{}).get('decision','not_reviewed')
        if source=='allenai/ai2_arc':rights_state='private_preparation_declared_cc_by_sa_4_0_notice_preserved'
        elif decision=='eligible_under_declared_terms_with_attribution':rights_state='initial_declared_terms_review_passed_formal_admission_pending'
        elif 'cc-by-nc' in license_text or 'non-commercial' in license_text:rights_state='restricted_noncommercial_terms_scope_review_required'
        elif declared is None or 'unknown' in license_text:rights_state='missing_or_unknown_license_component_review_required'
        else:rights_state='declared_terms_exist_but_not_yet_individually_admitted'
        train_assets=[x for x in assets if x.get('split_hint')=='source-train' and x.get('rows') is not None]
        if source in ('AmazonScience/massive','clinc/clinc_oos','Anthropic/hh-rlhf'):
            train_presence='original_train_inside_archive_or_json_needs_internal_partition_review'
        elif train_assets:train_presence='original_train_carriers_listed'
        elif any('train' in pathlib.Path(x['path']).name for x in assets if x['kind']!='hf-metadata'):
            train_presence='train_named_carrier_present_row_count_unavailable'
        elif source=='Team-ACE/ToolACE':train_presence='original_unsplit_requires_family_holdout'
        else:train_presence='no_confirmed_original_train_carrier_in_this_snapshot'
        results.append({'source':source,'revision':cat.get(source,{}).get('revision'),'task_fit':fit,'prediction_type':prediction,
            'answer_semantics_and_reason':reason,'source_schema_fields':sorted(fields),'answer_fields_observed':sorted(set(label_fields)),
            'class_label_names_from_metadata':class_labels,'metadata_configuration_count':configuration_count,
            'original_splits_observed':sorted(str(x) for x in splits if x is not None),'train_asset_presence':train_presence,
            'train_physical_rows_from_inventory':sum(x['rows'] for x in train_assets),'download_complete':cmp[source]['complete'],
            'declared_license':declared,'rights_state':rights_state,'initial_rights_decision':decision,
            'schema_evidence_strength':'bounded_decoded_sample_and_metadata' if source in prior_samples else 'frozen_metadata_features' if fields else 'catalog_task_only_explicit_schema_validation_pending',
            'evidence':evidence,'formal_training_admitted':False,'full_training_done':False,'distinct_question_count':None})
        if index%10==0 or index==len(selected):print('semantics reviewed=',index,'/',len(selected),flush=True)
    fit_counts=collections.Counter(x['task_fit'] for x in results);rights_counts=collections.Counter(x['rights_state'] for x in results)
    schema_counts=collections.Counter(x['schema_evidence_strength'] for x in results)
    binding_notes={
        'source_assets':'Original source file inventory: paths, hashes, bytes, revision and source URL. It is not a rights/admission contract.',
        'source_rights':'Separate source-specific declared license/review/attribution/admission decisions.',
        'arc_contract':'arc-full-v1/source-assets.json has 2 original training Parquet assets and 1 upstream README; corpus-bindings SourceAssetsSha256 hashes this local inventory.',
        'generic_bind_script':'bind_corpus.py SourceAssetsSha256 hashes external dataset/ASSET_MANIFEST.json and adds SourceRoot; it does not copy source-assets.json locally.',
        'reader_at_snapshot':'Corpus.cs CorpusBindings declares SourceAssetsSha256, but observed constructor checks manifest/exclusions/source-rights only and has no SourceRoot property; source asset hash is not yet enforced there.',
        'scope':'Read-only schema review; no training, teacher calls, GPU or publication.'}
    output=pathlib.Path(a.output).resolve();output.mkdir(parents=True,exist_ok=False)
    report={'schema_version':1,'status':'SOURCE_TASK_SEMANTICS_REVIEW_ONLY','sources_reviewed':len(results),'maximum_sources':75,
        'elapsed_seconds':time.monotonic()-started,'metadata_bytes_read':budget_bytes,'task_fit_counts':dict(fit_counts),
        'rights_state_counts':dict(rights_counts),'schema_evidence_counts':dict(schema_counts),'formal_training_admitted':False,
        'full_training_done':False,'source_assets_contract_notes':binding_notes,'sources':results}
    (output/'semantics.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    lines=['## 75 来源的任务与答案语义复核','',
        f"逐来源检查 {len(results)} 个冻结来源，读取固定元数据和此前小样本的字段证据。没有重新解码全部数据、没有教师或训练。任务形状可转换与许可/标签质量准入是两项不同判断；本次所有来源 `formal_training_admitted=false`、`full_training_done=false`。",'',
        '任务形状统计：`'+json.dumps(dict(fit_counts),ensure_ascii=False)+'`。','',
        '许可审查状态统计：`'+json.dumps(dict(rights_counts),ensure_ascii=False)+'`。','',
        '格式证据统计：`'+json.dumps(dict(schema_counts),ensure_ascii=False)+'`。只有 catalog 任务说明而无实际字段证据的来源仍须解码验证，不能按名称猜定答案。','',
        '| 来源 | 任务形状 | 已识别字段（最多12个） | 答案语义及接入/保留理由 | 许可状态 |',
        '|---|---|---|---|---|']
    for row in results:
        check();fields=', '.join(row['source_schema_fields'][:12]) or '本次未确认字段'
        lines.append('| '+row['source']+' | '+row['task_fit']+' | '+fields+' | '+row['answer_semantics_and_reason']+'；'+row['train_asset_presence']+' | '+row['rights_state']+' |')
    lines+=['','`source-assets.json` 是原始文件身份清单，不是权限合同；许可决定在 `source-rights.json`。ARC 本地清单包含 2 个 train Parquet 和 1 个原 README；通用 `bind_corpus.py` 却引用外部 `ASSET_MANIFEST.json`。本次观察到的 `Corpus.cs` 尚未复算 `SourceAssetsSha256` 且没有 `SourceRoot` 属性；主任务应统一绑定位置并补验证，不能把该字段存在当作已验证全部原始文件。','',
        '机器可读逐来源清单：`'+str(output/'semantics.json')+'`。保留全部未接入来源理由；没有宣称 75 个来源都已整理或训练。']
    content='\n'.join(lines)+'\n';(output/'report.md').write_text(content,encoding='utf-8')
    report_path=pathlib.Path(a.report)
    existing=report_path.read_text(encoding='utf-8')
    if '## 75 来源的任务与答案语义复核' in existing:raise ValueError('Preserve existing semantic report; no duplicate appendix')
    report_path.write_text(existing+'\n'+content,encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k not in ('sources','source_assets_contract_notes')},ensure_ascii=False),flush=True)


if __name__=='__main__':main()
