# 自有仓库完整编码器训练交接（2026-10-11）

## 新模型本机消费与比较（2026-10-11）

新会话已独立完成当前48步包的C#加载、135张量/有限值/非零head/tokenizer/完整SHA与Windows Native AOT真实执行验收。显式入口是 `RepositoryTrainer predict-package`，只做推理，原fixed encoder loader及5090训练验证合同不改。本机RTX4070 Laptop 8GB足以在共同1024token strict/4GiB owned预算比较新包与旧冻结encoder/24576步head，无需新租机、旧服务器或重训；并非8192性能验收。

冻结SNLI/BoolQ/中英XWinograd原始参考答案256题，加192Choice换序，每模型448、合计896真实GPU前向成功，failure/refusal0，native资源归零。新119/256（46.48%）、旧126/256（49.22%），没有准确率提升；新原序Choice全部首候选、Boolean全部true、负类召回0/24、换序selectedID改变148/192。Score仅compatibility smoke、未做gold MAE。正式质量/校准/封存、人审、中文混合组件权利、Linux新权重AOT、8192/受控性能与正式发行保持NOT_ACCEPTED，Windows Native AOT实际执行证据单列。

逐题输入/答案/预测/失败仅位于 `C:\Users\mysti\.codex\artifacts\sezika-load-compare-20261011`，冻结输入SHA `0d3b51c9896e8ffb0e037d5eb4618a16f4577a45ab6a713ea308146cc1487830`。原始源split/SHA、可确认曝光重叠排除、同输入分母与拒绝测试详见[加载与比较证据](evidence/new-package-load-comparison-2026-10-11.md)。未进行训练/调参、教师调用、源commit/push、模型/data外部写入或历史改写；公开历史隐私收尾仍由原主会话独占，不以此消费/比较证据替代。

## 本机接续当前状态（2026-10-11）

当前状态 **PASS_AUTHORIZED_PUBLIC_MASTER_HISTORY_AND_ANONYMOUS_FULL_SHA**：用户已明确授权只改写IoTSharp公开master，单次精确lease推送与独立匿名完整模型/可达历史验收均PASS。最新提交 `9c522c4e227c52c86f493a90595a894a41e1935a`，tree `d7743fde522c22352aee1048b29f303924f6b5b1` 与c68049相同、diff0；55个owned文件+1个保留英文README共6,174,855,372bytes全SHA通过。fresh无认证bare全历史核旧dfef170及c68049均不在该分支可达commit，validation.json路径无记录，原安全介绍历史保留。平台旧对象/缓存物理删除仍为 **NOT_VERIFIED**，该平台清理未获授权、未执行。本次授权的分支历史范围已经完成，正式质量、校准、新权重AOT/性能仍 **NOT_ACCEPTED**。详见[历史清理证据](evidence/repository-training-history-cleanup-2026-10-11.md)。

private模型 `e2e5bc97d62dfbd4e2bbd070542324bb544e6b32` 与数据未改。模型已从新认证clone和独立LFS缓存完整下载/materialize；18个底座文件/1,245,737,297bytes与33个run文件/4,929,157,012bytes逐文件SHA通过。optimizer三分块逐SHA和串联全流通过，并在本机实际恢复checkpoint三文件；原moments为2,455,565,752bytes，SHA `2cd5a601d63c658598e5717ce4c05cd26d1197a14212f82f3b47fba4bfb83426` 一致。

private数据 `d19515802d31310b4050226b51ab85c60f130d42` 的新认证clone/LFS下载已完成，原275资产/1,076,726,933bytes与新8派生文件/64,322,231bytes全SHA通过，实际HEAD/index/跟踪集合一致。objects fsck通过，pointers fsck仍为FAIL且严格仅两个既有原始Git blob；原完整字节及Git blob身份通过，不改写旧FAIL或升级正式训练准入。数据集保持private，不进入组织模型镜像。

IoTSharp public **Sezika-Decision-Base v0.1-preview** 当前master为上述9c522c4。当前公开tree的validation-summary按Split/Type/Rotated汇总，metadata已核不含Rows、RecordId、CandidateIds、Target、Probability或逐题预测ID。weights、tokenizer、package、training-report、checkpoint、optimizer与原训练files.sha256的SHA保持，私有源缓存未复用。分支可达历史检查通过不代表平台物理删除验收；48步仍只覆盖固定训练顺序前48题。

完整本机资产根为 `C:\Users\mysti\.codex\artifacts\sezika-repository-local-mirror-20261011-192617`，源模型在 `model`，恢复状态在 `recovered-checkpoint`，私有数据在 `dataset`。本机私有读回、当前汇总模型发布、授权分支历史改写及独立匿名验收已经完成，无需GPU、新租期或旧实例连接。最新原回执位于资产根下 `mirror-state/transport-push-history-cleanup-publish-v2.json` 与 `mirror-state/transport-anonymous-readback-history-cleanup-readback-v2.json`；本机早期过程见[接续证据](evidence/repository-training-local-mirror-2026-10-11.md)。正式质量、校准及新权重GPU/AOT/性能是独立后续任务。匿名v3工作区展开失败源于先前取消任务遗留的0bytes Git index.lock；v5确认旧任务进程退出后归档保存原锁，再执行标准Git restore与LFS checkout通过，原FAIL及锁证据保留。

汇总脱敏阶段快照（保留历史，已由上述授权清理状态取代）：当时状态 **CURRENT_PUBLIC_SUMMARY_SHA_PASS_HISTORY_PRIVACY_PENDING**。提交 `c68049c735c24da5f1c611c2b229619bb4b391ac` 的55+1文件/6,174,855,372bytes匿名全SHA PASS；旧公开提交 `dfef170bc9e26dff7e4317fc08686d0abd013438` 的详细验证标签当时仍在可达Git历史内，历史改写/平台清理尚未授权，整体历史隐私收尾未完成。dfef170的55+1文件/6,174,921,683bytes运输/fullSHA PASS保留历史，源README/SHA256SUMS映射及原optimizer串联SHA不变。下方服务器租期结束PARTIAL、预算与全部FAIL也保留历史。

## 服务器租期结束快照（2026-10-11，保留历史）

当时状态 **PARTIAL_MODEL_PUSHED_READBACK_PENDING**，发布验收 **NOT_ACCEPTED**。48步完整encoder/head训练已保存，128原题+68Choice换序共196次独立GPU前向通过；正式质量、校准、新权重AOT/性能仍未验收。用户选择服务器直传，完整模型未下载到本机。

maikebing private个人模型实际普通Git/LFS push和远端master为 `e2e5bc97d62dfbd4e2bbd070542324bb544e6b32`，fresh checkout HEAD一致，weights与三个optimizer传输分块共4个新LFS对象约3.7GB上传完成。源完整pre-push验收原18文件/1,245,737,297bytes及新33文件/4,929,157,012bytes，完整optimizer原SHA及逻辑重组通过；远端fresh认证完整读回480秒超时（payload124、calls10、stage791秒），全量byte/SHA及读回重组未完成。旧7b9bd72超限与78024624运输失败保留，不能重复训练或盲目重复push。

private数据commit `d19515802d31310b4050226b51ab85c60f130d42` 已正常push。切private前原275资产/1,076,726,933bytes和新8文件/64,322,231bytes完整匿名snapshot通过；当前认证ref已确认，fresh认证完整读回 **NOT_RUN**，旧snapshot不替代它。

IoTSharp public **Sezika-Decision-Base v0.1-preview** 状态 **INTRODUCTION_PUBLISHED_MODEL_WEIGHTS_NOT_PUSHED**。仅一次README native PUT，实际master `2a410d64a70955364439514cba60207b8b9ee796`，无模型或数据权重写入。完整介绍7585bytes/SHA `26d3ea93ca51e44da33d46ebdeeb8ab4456f55891b4e7e1182eaff81784d82d2`，认证和独立匿名官方GET一致，原7397bytes模型卡后缀逐byte保留。README明确权重尚未发布，下载示例验收后启用；完整模型 **NOT_PUSHED**、完整匿名读回 **NOT_RUN**。此commit是文档，后续镜像expected master须使用 `2a410d64…`，原初始c23c3c42仅历史。

最终本机小包 `publication-evidence-v3` 安全解包/全文件SHA通过，382文件/3,399,257bytes；tar4,085,760bytes/SHA `c83caeab91b58d05c744532457e50ba64c1fb8137a3cc747e8887dc4a94666d4`。manifest仍PARTIAL，三份完整读回证明缺失。服务器UTC06:56:37核359注册行/332独立身份全部已退出或复用，无匹配存活/unknown，RAM凭据清理；历史FAIL不覆盖。Range诊断两段1MiB仅证明HTTP206和约0.44/0.65MB/s，加速助手仍draft、实机NOT_RUN。

用户已确认本轮租期**已结束**，最后已确认截止为 **北京时间2026-10-11 15:00（UTC07:00）**；15:30续租建议已收口，本轮不再等待确认或连接旧实例。服务器任务此前已退出，旧服务器磁盘产物的后续存续未复查。下次在新实例先核期限/ref及可见性，从已推送的个人private模型 `e2e5bc97d62dfbd4e2bbd070542324bb544e6b32` 完成fresh认证完整读回和optimizer完整重组、private数据fresh认证读回；取得完整源验收后从组织实际2a410d64基线镜像准确owned资产，普通push，再独立匿名完整模型读回。无需为补齐发布重复训练。不要把本机小证据、源pre-push SHA或公开介绍当完整发布验收。以下保留实机过程及历史预算/失败，以最终快照为准。

## 本轮实机过程（2026-10-11，保留历史）

新实例已完成三仓库固定克隆、275数据资产完整SHA验收、RTX5090 tiny梯度/checkpoint恢复复验，以及真实 mmBERT 三步 profile。最长训练题7740token，最大单步13.6047607秒，owned峰值12,017,759,752bytes；profile权重未导出。实际Linux派生SHA与Windows准备物仅换行不同，已逐行转LF核对，执行计划绑定Linux实际版本。

本轮当前截止为**北京时间2026-10-11 15:00（UTC07:00）**，用户已明确确认续租；原14:18/训练阶段13:48/1800秒下载回推预留快照保留，智能体未自行购买。用户明确选择**48步全参数短程实验、独立验证和回推**，并允许多个智能体并行准备。完整17615步无法按原保守预算在最初租期准入；续租不改变已执行48步，仍只覆盖固定训练顺序前48题，不能称完整一轮。已另建48步计划、重新profile通过，于12:49:54在guardian下启动固定训练，48/48完成并保存完整encoder/head、checkpoint及报告；训练报告测点78.615秒，guardian全过程84.260秒，资源回收通过。完整模型下载、独立验证和真实回推按[本轮实机证据](evidence/repository-training-real-2026-10-11.md)继续更新。

数据标准LFS pointer检查发现两个原Git blob被宽泛属性覆盖，原FAIL保留；objects及全部资产SHA已通过。这次训练不消费这两源。精简Ubuntu缺ICU导致首次tiny失败，官方源安装libicu74后新stage通过。GPU只由主调度操作；并行智能体分别处理工具、回推准备和证据文档，不写同一文件或并发GPU。

完整结果归档约4.929GB；D盘剩余约9.1GB不足以容纳归档、解包、发布和独立读回。第一次owned SCP中止及546,275,328bytes partial保留，第二次改存C盘专属目录。用户随后明确选择**服务器直接上传仓库，省去本机大文件转手**；第二次owned SCP44732核身份后停止，262,537,216bytes partial保留。当前按服务器独立验证→dataset/model正常Git/LFS push→独立新checkout完整读回执行；用户最新合同为dataset private、maikebing private内部预发布、IoTSharp public发布目标。private数据和个人模型授权认证完整读回，组织模型匿名完整读回。本机只保存小报告和证据，完整模型未下载到本机。独立新进程验证已通过：package SHA `36adeeb7531e6212229e8df318bfd8dfd930703140b3be75576e0a873f50c20e`，128原题+68Choice换序，共196次；train64原题CE1.3304532719776034/teacher一致36，development64原题CE1.059903010725975/teacher一致39。它是实验子集与teacher一致性，正式质量/校准仍未验收；真实push和完整读回按实证继续更新。

本机小证据已安全解包至本轮任务 `local-evidence-v2`，34文件/98,257bytes（33条内容清单+清单自身）。保存报告的独立分布/CE/argmax/分母/候选ID换序验算通过，CE最大重算误差5.745255466926835e-6，68个换序题中selectedID稳定67、改变1。原v1清单自包含错误及拒绝解包FAIL保留。服务器RAM凭据传递/范围/清理的3个dummy测试通过，但不替代真实带凭据push；真实发布仍待完成。

首次服务器直传publisher在 `dataset-staged-check` 执行 `git diff --cached --check` 退出2，14次调用后失败，尚未commit/push；guardian资源回收通过，真实凭据RAM清理exit0。根因是保留的source-rights.json原CRLF被默认blank-at-eol误报，619bytes及SHA `19825d7f1eecc673ae9ce380c7c29187e92ac24079c21c444780d17878847bbd`不改。按单次Git调用增加cr-at-eol，并以tiny fixture确认真尾空格仍拒绝。

首个恢复supervisor7974通过检查并生成dataset提交 `d19515802d31310b4050226b51ab85c60f130d42`（父08f1fdad），push却因helper拒绝Git输入EOF而Username unavailable/exit128；当时真实ref未变。修复后11项正负dummy和服务器真实Git探针通过。第二恢复8894因原raw两文件的已知LFS属性误显差异导致全仓worktree检查exit1，未到push；限定本次owned工作树核验通过，同时保留原pointer FAIL。三次失败和资源/凭据清理回执均保留。第三恢复supervisor9402核保存commit/parent/index/owned/SHA及真实ref后普通push既有d195，实际LFS/pre-push和ref读回通过。dataset匿名新checkout已完整下载并核原275资产/1,076,726,933bytes和新增8文件/64,322,231bytes全部byte/SHA一致。

模型完整包prepare/owned完整SHA通过，但第三恢复在model-staged-check/call57因四份来源notices原始尾空格退出2，未model commit。这些文件与trained原byte/SHA相同；generated metadata检查已通过，原件和FAIL保留。第四恢复10971冻结11份原notices准确路径集合/逐byteSHA，但用户已将dataset转private，原匿名dataset ref因此training-proof/exit65/calls58失败；凭据清理exit0，未model commit/push。认证call59确认实际dataset ref仍d195并审核旧完整snapshot，不能冒称private后新的完整认证checkout。第五恢复按private数据授权ref、public模型普通push/匿名完整读回继续；最后还须fresh认证dataset全部byte/SHA验收。单项model push/fullLFS预算900秒、payload2200/guardian2220秒，租期14:18不变，不改变用户visibility。当前模型真实发布结果尚待收口。

用户最新发布合同为maikebing private内部预发布、IoTSharp public发布目标，正式名称/slug **Sezika-Decision-Base**，48步产物为 **v0.1-preview**，实验质量状态保持。平台目标已独立核实：个人模型28817/repo50738359为private，认证metadata200、匿名metadata403/Git401；组织模型28824/repo50759976已从原实验slug改名，匿名metadata/Git200，初始master `c23c3c428beaece8a116c4d75ae51df2c97206d2`。模型字节push/完整读回仍待主调度回执；metadata200与创建/改名不替代模型发布。镜像范围绑定个人private模型授权认证完整验证的准确commit，只包含完整base及本run；private数据/derived、缓存、凭据和任务根不进入组织模型。组织正常push后另做独立匿名完整byte/SHA验收。当前15:00截止覆盖两仓库验收，原14:18预算仅为历史。

后续用户已明确续租至2026-10-11 15:00（UTC07:00），这取代上述最初14:18期限；主调度保存原deadline快照并更新当前deadline.json。分块恢复tiny7项payload通过，但Windows原guardian因过快进程记录CleanupFailed/unknown1保留；精确PID75396/launcher9112及75396直接children当前均已消失，audit后只删除task/transport-tiny-fixture-v1的23个临时成员，原日志与FAIL不改。真实transport-personal-stage/guardian13213正确拒绝根base encoder/tokenizer仍是LFS pointer的pre-push SHA；trained新weights正常，主方仅从已验底座相同原SHA补齐base并另建stage，不用旧encoder替换新run。

第五恢复保存日志已确认model本地commit `7b9bd72ac952e427b4a2580c50d81c51585d3c57`生成，但model-push/status1/calls68/elapsed8秒失败，凭据清理exit0；moments2341.81MB超平台2048MB单对象限制，旧commit未发布并保留。新传输版本仅分块moments，清单固定原完整SHA和每块bytes/SHA，读回重组核原完整SHA后才接受checkpoint，package/receipt不改。先核真实ref并把失败commit保留独立ref/工作区，避免超限旧对象仍在新待推祖先中；不force、不删moments。最终模型、组织镜像和private认证完整读回仍待实际回执。

自己的完整中文组织模型卡及193字符平台短介绍已准备：[本轮模型卡](../.artifacts/repository-training-encoder-full-20261011-121827/iotsharp-model-card.md)，标题 `Sezika-Decision-Base v0.1-preview`，SHA `e4688c01565356caff45eee79344d43bb7ba9c044f0f5d41336538c96d1e0ba0`；[可复用中文模板](templates/model-card.zh.md)。初版7c545原字节及旧出处保留为initial文件。参考用户指定官方Decision Model介绍结构，出处/页面抓取SHA及自己package-report-validation身份保存于本轮任务 `iotsharp-model-card-sources.json`；模型卡没有把Serverless API、质量提高或新权重AOT写成已支持。平台description和组织根README展示此介绍，原base README/SHA256SUMS分别保存为BASE_MODEL_README.md/BASE_MODEL_SHA256SUMS并以映射记录原SHA，新介绍与新总清单单独验收。

## 离线准备阶段结论（保留历史）

用户已选择完整微调 mmBERT encoder 和决策 head，先完成 C# 反向工程。离线工程、真实原始数据转换、tiny GPU 数值/恢复、Linux 工具和本地发布模拟已有证据。可以租用所给 RTX 5090 环境进入**真实底座 profile 验收**；完整正式训练是否准入、需要多久，必须由新实例的显存和最长序列耗时决定。本轮未连接旧服务器、购买实例、执行真实 mmBERT 完整训练或向真实仓库回推。

目标环境：CUDA 12.8、RTX 5090 / 32 GB、25 CPU 核、48 GB RAM、30 GB 系统盘、50 GB 数据盘。实际 driver、GPU UUID、磁盘剩余容量与新租期仍待读取。历史实例租期已在北京时间 2026-10-10 00:22 截止，不能复用其连接或资源证明。

## 固定输入与本地准备物

| 输入 | 仓库与固定 commit |
| --- | --- |
| 源码 | `https://github.com/IoTSharp/Sezika.git`，`9ee9eda7a45d41fa0f3a283e9377094ff6885760` |
| 数据 | `https://moark.com/maikebing/public-decision-data`，`b8ea13b81aaaca722baf6a3c0f12e2217fc4353e`；后续发布回执 commit 为 `08f1fdad70add4e0dd442ec3c796242df596c669` |
| 模型 | `https://moark.com/maikebing/Sezika-mmBERT-8192-experimental`，`06ebe82325e58c0ae723fb9014a38cdb2f87b22a` |

模型与数据在离线准备时的公开状态已有完整匿名Git/LFS/SHA历史验收，见[模型发布实证](evidence/moark-personal-publication-complete-2026-10-11.md)及[数据回执](research/public-decision-data-2026-10-10/publication-receipt.json)。本轮用户后续将dataset改为private，当前认证合同见[仓库合同](../.agents/skills/sezika-repository-training/references/repositories.md)。发布成功不替代训练数据准入。

新代码仍在本地工作区，源码远端 commit 尚不包含本次新工具。Linux 使用固定源码 checkout 加冻结自包含工具包；`engineering-sources.json` 分别记录源码 commit 和本次工具实际源文件 SHA，不能混称已经提交源码。

- 项目技能：[sezika-repository-training](../.agents/skills/sezika-repository-training/SKILL.md)。下次可直接说“使用 sezika-repository-training 准备/执行本轮完整微调”。
- 准备任务：`D:\source\Sezika\.artifacts\repository-training-encoder-full-20261011-prep`。
- 最终工具包：上述目录的 `tool-bundle-v6`，398 个被清单覆盖的文件，`files.sha256` SHA 为 `2b40e72cee968dba9b4b7da8355ae432f8412c7cd0d3c97ea77e26b18dab0e96`。旧 v1–v5 保留，不作为最终交付。
- 真实派生数据：`D:\source\Sezika\.artifacts\repository-training-engineering-20261011\derived-admitted`。
- 执行入口、SSH/期限模板和固定参数：准备任务中的 `task.json`、`connection.template.json`、`deadline.template.json`、`run-plan.template.json`；详细命令见[执行合同](../.agents/skills/sezika-repository-training/references/execution.md)。准备模板不是可直接运行的新服务器计划。

## 已完成的反向与训练工程

`EncoderBackwardKernels`、构建期 `BackwardKernelCompiler`、静态 `CudaEncoderBackwardKernels` 和 `CudaFullEncoderTrainer` 覆盖线性、norm、GELU、attention/softmax、RoPE、embedding、三类 marker soft-target CE、AdamW 和 finite 检查，共20个 backward PTX/ABI。模型、算子和调度均为 C#，GPU 运行时走静态 CUDA Driver，不安装训练框架或 bridge。

完整 FP32 encoder/head 梯度及 Adam first/second moments；反向按层重算前向，owned GPU 上限24 GiB；调用串行。训练固定数据顺序、cursor 与 Choice 换序位。checkpoint 包含完整权重、两份 moments、进度和 plan/data/order/PTX 身份，保存 SHA 完整后才生成 receipt。只保留最新两份完整 checkpoint，失败/取消和 poisoned 状态不得导出成功模型。

新完整模型包以 `package.json` 为入口，独立验证必须提供 `--package-sha`；保留旧 loader 的固定底座 SHA。包携带实际 encoder/head、tokenizer、配置、checkpoint、训练报告、来源、组件许可证和逐文件清单。旧 tokenizer 的精确 Gemma 上游 model/revision 仍未知，整体许可保持 unknown，不统一改写成 MIT/Apache。

## 已适配数据

首轮只适配固定 revision `f31d5a3f3da828021c792edf5ab4b93cf60fe01b` 的 `kaivoss/system-one-270m-data` 原始 train：25,002行，原件59,900,933 bytes，SHA `c49ef3295387765178b29cdbd1565413ba6e04ed20880e72352f6a5834d7cf94`。采用声明 Apache-2.0 与相应 rights decision，保留实际来源 README、rights 和逐行派生账本。teacher soft targets 不代表实测正确率。

| 最终分母 | Choice | Boolean | Score | 总计 |
| --- | ---: | ---: | ---: | ---: |
| train | 9,908 | 6,651 | 1,056 | 17,615 |
| development | 2,381 | 1,650 | 267 | 4,298 |
| accepted | 12,289 | 8,301 | 1,323 | 21,913 |

隔离3,089行：2,994个无法确认升序语义的 Score、54个不支持的 prompt 布局、2个候选边界问题、39个超过8192 token。duplicate0；最长 accepted7740token。第一次长度检查21,913/21,952、退出3的 FAIL 保留，另建 admitted 派生版本后21,913/21,913 PASS，不裁剪原文或改写旧失败。

normalized 原始 state SHA作为 family，前8位mod10<2为 development；同原文全部题保持同 split，不消费原始 test。尚无语义近重复、人审、正式质量与其余17来源的适配，因此 `FormalTrainingAdmitted=false`、`ExperimentalOnly=true`。这次可以执行实验完整微调，不能宣称全仓库混合训练或正式质量通过。

| 离线Windows admitted 文件（历史准备物） | SHA256 |
| --- | --- |
| data-manifest.json | `f7d15d200b0a11170204a862d120bd7965d18586024605d739bc1e70e379f2e1` |
| training-records.jsonl | `a9f4d75af468202504b33c24b2e25eb1b8c75d1d8e53c176e4aaaff5a458e783` |
| derivation-ledger.jsonl | `6999ee1992a5a75b85eea9f6d9cb0a46d15a69ea3d7cf1aaa9fbf4a4872e9fc0` |
| SOURCE-README.md | `0fd8f682eee4144d47c0f2911389963c390cc39f93cd7cca9af9c0f4f96d90bf` |
| source-rights.json | `19825d7f1eecc673ae9ce380c7c29187e92ac24079c21c444780d17878847bbd` |

给旧 derived-full/admitted 附来源文件后，前三文件 SHA 完全不变；Linux2行 prepare→lengths→admit、来源复制与重复覆盖拒绝通过。

本轮新实例实际Linux admitted文件身份如下；这些SHA用于训练和发布，不用上表的历史Windows SHA替换。原始raw未改，逐行内容转LF对照已通过，证据为本轮任务 `lf-reconciliation.json`。

| 实际Linux admitted 文件 | SHA256 |
| --- | --- |
| data-manifest.json | `ccf7193de9efed49e9c6f511ac54907de13354a50761a8f808ad4f8462559bdf` |
| training-records.jsonl | `9d62559b6e668dc14dd79e3819f6afbc984e7ab87366c983a3c66080527c18e6` |
| derivation-ledger.jsonl | `b649c02f61a18c76858f096e5ed74d083ea955dd53abbc88917f661afb59aa76` |

## 新实例执行与回推

1. 取得**新** SSH host/user/port/auth、可信指纹（如有）、创建时间、租期截止与用户目标时间。凭据放安全存储或进程内存，不进URL、argv、配置或日志。新建正式 run-id，不覆盖 prep/旧实验。
2. 只读核 driver、GPU UUID、32GB显存、CPU/RAM、数据盘剩余容量、boot ID及现有compute使用者。全部资产、缓存、checkpoint放数据盘；不停止他人进程、不自动reset/reboot。GPU/SSH主调度并发1。
3. 上传冻结工具包、核398文件SHA，guardian `/usr/bin/true` 探针通过后，在新数据盘任务根克隆三个仓库、固定commit、标准basic Git LFS单并发、核真实原件清单与SHA；不运行数据仓库 loader。
4. 重建数据合同与严格长度、小GPU梯度和保存/恢复，填实际Linux路径、admitted manifest SHA及GPU UUID，冻结 run-plan。
5. 真实底座 profile 做3次更新：同一train题两次与最长train序列；第二次核第一层QKV非零梯度，核显存、forward/backward/update耗时及回收。dev最长更长时仅以train输入追加EOS作明确合成shape诊断，dev标签不得进入任何反向。profile权重不作为正式初始化。
6. 按真实profile和剩余租期准入固定训练，encoder/head均从底座/零head开始。模板一轮17,615updates、seed17、encoder LR1e-5/head LR1e-3、decay0.01、每128步checkpoint。CLI保守估算不足就拒绝；不能悄悄缩减训练步数或承诺未测时长。下载/独立验证/回推另留预算。
7. 完成后完整保存新字节并核SHA，按所选运输路径执行本机下载或服务器直传。新进程从新完整模型 GPU 前向，固定64train+64development及全部Choice左移；这是实验子集验收，正式语言质量、概率校准、新权重AOT/性能仍独立。
8. 用户本次已授权训练完成的模型与实际变更数据回推，并在执行中选择服务器直传。派生数据放 `derived/<run-id>/`，模型放 `runs/<run-id>/`；先完整保存、manifest/owned-path/属性审查，再正常commit/push。数据有变更先推送并核实际ref，模型绑定该真实数据commit后推送；没有数据变更则跳过数据提交。禁止force、改写raw或关闭LFS pre-push。凭据仅在受保护stdin/内存或受限任务RAM路径，finally回收。
9. 每个仓库独立新checkout、实际完整LFS/byte/SHA读回。本轮数据集已由用户转private，授权认证读取/回推并明确authentication=true；public模型用匿名读回，保持用户visibility。push失败先按可见性只读核ref及LFS，不盲目重写；ref/平台注册/完整授权下载/完整公开下载分别报告。保留失败回执，核本任务进程和资源收口。

本轮模型/data回推授权有效；源码push和购买实例未获授权。未来使用技能，以当轮请求重新判断外发授权。完整工程验证详情见[实证](evidence/repository-training-engineering-2026-10-11.md)。
