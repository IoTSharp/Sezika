# 租期结束后的本机读回与模型镜像

适用于训练已经保存并推送、GPU租期结束，但完整读回或组织模型发布尚未完成的任务。文件运输、SHA与Git/LFS不需要GPU。此路径不重训、不连接旧实例，也不购买或续租。

本轮身份：private源模型 `maikebing/Sezika-mmBERT-8192-experimental` commit `e2e5bc97d62dfbd4e2bbd070542324bb544e6b32`；private数据集 `maikebing/public-decision-data` commit `d19515802d31310b4050226b51ab85c60f130d42`；public目标 `IoTSharp/Sezika-Decision-Base`，版本 `v0.1-preview`。这是本轮合同，下次任务先核新run/ref，不能复用为自动发布授权。

1. 核控制机容量，建立带归属marker的新专属根，预留源clone、独立缓存、恢复文件、发布clone和公开匿名读回共存空间。D盘不足时选择有容量的C盘任务根。所有外部进程通过项目 `Invoke-BoundedProcess.ps1` 记录身份、取消和墙钟上限，保留失败回执。
2. 用授权安全credential helper正常clone private源和数据集、固定commit；再下载完整LFS。Git LFS pull成功仅证明缓存对象：如果提示跳过checkout，需 `git lfs install --local` 和 `git lfs checkout`，直到工作树含真实字节。分别检查objects、pointers和实际逐文件bytes/SHA。原始数据的已知pointer属性错误保留FAIL，核其原Git blob和实际SHA，不修改原数据或混称缺文件。
3. 核模型底座清单、完整本run路径集合、package/checkpoint/weights/tokenizer/配置/来源许可。平台对象超限导致的optimizer运输分块，核每块和串联全流SHA，并在专属恢复目录实际写出完整checkpoint三文件后再核原始SHA。不执行仓库附带loader/helper代码，仅执行本项目审核过的文件工具。
4. 只从验收通过的模型白名单复制到当前目标commit，再审核小型报告的实际字段。模型、checkpoint与optimizer保持原SHA，验证报告只发分组汇总，转换路径与新SHA单独记录。保留目标既有文件和介绍原件，源README/SHA256SUMS分别映射为 `BASE_MODEL_README.md` / `BASE_MODEL_SHA256SUMS`，目标README展示审核过的完整模型介绍。记录owned和untouched精确路径及SHA，修复目标属性拼写时保存旧字节身份。private数据原件/derived records/ledger、详细验证记录、缓存和凭据无复制入口。
5. 安装普通LFS pre-push hook，核源HEAD/index、目标准确父commit、全部准备文件及stage后实际Git blob/LFS pointer。只有精准不可变notice来源文件可豁免历史尾空格，其余新文件严格检查。可在核完整OID字节后复用本任务源LFS缓存；普通Git/LFS fast-forward push，无force、无no-verify、无上传控制绕过。超时先只读远端实际ref，不能盲目写重试。
6. 发布后另起无凭据的新clone、独立空LFS缓存，核准确远端commit、完整LFS下载/materialize/objects/pointers、全部owned和保留文件bytes/SHA及原optimizer全流。只有这组公开运输和文件证据都通过，才能把IoTSharp模型完整发布置PASS。private数据仍留private，模型镜像不推数据到组织。

当前可复用审核工具位于 `.artifacts/repository-training-encoder-full-20261011-121827/local-mirror-contract/` 与 `local-mirror-preparation/`；实际验收回执见[本机接续证据](../../../../docs/evidence/repository-training-local-mirror-2026-10-11.md)，调用参数见工具旁的使用说明。工具绑定本run冻结合同，不能未经新合同审核套用下次训练。

文件发布PASS不改变正式质量、校准、新权重AOT与性能状态。本轮48步仅覆盖固定训练顺序前48题，不是完整一轮；所有GPU历史证据保持原范围。

公开内容审核必须深入报告字段，不能只检查文件名或没有raw/derived路径。`validation.json` 的逐条 RecordId、CandidateIds、Target、Probability、预测ID、教师argmax和逐条loss都属于详细验证记录。私有仓库保留完整原件；公开模型用 `validation-summary.json`，只保留包身份与按Split/Type/Rotated分组的数量、教师一致性和平均损失，原题与换序分母分别记录。相应更新publication-binding的summary SHA、run发布清单、README/MODEL_CARD及根SHA256SUMS。原weights、tokenizer、package、训练报告、checkpoint、optimizer和不引用验证细目的训练files.sha256可保持原SHA。生成的新汇总不能伪称原详细报告逐byte不变。

若详细验证记录已经进入公开Git提交，普通fast-forward仅能修复当前树；旧提交和平台对象留存是另一项门槛。保留实际旧ref、具体问题与当前脱敏证明，历史改写和平台清理由当轮明确授权控制。不使用force绕过技能的常规发布约束，也不把不可达对象自动写成已物理删除。

本轮已在用户明确授权后，以一次精确lease将master从 `c68049c735c24da5f1c611c2b229619bb4b391ac` 改为 `9c522c4e227c52c86f493a90595a894a41e1935a`，两者tree相同、文件差异0，保留安全介绍父 `2a410d64a70955364439514cba60207b8b9ee796`。fresh匿名全历史确认旧详细报告提交 `dfef170bc9e26dff7e4317fc08686d0abd013438` 与validation.json路径不再可达；最新55个owned+1个保留文件、6,174,855,372bytes匿名全SHA通过。push使用显式目标URL、禁用followTags/mirror/附带子模块，保留普通LFS hook；不修改private库或大LFS对象。完整证据见[历史清理回执](../../../../docs/evidence/repository-training-history-cleanup-2026-10-11.md)。平台旧对象/缓存物理清理仍未执行及未确认；该一次性授权不能复用到未来任务。
