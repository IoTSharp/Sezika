# 三个仓库与版本

2026-10-11租期结束后的本机接续已完成private源模型完整认证读回及optimizer实际恢复、private数据集原275+新8文件完整认证读回；旧下文PARTIAL/NOT_RUN为当时历史。组织发布最新状态见[本机接续证据](../../../../docs/evidence/repository-training-local-mirror-2026-10-11.md)，接续路径见[本机镜像](local-mirror.md)。补齐发布无需再租GPU。

下列是 2026-10-11 本地实证核对到的已验收版本，并非今后永远使用的 latest；每次任务固定实际输入 commit，冻结之后不跟随默认分支漂移。

| 用途 | Git URL | 已验收输入 |
| --- | --- | --- |
| C# 源码 | `https://github.com/IoTSharp/Sezika.git` | 任务创建时准确 HEAD；未提交的新工具通过本次冻结工具包交付，不能冒充远端 commit 已包含这些字节 |
| 原始数据集 | `https://moark.com/maikebing/public-decision-data` | `b8ea13b81aaaca722baf6a3c0f12e2217fc4353e` 数据；`08f1fdad70add4e0dd442ec3c796242df596c669` 是追加发布回执的提交 |
| 模型底座/模型输出 | `https://moark.com/maikebing/Sezika-mmBERT-8192-experimental` | `06ebe82325e58c0ae723fb9014a38cdb2f87b22a`，默认 `master` |

数据发布版本 18 来源、275 个资产、1,076,726,933 bytes；模型原始 18 文件共 1,245,737,297 bytes，完整 encoder 1,227,772,549 bytes。两仓库在当时公开状态下已有匿名完整 Git/LFS/SHA 历史验收。用户最新仓库角色为：数据集 **private**；maikebing 模型为 **private 内部预发布**；IoTSharp 模型为 **public 发布目标**，正式名称/slug `Sezika-Decision-Base`，本次产物版本 `v0.1-preview`。private 数据和个人模型读取、LFS、回推及独立fresh checkout完整byte/SHA均使用当轮授权身份，明确authentication=true；组织public模型独立匿名fresh checkout完整LFS/byte/SHA。正式名字和公开发布角色不提高实验质量状态，本版本仍仅48步/未正式质量、校准或新权重AOT验收。每次新任务核实际visibility及合同，不把匿名读取作为private永久门槛，不在缺少当轮明确授权时改变用户可见性。private仓库匿名401/403是预期权限状态，不能当成要求重新公开的故障。数据源和标签训练准入仍独立于仓库可见性；读取逐源 `RIGHTS_MANIFEST.json`，不能给整个镜像统一重新许可。

本轮dataset输出提交为 `d19515802d31310b4050226b51ab85c60f130d42`（准确父08f1fdad），追加 `derived/encoder-full-20261011-121827/`。切换private前已验原275资产和新增8文件/64,322,231bytes完整匿名字节；这保留为历史公开snapshot，不能冒称private之后的新认证checkout验收。最终private ref确认和独立认证完整读回另记。认证凭据仍不进URL、argv、仓库配置或日志，只使用受保护stdin/任务内存或准确tmpfs任务目录，读回后完整清理。

本轮用户额外授权IoTSharp组织的**public正式发布目标**，从maikebing private内部预发布模型准确已验证commit同步完整base18文件及本run，private dataset不镜像。组织计划目标为 `IoTSharp/Sezika-Decision-Base`，实际URL、默认ref、public可见性和当前写入/LFS权限必须实测固定，不能把计划名当已创建仓库或套用旧组织权限失败。源个人模型按授权认证完整读回，组织保留许可/来源、完整encoder/head/checkpoint/tokenizer/配置与包入口；普通push后独立匿名完整byte/SHA读回。准备、创建/改名、push、ref、完整读回分别记录，未写入时为PREPARING_NOT_PUSHED。任何private dataset原件、derived、凭据、缓存或任务目录不进入该模型发布。

2026-10-11本轮平台目标实测已确认：maikebing model28817/repo50738359为private，IoTSharp model28824/repo50759976已改名为 `Sezika-Decision-Base` 且public，默认master初始commit `c23c3c428beaece8a116c4d75ae51df2c97206d2`。个人认证metadata200/匿名metadata403与Git401，组织匿名metadata/Git200，旧组织实验slug404；准确小回执为本轮task `publication-target-state-v1.json`。这些结果验收目标角色及改名，不验收新的模型字节，仍须独立normal push和完整认证/匿名读回。后续任务重新核实际ref及visibility。

证据入口：

- `docs/research/public-decision-data-2026-10-10/publication-receipt.json` 和 `download-guide.md`。
- `docs/evidence/moark-personal-publication-complete-2026-10-11.md` / `.json`。
- `docs/model-credential-storage.md`：本机凭据目标名；不从记忆、文档或命令输出读取/传播真实值。

MoArk 的已验收公开下载是 Git + 标准 Git LFS；本数据仓库的 HF 风格 resolve 曾实际 404。普通 LFS 曾在 IoTSharp 组织受企业限制，而个人 maikebing 库标准上传/verify/pre-push 已实测通过；不要把组织的失败推广到个人库。gai callback HTTP200/true 曾仍然 missing，完整 BOS 存储也不等于 Git ref 发布，因此首选标准 Git/LFS basic，保留默认 pre-push。

2026-10-11新实例发现：固定数据commit的宽泛LFS属性把 `raw/upstream/clinc150/data_full.json`（普通Git blob，2,495,390bytes）及 `raw/hf/PolyAI/banking77/dataset_infos.json`（普通Git blob，5,892bytes）标成应为pointer，标准 `git lfs fsck` 的pointer部分实际FAIL。保留原回执，不改写raw或原checkout属性；单独 `git lfs fsck --objects` 实际PASS后仍须核275资产完整byte/SHA及两文件原Git blob身份，才能决定输入准入。不能把objects通过改称pointer通过；出现其他缺失/损坏对象或未知pointer问题应停止并核对。本轮训练来源不消费这两个数据集。

[clone-inputs.sh](../../../../tools/repository-training/remote/clone-inputs.sh) 已把这项核对纳入可复用流程。例外仅适用 `b8ea13b81aaaca722baf6a3c0f12e2217fc4353e`，原始标准 fsck 必须退出 1，原诊断必须恰为上述两条 `unexpectedGitObject`，新增、重复或无法识别的诊断均拒绝。单独对象检查退出 0 后，校验固定 `ASSET_MANIFEST.json` SHA `fef8c839922ab676b9552a5fa7e0690345037e76a3f1a8ce81683a9aadd66438`、`RIGHTS_MANIFEST.json` SHA `9428702a8ad92133c3173ff1200ee3f21e461216eabfaa5eadad3b55ea84eed4` 和全部 275 资产的实际 byte/SHA。再以 `git cat-file` 核原提交中两个普通 blob 的类型、大小、内容 SHA，并核 checkout 中对应原字节：clinc150 的 SHA 为 `36923c3705a59e08fe9c3883d8bc2dd966ef93e22cb78ac41171782a698d56e0`，banking77 的 SHA 为 `402fed10f4894f8989dffeca0a81572ca488e6488fac3f18a33d9fd32422acc7`。辅助 Python 只检查文件与 Git 身份，不执行数据 loader 或模型计算；仅在此已知异常分支要求系统 `python3`。

任务根保留 `dataset-lfs-fsck.*`、`dataset-lfs-objects.*` 及 `dataset-known-lfs-reconciliation.json`。准入结果记录 `PASS_content_admission_only`，pointer 状态仍为 `FAIL_known_attribute_mismatch`，`formal_training_admitted=false`。此内容准入不能代替标签、split、许可与正式训练准入；模型仍须独立完整校验。每次新工具包须重新冻结这个脚本，旧 bundle 不自动获得修复。本地 12 个小型 fixture 已覆盖两条已知诊断、未知诊断、错误 commit、原失败回执、实际内容 SHA、损坏/缺失/重复/越界/链接资产、对象成功回执与取消；它们不构成新版脚本已经在实际服务器完整复跑 275 资产的证据。

模型 checkout 根含已训练旧 head 与底座；新训练从 `model.json`、`encoder.safetensors`、`tokenizer/tokenizer.json` 读取底座，新 run 输出另存。不要将旧训练报告当新报告、将原 checkpoint 当零初始化。将所有输入 commit、manifest SHA、原始 source revision、转换版本、采样/分组/去重、实际每类型和语言数量写进新报告。

## 2026-10-11最终ref快照

本轮最终为 `PARTIAL_MODEL_PUSHED_READBACK_PENDING`，发布验收 `NOT_ACCEPTED`。private dataset实际master `d19515802d31310b4050226b51ab85c60f130d42`，正常push/当前认证ref及切private前完整匿名snapshot通过，fresh认证完整读回NOT_RUN。maikebing private模型实际普通Git/LFS push/master为 `e2e5bc97d62dfbd4e2bbd070542324bb544e6b32`，fresh HEAD同值；源完整SHA与optimizer逻辑重组通过，fresh认证完整读回480秒超时，未取得全部模型/分块重组读回验收。旧7b9bd72/78024624失败提交保留。

IoTSharp public model28824/repo50759976当前master `2a410d64a70955364439514cba60207b8b9ee796`，状态 `INTRODUCTION_PUBLISHED_MODEL_WEIGHTS_NOT_PUSHED`。仅README单次native PUT已发布，7585bytes/SHA `26d3ea93ca51e44da33d46ebdeeb8ab4456f55891b4e7e1182eaff81784d82d2` 认证和独立匿名官方GET一致；原7397bytes模型卡逐byte后缀保留。权重NOT_PUSHED，完整匿名模型读回NOT_RUN；该文档commit不是model artifact commit。下一次镜像先核当前ref并以2a410d64为expected master，原c23c3c42只作历史初始值；保持private dataset不镜像。

最终382文件小证据SHA通过不能替代上述完整读回。用户已确认本轮租期结束，最后已确认截止为北京时间2026-10-11 15:00；15:30续租建议已收口。新实例优先读取已推送的个人private模型 `e2e5bc97d62dfbd4e2bbd070542324bb544e6b32` 完成完整认证验收及optimizer重组，再接续组织发布；不依赖旧服务器仍可访问或其磁盘产物仍保存，不为补齐发布重复训练。
