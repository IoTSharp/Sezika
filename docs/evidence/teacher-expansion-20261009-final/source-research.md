# 真实公开数据来源研究与有界采样（2026-10-08）

本轮已提供可复用的官方MASSIVE 1.1中英文原始文件及tiny2；停止新增来源检索，不调用教师、GPU、凭据或Git。256是首批流程规模，不是最终质量样本上限。许可、原始金标、教师标签与正式人工金标分别记录。

## 可立即使用：MASSIVE 1.1

官方原始数据URL：<https://amazon-massive-nlu-dataset.s3.amazonaws.com/amazon-massive-dataset-1.1.tar.gz>。许可证据来自AmazonScience官方HF仓库固定revision `ff6bd8e4b27c3543e4f8fe2108f32bb95a6f8740`：数据卡声明CC BY 4.0，`massive.py`和`LICENSE`包含Amazon版权声明及Creative Commons Attribution 4.0 International全文，含复制、修改/翻译授权及署名、来源链接、许可链接、修改声明要求。来源是对SLURP语音助手交互的本地化，不能称所有locale为彼此独立的自然文本采集。

许可链接：<https://huggingface.co/datasets/AmazonScience/massive/raw/ff6bd8e4b27c3543e4f8fe2108f32bb95a6f8740/LICENSE>。署名保留Amazon.com Inc.及作者Jack FitzGerald等；保留MASSIVE论文与SLURP原始来源引用。MASSIVE题文转换、候选生成、教师标注和翻译须声明是本项目后续修改，不覆盖源intent/slot标注。

实际HTTP200，ETag `51e0da2a3ff7a016f109e1d1b4306e93-3`，Last-Modified `2022-11-07T16:55:04Z`。文件实际40,251,390 bytes，SHA-256 `4CBA5FAA11C71437928E17CB1B9B3D8B8E727E7EA363A3A9A8045E19C0491577`；下载耗时51.746秒，直接10秒失败后仅一次proxy7890。完整下载回执在sources/massive-1.1.tar.gz.receipt.json。

精确归档成员为两个regular files，路径经核对，无链接、无路径越界；没有宽泛extract：

| 本地原始文件 | 归档成员 | bytes | SHA-256 |
| --- | --- | ---: | --- |
| sources/en-US.jsonl | 1.1/data/en-US.jsonl | 3,904,197 | C70F75C6A543A26E249EC383DF67733AD9B1066F6C0406C2E04A3F03356E407E |
| sources/zh-CN.jsonl | 1.1/data/zh-CN.jsonl | 11,818,747 | 992BF0BEF3D678F08C27E514739BC851163E8F40F530BFB4D5970A2C24408ACE |

sources/massive-tiny2.json包含不同utterance家族的两条原train记录：en-US/id1为“wake me up at nine am on friday”，zh-CN/id2为“设个两小时后的闹钟”，原intent均为alarm_set。保留完整original_record，不调用教师。

原始字段包含id、locale、partition、scenario、intent、utt、annot_utt等；源partition必须保留。en/zh对应同id是同一源utterance家族，应采用 `massive-1.1-utterance-{id}` 跨语言绑定；训练/开发不能拆开同家族，采样需使用互不重叠id，不能把翻译计为独立原始来源。新source record id使用dataset/version/locale/id，旧32及其general-*家族不混入，根任务另做原文/近重复排查。

该数据提供60 intents、55 slot types、51语言、超过100万本地化utterance的扩量潜力。首批可选择较多领域及intents构造2..8候选Choice，并按intent/scenario分层、保留原始train/dev划分。scenario分类、intent分类、slot识别是不同原始任务，不能为了覆盖Score/Boolean将它们改名为相应金标；原工人judgments是语言/意图标注质量，不是本项目五级语义评分金标。

## 可核许可的下一来源：BoolQ

官方README：<https://raw.githubusercontent.com/google-research-datasets/boolean-questions/master/README.md>。已读取其明确声明：BoolQ为 naturally occurring yes/no问题，15,942例；官方train9,427、dev3,270；题目、passage、answer三元组；采用Creative Commons Attribution Share-Alike 3.0，链接<https://creativecommons.org/licenses/by-sa/3.0/>。应保留Christopher Clark等作者、论文、原数据URL和Wikipedia passages来源，派生题文/翻译须标变更，分发派生数据须保留相应署名/ShareAlike义务；不把数据许可直接等同于最终模型权重许可结论。

本研究尝试官方storage.googleapis.com/boolq/train.jsonl，直接及一次proxy均HTTP403；没有成功下载、没有覆盖或冒造数据，没有继续试dev、登录或绕过。GitHub公开API还出现rate-limit，无法固定repo commit；README许可证据不伪称已取得固定git revision。

root随后独立取得官方HF镜像metadata及train/validation各100条rows，并通知本研究停止新增下载。可用任务路径为sources/boolq-hf-metadata.json、sources/boolq-train.rows.json、sources/boolq-validation.rows.json；这些文件由root负责其dataset revision、hash、许可证镜像一致性和采样绑定，本研究未冒充独立复核这些新增文件。

BoolQ适合“依据给定段落回答真假”，不能宣称评测任意世界事实或将回答失败填成false。question+passage原文保持，重复Wikipedia页/段落合并family，源train与validation不相互搬运；中文翻译是新派生来源而不是中文原生金标。优先建议root的160 MASSIVE Choice（128train32dev）+96 BoolQ Boolean（64train32dev）共256计划；如果BoolQ准入尚未闭合，已授权fallback为256 MASSIVE中英Choice，明确Score/Boolean未新增相应监督。

## 程度评分与其他候选的实际状态

C-MTEB/MultilingualSentiment和SetFit/amazon_massive_intent端点返回Invalid username/password，本研究没有访问任何凭据，不能据此宣称其数据或许可已确认。Amazon Multilingual Reviews原1–5星、SST-5等仅是下一阶段待核方向，未在本轮核实、下载或授予准入。二分类情感不能虚构为五级金标；源评论星级是用户评分而非客观正确率。

若下一阶段补Score，可明确创建独立原创阅读强度/显式信息计数合同，由教师盲标并作为AI伪标签，保留原创作者、来源/题文hash、rubric和推导方式，不冒称公开数据集的原等级标签。此类工作不在本研究中自动执行。

## 预算、资源与保留限制

最初8MiB cap下未下载官方tar；root在新下载开始前明确冻结为1个官方tar≤128MiB/180秒、研究总20分钟、成员≤512/30秒、两selected files各≤16MiB，之后又允许BoolQ train/dev各≤16MiB/60秒。最终仅本研究成功下载1个原始data archive；成员列表实际58项，三次tar子进程分别84792/78684/72340均经Invoke-BoundedProcess 30秒界运行并Succeeded/exit0，精确任务树由wrapper收口。Windows固定PowerShell7，实际7.6.6。

一次包含失败清理Remove-Item的初始长命令在创建进程前被自动审批拒绝(blocked by policy)，未执行或产生文件；没有重试被拒绝的删除动作。后续安全下载脚本只保留并报告失败残件，本轮实际未产生部分data文件。成功archive、精确源文件、tiny样本、下载及process回执为交付物，未删除用户文件或共享缓存。

公开元数据请求中的失败原样保留：官方HF viewer拒绝旧dataset script；SetFit/C-MTEB端点不可用；GitHub API限流；BoolQ原始云bucket403。不把非终止PowerShell错误的exit0当作数据请求成功。

来源研究共4项目/11个唯一请求URL，未突破8项目/12URL；没有模型API、GPU、数据库、安装、Git或Graphify。source-readers.json登记各reader完整PID/start/argv/父链及真实工具退出回执；sources/process-logs保留tar wrapper identities/results。没有专门临时目录或待删除下载残件，root执行最终精确身份absence检查。当前文件是来源证据，不恢复ROADMAP、不修改trainer或正式数据准入状态。
