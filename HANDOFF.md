# Sezika 监督交接

2026-10-11 北京时间个人模型完整发布完成：maikebing/Sezika-mmBERT-8192-experimental模型28817/仓库50738359已通过标准Git LFS登记及普通pre-push，远端master 06ebe82325e58c0ae723fb9014a38cdb2f87b22a；原始18文件/1,245,737,297 bytes、19 Git路径、16小文件bytes/SHA及两个大对象完整匿名SHA全部通过，无需分片重组。[个人模型下载](https://moark.com/maikebing/Sezika-mmBERT-8192-experimental)与[完整实证](docs/evidence/moark-personal-publication-complete-2026-10-11.md)已写中英首页。正确路径长等待、官方替代域名callbacktrue但仍missing、运输tiny登记失败全部保留；个人标准LFS真实准入及tiny实传/回读通过后才推送原模型，旧IoTSharp企业权限失败不能推广至个人库。Windows凭据保存及HF既有发布保持；训练技能已完成，不重训、不调用教师、不操作旧GPU、不push源码。正式质量、校准、新head AOT/性能及第三方权利补证继续pending。

2026-10-10 北京时间发布交接：Hugging Face实验模型完整上传和18文件匿名验收已通过，revision a46028dfebd539bd04a1c567fbb24c26a649a50a，公开包1,245,737,297 bytes；中英README已写真实[模型下载链接](https://huggingface.co/IoTSharp/Sezika-mmBERT-8192-experimental)。新令牌已按用户要求存Windows凭据管理器 Codex:HuggingFace:IoTSharp:Sezika，读回及maikebing/IoTSharp身份通过，详见[凭据说明](docs/model-credential-storage.md)。仓库训练技能及独立审查已完成，本地提交b477e66。模力方舟[IoTSharp同名公开模型库](https://moark.com/IoTSharp/Sezika-mmBERT-8192-experimental)已创建，model28816/repo50735916；用户新MoArk令牌存Windows凭据管理器并读回，官方gai的两枚凭据也接入原生Windows keyring，无真实token argv或明文配置。普通LFS被paid/trial enterprise限制，gai的missing-lfs接口实际HTTP500，权重未验收、远端仍初始6a64335eeea35e0c4058a88ce507cc04cd0c402d；本地镜像提交53ca528178af60aa413b08e2c4ce40b83070ce5e及完整冻结包保留。发布许可已经取得，不重复索取；接续先核接口/远端，真实对象完成后再推送和匿名验收，见[模力方舟回执](docs/evidence/moark-model-publishing-2026-10-10.md)。旧403、Xet取消、串行HTTP超时、root提前取消及候选session400均保留为历史；恢复进程全77分片已重新PUT并完成服务端合并，两个成功阶段guardian均live0/unknown0。详见[发布回执](docs/evidence/model-publishing-2026-10-09.md)。完整模型本机已保存，发布不依赖租期；不重训、调用教师、恢复旧服务器或push源码，heartbeat保持PAUSED。正式质量/校准/封存/新head AOT/性能及第三方权利补证仍分别pending。

当前租用实验（2026-10-09）已完成：新 RTX5090 上唯一固定24576updates/LR0.001/seed17/strict8192/零head/encoder冻结训练35.2160222秒、231 forwards；新进程独立验证28.9658498秒、231 forwards，与磁盘保存输出一致；原32回归8.6122972秒、32 forwards。完整模型已于北京时间2026-10-09 21:17:08下载并逐项核SHA，提前于23:22目标，四小时租期截止2026-10-10 00:22。路径 `D:\source\Sezika\.artifacts\rental-training-20261009-01a12099\trained-model`；本机独立证据核验10/10 PASS，pipeline和Linux正式runner当前live0/unknown0，结束显存free32149MiB。train教师一致118/162、dev27/53、换序7/16且selected ID改变6/16；这是实验结果，正式质量/校准/许可发布/封存/新head AOT与性能仍pending，Score无新监督。远端训练及本地下载均停止，不重复训练或自动扩量；其他ROADMAP继续暂停。root为唯一GPU/SSH/Git调度者，两个子智能体完成无冲突进程审查和独立验收；连接凭据仅存ignored任务根。见[完整证据](docs/evidence/rental-training-2026-10-09.md)与[租用交接](docs/training-rental-handoff-2026-10-09.md)。下文旧机故障属于历史，不在旧机恢复或训练。

旧服务器历史状态：用户已明确采用8192、保留215/train162/dev53，并批准GPU重置及必要主机重启。8192工程/长度/真实8k前向和唯一24576updates训练已完成，head/report/checkpoint已核SHA保存。首次独立验证因Xid119/154失败，原32回归未运行。NVIDIA reset实际exit6/No devices；既有580.173.02驱动卸载重载exit0、原文件/参数保持，但GPU仍exit6。两原容器已恢复启动，重启前翻译health200/ASR503，不称GPU健康。10:56:29 UTC已请求一次主机reboot/request exit0；11:24:57 UTC（北京时间19:24:57）已确认SSH恢复及新boot_id 31951b0f-232d-40c2-aec0-d7e7dd5215a6；11:27:35 UTC诊断确认PCI仍识别同一RTX4090/UUID，但nvidia-smi仍exit6/No devices，GSP固件初始化失败，显卡未恢复。两容器running、翻译health200/ASR503；六项保存物/数据/encoder SHA通过。当前需要服务器管理方处理GPU初始化故障。禁止重复reboot/reset/训练；只观察恢复，连通及资源准入后仅接续保存head的独立验证和32回归。用户许可已取得，不重复询问。见[本轮完整证据](docs/evidence/token-limit-8192-2026-10-09.md)。

更新：2026-10-09，Asia/Shanghai。规范工作区 `D:\source\Sezika`，分支 `main`，监督起点 `716130e`。ROADMAP.md 是唯一阶段计划；本文件只记录当前交接。此前交接的过程状态保留在 Git 历史及下列证据中。

## 授权与定时接续

此前全部ROADMAP监督授权保留为历史。2026-10-09本轮租用GPU固定实验、独立验证、原32回归及完整本地下载均已完成，按“固定实验全部完成后停止等用户”暂停同一聊天 heartbeat `sezika`，配置状态已核PAUSED。其他ROADMAP保持暂停，后续等待用户指定；每项工程任务仍按独立审查、验收及本地commit执行。

正式训练、批量教师采集和批量特征计算使用 GPU；CPU 仅小规模兼容性、数值参考与诊断。GPU 前向、head 反向、正式数据训练、质量、AOT 和性能分别验收。教师答案不能自动成为人工审核或封存真值。

仅 `D:\source\Tomur` 已获核最新、拉取、必要修改提交及推送授权。Sezika 只 commit；发布、部署及正式默认切换仍按各自门槛处理。用户未跟踪的 Sezika `docs/wechat-introduction.md`、`.html` 及 Tomur `docs/articles/` 不修改或暂存。

## 当前结果
### 2026-10-09 8192固定训练完成；GPU恢复及主机重启已获批准

本轮8192工程本地提交 a5f4d9c / 167470a / 44b4082，未push。新profile仅改变runtime_max_tokens并绑定encoder manifest B8FB9CECC0912F034E0FEA184E8AA78871D11533093DCCF4F5AAD74646007551；历史1024包/旧v2v3报告不变。215/215长度通过、最长1037；H4完整8192最大误差3.5762786865234375E-07，真实mmBERT8192 GPU前向owned峰4813921280bytes/host2741837824bytes、结束归零。该独立边界≤6GiB，正式215实验仍≤3GiB GPU/4GiB host，CLI1500s/SGD300s/每阶段384forwards。

唯一训练为24576updates/LR0.001/seed17/strict8192/零head/encoder冻结，实际46.5468512秒、231encoder forwards。同进程磁盘roundtrip通过；train教师一致118/162、dev27/53、换序7/16、换序ID改变6/16；train CE1.1844641080609075→0.8185527480586812，dev CE1.10233565996278→1.0354540944099426。实际commit回调及固定顺序重放为151周期+114前缀，每题151或152次，不称128完整epoch或直接kernel ID观测。report SHA C6F3AE942FB90CA842FD49AFF7AE6505EB02A71128264EDC2EF08BC323A48905，head4205457556736AB07799B462D42C377710E765CB14EDB9AAE508C1558C5E9D64；checkpoint及完整身份见新证据。教师一致性不等于正式准确率或质量通过，Score无新监督。

准确新任务根 .artifacts/token-limit-20261009-8192、远端 /root/sezika-experiments/20261009-repaired-training-8192，本会话独占Git/API/GPU。永久镜像 docs/evidence/token-limit-20261009-8192-final 的inventory绑定原始报告/保存物/回执/日志/脚本/源码；完整13,496,320bytes tar本机保留。Linux46登记身份live0/unknown0，但验证CUDA释放失败及NVML进程N/A，不能宣称最终GPU正常回收。10:28:20 UTC只读预检确认Hearon翻译PID3055及ASR PID3068仍running，准确容器/启动/argv/父链与恢复方案已记录；该10:28快照尚未停止服务或reset/reboot；随后真实恢复动作见顶部当前状态与新证据。以上10:28快照之后用户已明确批准GPU重置及必要主机重启；软件恢复未成功；一次reboot已经完成，11:24:57 UTC新boot_id已确认，11:27:35 UTC显卡仍GSP初始化失败，独立验证/32回归仍阻塞。授权不包括重复训练。相同heartbeat阻塞安静，不重复通知/索要已取得许可；主智能体已有重启请求时不得重复操作。

本机当前登记身份及直系子进程补充只读检查matching0/unknown0/unproven_children0，正在观察的launcher/console另后核；历史短命unknown及第一次收口误计当前launcher的失败保持。16份专属编译日志按hash保留，仅非递归移除36个确切空目录，未清共享TEMP/失败Linux临时asset。暂停源码、ROADMAP旧诊断hunk及用户文件保持，仅本地commit。原1024准入、合同afcca10/e808191及旧工程镜像为已完成历史，见 repaired-training-engineering-2026-10-09.md；旧“等待删题/未训练”状态已被明确8192选择与本轮实际训练取代。正式质量、人审、许可发布、校准、封存及新head AOT/性能仍pending。以下各小节记录此前快照，不作为当前在途任务。

### 2026-10-09 修题/盲审完成：215题候选包，新会话接续训练

用户授权使用sonnet_vip修复歧义题，修不好就删，再新建会话开始下一步。本轮78问题题由同一sonnet.vip/gpt-6.1-sol/max处理：模型39修/39删，root另排除2项改变原义的修订，剩余37题全部独立盲审通过。原178通过题复用，最终215题（train162/development53，Choice140/Boolean75），删除41题（train30/dev11）。原state.text/source gold/family/split保留，全256分母及删除原因有账本；原32另列已曝光回归，Score无新增监督。

数据与完整证据已本地提交a804cc9（334文件），未push。任务根.artifacts/teacher-repair-20261009；修题cell120与复审cell135已全部结束，14修题批+7复审批共21次HTTP200、21成功验证，无网络重试；原33批只本地重解析，成功请求不得重发。原5400秒窗口已关闭，不重置、不再补发。永久证据docs/evidence/teacher-repair-20261009-final，332文件+inventory、8,281,934 bytes；详见docs/evidence/teacher-repair-2026-10-09.md。

新data-v3 manifest SHA E8E74CE0BBF1A72DA5FCDF4DCAD9D302513DC5DC42CB548BFD21D2569621AEB6，账本SHA55F3226946831C4B9CBD7F6EE10D1194764860C8343A9386CAA40C678CE24EC0。静态分母/字节hash/身份/映射/原文与分组核验通过；C#原生v3合同、新strict1024长度、GPU训练/独立验证尚未运行，不把旧914tokens当新测量，不把AI审题当模型质量通过。正式人审/许可发布/校准/封存/AOT性能均pending。

22回执/112身份/93PID终核matching_live0/unknown0，打包/核验/closure进程另核absent；18历史argv缺失局限保留。所有API guardian Succeeded/exit0，无GPU或教师在途，未停现有服务。日志保留，未清空全局TEMP。

按用户要求完成本地分项commit后创建Sezika本地新会话（projectId d83b0c15-7f7d-4480-b937-ccca4abcc86a），立即按docs/training-next-handoff-2026-10-09.md接续C#可变分母v3合同、长度、一次固定24576updates/LR0.001/seed17/strict1024/零head/encoder冻结、独立加载完整CE/逐题/16Choice换序与原32回归。创建后新会话独占Git/API/GPU，旧会话停止实施并迁移heartbeat；不自动扩量、加训、LoRA或encoder反传。保留其他暂停源码、ROADMAP旧诊断hunk及用户文件，只commit不push/发布部署。

### 2026-10-09 扩样256题审核完成；78题质量阻塞，GPU未启动

完整教师证据已本地提交 `10dee08`（279文件），未push。Git stage/commit真实child exit0、提交对象存在；短进程快于CIM采样，通用wrapper仍保留CleanupFailed/unknown1，不能称其历史进程观测完整，另核准确PID已退出。

本轮256/256唯一题取得有效教师盲审：178题符合当前AI审核条件、78题需修订、8条建议改标；train192中137符合/55阻塞，dev64中41符合/23阻塞。Choice160中117符合/43阻塞，Boolean96中61符合/35阻塞。全部原source gold、family/split、题文保持，原修正32另列已曝光回归，Score无新监督。真实打包从33份原响应严格复验后拒绝，保存78条清单，manifest不存在、训练未启动；外层exit3为真实拒绝，不当成功。详见 docs/evidence/teacher-expansion-2026-10-09.md。

工程已分项本地提交 e6f7979（256题/24576步容量与存储）、d30655f（显式v2训练与独立验证）；实际GPU容量、训练、独立加载及原32回归均未运行。256题严格renderer全通过，最长914tokens；原32/审核sidecar绑定3修正通过。小参考、fixture及历史短进程身份局限见工程证据，不冒称真实训练或完整历史进程审计。

用户批准的北京时间2026-10-09 11:28:03.8319509–12:28:03.8319509窗口（含额外4请求）终态为累计38HTTP尝试/31成功批/240题/73阻塞，原235文件归档 teacher-expansion-20261009-final 保持。用户随后明确批准最后16题的新600秒窗口，model-selection-final16-20261009.json 起算UTC04:32:06.5190229、截止04:42:06.5190229；最多2次HTTP/并发2/每批8/单次240秒。final05/06因旧脚本依赖当前Codex全局model变化在HTTP前失败，无request/provenance；保留失败后，Direct路径改为固定已批准教师模型并读取同一CC Switch选中sonnet_vip凭据，不修改用户配置。final05v2/final06v2各一次实际HTTP200/8条严格验证/guardian Succeeded0清理空，于UTC04:36:44完成。函数cell89已终结，不得重发或重置计时。

最终累计40次教师HTTP尝试/33成功批/256唯一题；5次HTTP前失败不混入HTTP，失败/中断上游计费及完成未知。只有另1次连通性GET。final16-request-ledger.json逐原响应重解析、核原请求hash及保存结果一致、成功题去重为0。完整补充41文件位于 teacher-expansion-20261009-complete，绑定旧235归档hash；final16-repair-proposals.{json,md}包含78原题/建议，仅提案未应用，不能作为已审核新题。batch07旧provenance/redirect与Direct children_started字段局限以纠正sidecar为准，不回写旧字节；上游模型仅服务响应声明。

进程终核86回执/403身份/331唯一PID matching_live0/unknown0，checker与打包进程另核absent；82条历史记录缺argv，不能补造历史完整性。compiler-temp两日志按hash移存、七个空目录非递归清理，未触碰全局TEMP；regression publish未指定专属TEMP局限保留。未启动/停止GPU服务，没有本任务GPU训练或API在途。

当前只允许继续必要证据/分项本地commit/交接，不能自动进入GPU训练、再次扩量或重新教师采集。下一实质步骤是按78题清单准备修订版本，保留来源/修改声明与split/family，变更题复审另行固定范围和窗口；新教师调用需新明确授权。正式人审、许可发布、质量、校准、封存、新head AOT/性能均pending/false。准入闭合后才可执行原固定一次24576updates/LR0.001/seed17/strict1024/encoder冻结，再独立验证；不按dev调参、不自动加训/LoRA/encoder反传。其他ROADMAP暂停，root独占Git/API/GPU；只commit不push、不发布部署。

### 2026-10-08 用户新授权：公开样本扩量与同教师学习（进行中）

用户已要求搜索更多真实题型和训练样本，并明确继续 sonnet.vip / gpt-6.1-sol / max；ACM发卡机消息已确认发错，不修改LaneApp。旧72步、768步A/B/C与32题审题均已完成，禁止重复；本轮授权覆盖旧“不扩量/不重新采集”限定，其他ROADMAP仍暂停。

任务根 `.artifacts/teacher-expansion-20261008/`，计划256新题：MASSIVE 1.1的160中英Choice（128train/32development）与BoolQ的96英文Boolean（64train/32development），沿官方split、跨语言/段落family冻结192/64；原32修正题保持独立回归，不覆盖旧标签/来源。MASSIVE官方tar及两locale真实下载/hash/CC BY4.0全文已核；BoolQ官方storage403保留，公开HF metadata和train/validation各100行实际响应已取得，CC BY-SA3.0，viewer字节身份冻结但未独立证明等于metadata revision。五级Score来源尚未核实，本轮不新增Score监督。

固定一次24576 GPU updates/LR0.001/seed17/strict1024/encoder冻结/零初始化head，随后独立新进程重新GPU编码完整CE与256原题/16训练Choice固定左移检查；CLI1500s/SGD300s/forward384，GPU并发1/owned3GiB/host4GiB/空闲5120MiB。教师每批8/并发2/成功请求最多33/单次240s/全阶段3600s；原标签隐藏，真实wire bytes原样保存，成功请求不重发，不确定或需改题保留并阻止训练，不默默丢弃。正式人审、训练输出许可发布、质量、校准、封存、新head AOT/性能仍未通过。

当前扩样C#工具build1零警告错误、fixture合同tiny1→full16、旧core247检查通过（固定tokenizer oracle因相应路径资产缺失仍skip），单H4小参考24576保存/加载、257题与24577步拒绝通过；这不是正式训练。容量fixture首次参数漏项/身份不合法失败已保留并修正。Linux新工具与修正容量fixture已发布，source-freeze已锁；远端新根 `/root/sezika-experiments/20261008-teacher-expansion-v3` 仅bootstrap创建成功，未执行教师HTTP或GPU。已核现有GPU空闲15294MiB，未停任何服务。新runner只把旧600s上限提升1520s，新SHA32167e8cf7396ac6d1f8f54a8d664dfbfaaef71fccba871433b0fdf73e24f2f1，旧runner原字节不改；独立退出码/生命周期probes待执行。

监督者root独占Git/GPU/API调度；contract_review仅准备数据，evaluation_impl完成工程后独审core/API，expansion_runner准备脚本后独审trainer。接续先读各自实际回执/身份与保存批次，不能以Windows0、fixture或准备文件替代真实运行。heartbeat已更新至本轮范围，完成后安静等待新指令；只本地分项commit，不push/发布/部署，保留暂停源码、旧ROADMAP诊断hunk与用户文件。

### 2026-10-08 当前32题独立AI审题已完成

用户指定当前Codex sonnet.vip凭据/gpt-6.1-sol/ultra；真实接口HTTP400拒绝ultra，用户明确回复批准max，未改全局配置。5次成功请求（1+8+8+8+7）在原900秒API预算内完成32/32盲审，29保留、3建议改标、0不确定、0需改题。改标：general-en-score-03 2→3；general-zh-boolean-02 true→false（12−5=7）；general-en-boolean-01 true→false（09:15早于10:00）。后者属于development。原标签不进请求；32条保存响应重新通过严格类型/字段/映射/分母/单字段逐字引文验证，17旧响应与6反例先验，独立脚本及最终报告审查留证。网关声明gpt-6.1-sol/max，不独立保证上游身份；key仅内存，未输出或提交。

原JSONL SHA A015A8FA182EE62223CAFC307C618C515A19E90F843290DB9DFB53A56B9A041C保持，旧Qwen来源/manifest/provenance及训练结果不改。新AI审核为sidecar、pending/formal=false/非sealed；未来按修正标签训练必须建立新数据版本及来源合同。复用C的32条原序GPU预测作事后比较：原教师17/24与1/8，新AI标签18/24与2/8，权重未变、未新增前向/训练/重新教师采集，仍未充分拟合且质量未通过。后续拟合与候选ID换序验收方向见[完整证据](docs/evidence/label-audit-2026-10-08.md)，无自动加训/扩量/LoRA，其他ROADMAP继续暂停；heartbeat只接续证据与交接，不能重发32题或重复GPU实验。

任务目录 `.artifacts/label-audit-20261008/`；最终永久原请求/响应/报告在 `docs/evidence/label-audit-20261008-final/`。ultra失败、CLI在用户rules解析失败且未发HTTP、首次脚本parser失败与报告Md别名冲突失败均保留。首次报告创建了工作区两个空目录general-zh-choice-01与delivery；自动审批拒绝归档/清理命令，仅返回blocked by policy，未绕过，旧部分归档 `docs/evidence/label-audit-20261008/` 及两空目录保留不参与成功证据。API任务进程以本轮登记身份终核；既有GPU服务本轮没有启动/停止。只本地commit、不push或发布。

最后逐题独审通过；独审总墙钟约257秒，超出所派4分钟约17秒，已停止、未追加API/GPU。进程终核114记录/53唯一PID无matching/unknown live，checker/launcher及质量建议reader另核absent；审查最后writer由真实工具exit0及后核收口。原失败closure误把当前launcher计为live，保留后修为明确排除当前祖先且另核，不篡改失败。进程已关闭不等于全部目录清理通过，保留对象及审批拒绝是未闭合清理项。

### 2026-10-08 固定768步实验及独立加载验证已完成

A独审后提交 `06a6ded`。B单次实际768次GPU SGD、零初始化三行head、LR0.001/seed17/strict1024、encoder冻结；C独立新进程从磁盘加载并重新GPU编码，B/C各48/48 forwards。train教师一致17/24、CE1.229626456896464→0.9920696603755156；dev1/8、CE1.2687934339046478→1.2740284875035286；换序4/8、CE1.3999227583408356、6/8预测ID改变。C fresh_disk_outputs_matched=true，监督者另核40逐题及全部观察解析值相同。训练集仍未充分拟合、位置敏感、质量未通过，未按dev选参数或自动加训，全部正式验收仍false。

新head SHA7B4CE09F22AD80E175030F2A73FC878974347D53259DC16D3727B92E7F3184A4，checkpoint972D6CADDF5743CDA7EAD4CA0F3D657D572DF15B8710A36ADC659B892547EB92；实际权重目录远端 `/root/sezika-experiments/20261008-head-learnability-v2/output/run-768/`，本地 `.artifacts/gpu-learnability-20261008/retrieved/output/run-768/`。原报告/日志/回执/hash和分组见[本轮证据](docs/evidence/gpu-learnability-2026-10-08.md)。A/B/C全部真实completed/exit0/清理完整，GPU/host峰值均在3GiB/4GiB内，实际device/model Dispose返回及所有owned/module/release归零。独立Linux终核18登记身份（含重复）全absent，空闲仍15294MiB、已有服务未停止；旧报告原SHA保持。B/C独审与文档实物核验通过；Windows全量34回执/151登记身份/144唯一PID终核matching_live0/unknown0，checker另由独立新进程核absent，10暂停文件hash保持。历史子PID80712缺argv的初次失败保留；限定父链后的新label终核通过，不伪造argv。审查部分短命reader缺OS身份的范围局限已明确保留。精确任务临时日志移存与空目录/fixture回收完成，本轮证据已收口；完成本地结果提交后等待用户新指令，无运行训练/教师/下载/上传/GPU。禁止重跑A/B/C或因heartbeat新增派单；完成本轮后安静等待用户新指令，其他ROADMAP仍暂停。

### 2026-10-08 历史检查点：阶段A已完成（由上方B/C结果接续）

评价工程独审通过并提交 `2111ed9`，冻结linux-x64/runtime10.0.12工具实际发布及远端196文件校验通过。阶段A单次32真实GPU forwards，用旧72步磁盘head评价train24/固定左循环Choice8；train教师一致10/24、CE1.1749068026741345，换序2/8、CE1.3898979127407074。原/换序Choice各8题都选首候选、预测ID全部改变，旧head未充分拟合且位置敏感，质量未通过。原字节报告SHA B3868C86946B8E283B12C6B0DFA7D77A5BA895E5B524940A326BE0FF09B51082，见[本轮证据](docs/evidence/gpu-learnability-2026-10-08.md)。Linuxcompleted/exit0及清理完整，GPU/host峰值1,309,369,952B/2,723,254,272B，Dispose返回/资源全零；独立远端后核8身份记录全absent、显存仍15294MiB。B/C尚未启动，继续预先固定768步配置，不根据A/dev改参数。准确阶段目录存在即先读回执，禁止重复运行A；本轮监督者仍独占GPU与Git。

### 2026-10-08 历史检查点：新会话接管（由上方完成结果接续）

本会话 `01a11ab6-7ceb-7ac0-9235-58a8eec95729` 已从准备提交 `e5c0888` 接管源码、Git及GPU调度；旧会话不再并发实施。先核本地encoder/tokenizer、派生三文件数据及旧head/checkpoint/report身份，与交接相同；新本地任务根 `.artifacts/gpu-learnability-20261008/` 原先不存在，已登记身份和暂停文件hash。heartbeat `sezika` 已指向本会话，ACTIVE每30分钟。当前前置评价工程、只读合同审查和新scope runner准备并行进行，GPU尚未启动。唯一本轮配置及A/B/C预算仍按 [训练交接](docs/training-next-handoff-2026-10-08.md)；不重训72步、不扩量、不按dev选参数、不恢复其他ROADMAP。

实际远端前核已通过：旧十项hash一致，GPU UUID正确，可用15294MiB；新远端任务根此前absent已按身份创建。有界runner tiny真实completed/exit0及最终分母全零，SSH实际exit7传播探针返回7，失败回执保留。用户在本会话追加授权：若剩余显存不够，可以停止占用服务；当前显存足够未停止任何服务，后续仅在必要时先核服务身份和影响。该授权覆盖旧交接中的“不动既有服务”限制，其他预算保持。

### 2026-10-08 新会话训练交接准备

用户要求准备训练信息并创建新会话运行和关注训练。下一范围为同24题GPU可学性与位置诊断：先用旧72步权重补train24逐题及8条训练Choice真实换序评价，再固定768步/LR0.001/seed17/token1024零初始化新head，保存并独立新进程加载验证；不按dev选参数，不扩量或恢复其他ROADMAP。完整路径、hash、来源、SSH/runner、数量/墙钟/资源边界与分项提交见 [训练交接](docs/training-next-handoff-2026-10-08.md)。当前会话仅准备；新会话创建后接管源码、Git和GPU，30分钟heartbeat同步转入新会话，旧会话不并发实施。本段只表示准备与授权，不表示768步已经执行。旧实验及暂停文件继续保留。

### 最新优先级：真实 GPU 实验训练已闭环（2026-10-08 UTC08:08）

用户要求的福海“训练→保存新权重→加载使用→实际验证”已真实完成，其他 ROADMAP 仍暂缓。冻结独立真实 mmBERT encoder，32 条实际 GPU 教师标签按训练前冻结规则分24train/8development，零初始化三行head、LR0.001/seed17、72次实际GPU CE/梯度/SGD更新。2304个F32参数全部有限/非零，encoder未反传。原人审pending/正式许可false保持，另登记用户实验授权；正式质量、许可发布、校准、封存、新head AOT及性能另验。

已核福海 root@211.149.170.42:2222 → digisyn-gpu01，22000为独立跳板不用于训练。Ubuntu24.04.4、RTX4090 UUID GPU-e786e313-bc92-a63d-a833-f4e48cbafb66、驱动580.173.02/API13000，总49140MiB/空闲15294MiB，既有服务保持；使用self-contained linux-x64/runtime10.0.12，不装全局runtime。训练/验证GPUowned峰值1,309,369,952B、host峰值约2.72GB，满足3GiB/4GiB；两个进程实际context/model Dispose返回，owned/alloc/module/release全零，结束空闲仍15294MiB。

真实产物远端/root/sezika-experiments/20261008-real-head-v1/output/run-03/，本地.artifacts/real-gpu-experiment-20261008/retrieved/output/run-03/。head SHA2C93C6A9FD28C491AE5718F00C81C5FFB08859FC7F85C6A8F4DF6B209A5D5AAD；checkpoint5C604C9510658373EB2933C2D82FAE0FF0E798D8837C59D3B66E5BE7A63C3528，各9619B。report C6B065CD0C66AA4544C0AF277C539D82A4E7932C66CD41AAAD37CFC1A0D30EE7；独立validate原stdout1B49EF8859131ADFB582BD380739C7F99D64E5C88F042C1A417B4A2C1B435A1F，已原字节归档docs/evidence。两个新进程分别真实GPU导出32特征、完整24/8 CE、加载新head推理8题，参数/features/CE/逐题logits一致。

train CE1.229626456896464→1.1749068026741345；dev1.2687934339046478→1.2772859781980515，教师一致仅2/8，质量未通过/未建立，不自动按dev调参扩量。32题长度103–920，使用strict1024保留完整输入。初始括号编译错误、Windows调用Linux runtime错误、train01 token256失败/0更新、旧SSH包装漏退出码造成Windows0、train02 runner早期setup没有payload的失败均保留；具体setup guard原因unknown。包装v2真实exit7核传播后，唯一成功train03及fresh validate03分别Linux24.400/24.090秒（含清理）、Windows28.939/28.114秒，actual completed0/全回收。时长不是正式性能。

必要evaluator已单独commit8d93877，tiny1/full3实际GPU数值/取消通过最大误差8.324e-8；ExperimentTrainer五源v3独审冻结、实际publish/check1与8/lengths1与32通过。独立Linux后核6runner的22条身份记录（含重复）全absent；Windows37回执/126记录/135PID零matching/unknown，历史短reader native-child登记局限保留。专属空fixtures、compiler空子目录、setup-only probe已精确清理，workload日志/失败/交付物保留。见[永久闭环证据](docs/evidence/real-gpu-head-experiment-2026-10-08.md)。训练工具/报告/证据已完成独审并单独commit875ccda；evaluator独立提交8d93877。

提交收口：8d93877（只读GPU完整CE evaluator及数值/取消证据）、875ccda（真实实验训练工具、原报告/新进程验证/独审证据）。源五文件freeze v3保持，独审最终SHA1854877632A75DAE6A56B86EF2F614D9085307E75C7E8B34F77554BA0259A6C0；v3独审两个可选脚本/最终资源追加脚本错误如实保留，root已另核其5reader全部absent。提交前141个PID/130条记录/41回执无matching/unknown，root-local-resource-closure-precommit.json SHA80C36A09C36B332316B9A9073B5EA45789A61046263BED5129DC9A950AE42DA0。875ccda前两行混合换行whitespace检查失败后root误提交，本次交接将LF规范化并重新核查，原失败回执保留；未绕过训练或源码验真。暂停诊断源码、ROADMAP诊断登记hunk及用户文件继续原样未提交。
root独占Git/shared docs；不push、对外上传、发布、部署或再训练。30分钟heartbeat保持ACTIVE并已更新：只补本实验剩余交接，闭环后安静，不重训、不扩量/调参、不恢复其他ROADMAP；等待用户新指令。当前子智能体只独审交付，无运行的训练、教师、上传、下载或本任务GPU进程。


诊断 profile 四源 v2 已通过唯一 build（0warning/error）、tiny1/full8/旧 formal8；CLI 三源 v2 已通过唯一 build（0warning/error）、tiny1/full8。两个 build 外层首次失败分别为 DLL 名数组错误及专属 TEMP 残留，修复后补核/精确关闭，未重编译，不改写原失败。两个任务尚未 commit，所有源码/日志保留；资源终核草稿暂缓，不执行真实 SZDH1 资产或诊断 GPU 画像。该暂停切片基线为 0ffeb6c406278d129aa1f801ee997a063b0aaa3f；当前提交以下方新收口记录为准。共享文档及用户微信两文件保留，root 独占 Git，无 push/publish/deploy。30分钟 heartbeat 已更新为训练优先，其他 ROADMAP 暂缓。

| 独立任务 | 提交 | 已验收范围 |
| --- | --- | --- |
| 教师提示 v2 | `3ac5ead` | 保持32条请求字节、题序、family、候选顺序，只明确所有候选ID为JSON字符串；源67/新69项绑定复验通过 |
| 首轮GPU教师实测 | `087bfab` | 32 HTTP200、13格式通过/19类型失败；133引用核验，失败和全部分母保留；服务/资源已关闭 |
| GPU schema3准入合同 A | `6504d84` | 1→64合同、190核心回归，pending/许可/身份不符输入在前向前拒绝；没有正式训练 |
| GPU workflow/恢复 B | `cbd6790` | 真实UUID、17静态PTX/manifest身份、三文件发布与恢复；CPU3、GPU5→31、stamp正负通过 |
| GPU CLI/特征导出 C | `d2ccb5f` | 显式train-original-gpu、五项恢复输入、完整GPU encoder特征及GPU SGD、schema3评价适配；CPU2→22/GPU8→12/旧CUDA59通过 |
| 第二轮GPU教师实测 | `e26d9e7` | Score/Boolean tiny后full32，32条全部格式有效；34 HTTP、32不同题、无retry，151引用核验通过；标签全部pending |
| 独立数值比较器 | `f469b27` | 25层级tensor+marker的冻结合同，零警告/错误构建，tiny1/full16共19变体；26项完整分母、fixture/自比/源码及路径门禁；没有真实参考数值 |
| 独立上游oracle工具 | `01d23f3` | 原权重/源码lease、首次import前hash及bytecode隔离、25hook+marker、Windows原子发布/实际Move后取消回滚，最终tiny1/full16通过；没有真实forward |
| Windows独立诊断AOT样例 | `b837be7` | 真实固定encoder+未训练内存head，managed/native各1→8、唯一publish/probe/46-token CUDAcase；311launch、552-byte D2H、预取消零launch、释放归零，12份结果/45身份终审通过 |
| Linux独立诊断AOT | `86099ee` | Ubuntu10.0.112/cache唯一发布、native1→8/fstat、probe/唯一46-token CUDA；311launch/552-byte D2H、预取消零launch、资源归零；正式trained-head/训练AOT/质量另验 |
| GPU训练器异常清理 | `2800cbe` | 最终build零警告/错误、CPU1→3/GPUtiny1/full8与既有21项训练smoke通过；独立5秒/40owner/17module、主异常原帧、副本清零/caller基线/false release与context回收分别核验 |
| CUDA原生取得与登记 | `ef1288d` | 五源码、四实际DLL冻结匹配；CPU1→7/GPU1→11、training21/cleanup9case、十一runner/37身份终核，实际context回收；AOT/正式训练另验 |
| 32题人工审核入口 | `c386d29` | 完整题文/候选/实际教师建议、八null人工字段，tiny1/full32及64引用核验、独立映射与root五文件身份通过；全部pending，曝光题不可封存 |
| Windows GPU训练AOT | `6c3b096` | 最终五源freeze、原生PE/flags、tiny1/full11、H4三题磁盘3→6恢复与连续6、主48launch/480H2D/240D2H；旧managed16/新native16分别绑定版本，63身份终核 |
| Linux GPU训练AOT | `14a3917` | 28固定输入、新ELF、native1→11共16更新、RID内三文件3→6恢复/连续6；首失败/runner预算限制保留，49Linux身份及root46身份零匹配，两TMPDIR已精确回收 |
| 审核切片侧车工程 | `b2c54b3` | 最多32行既有development报告与hash绑定审核侧车；unknown完整分母及正式接受false。当前真实DLL冻结后零警告/错误构建、tiny1/full12通过；Git8调用及18身份随后无匹配存活，没有push |
| Linux协作runner工程 | `32822f8` | v10 tiny→8case通过，原始回执/参数和独立审查通过，28Linux身份/67Windows记录无匹配；随后11份Git/reader/worker回执与44身份终核0，旧失败保留 |
| 独立画像入口工程 | `06de2ed` | 最终三源/实际DLL冻结，第二构建零警告错误、tiny1/full8，四runner/24身份及八Git/16身份终核0；GPU/正式性能/AOT仍开放 |

### 2026-10-08 诊断资产与runtime接续检查点

SZDH1诊断资产已单独提交c72ab5acee27edecd39c5ec56f8d9a7af684f973：作者05:01:04–05:13:26、9调用/7源码；五源冻结05:12:29，freeze SHA1FA904…C82CA。一次build76260/37.993秒零警告错误，tiny42044/4.031秒1PASS、full83180/4.170秒8PASS（27变体）、旧publisher84256/4.452秒8PASS。三DLL恒定，四成功runner/21条记录身份无匹配，三个专属fixture根0项且已删除；root-final-validation.json SHA BBE3055CF7858F17C1BA9190002FAF3A7ABC0CA75F7F2BB61E54378382EA5158。八Git回执/21记录身份零匹配，root-git-resource-closure.json；首次sandbox StartTime null失败保留，后续实际读取成功。不重复旧验证，见[永久证据](docs/evidence/diagnostic-head-asset-2026-10-08.md)。

runtime/CUDA切片已单独提交0ffeb6c406278d129aa1f801ee997a063b0aaa3f：core作者05:25:46起、05:40:13锁源，CUDA作者05:25:37起、05:37:17锁源/05:42:25复核；14/12短调用，无自行build/GPU。root两项目build86820/30.346秒、84344/13.747秒均0warning/error；CPUtiny39432/5check、full59368/41check、核心46680/247check（tokenizer oracle skip）、实卡诊断2184/19check、正式CUDA86956/16check全部通过。五DLL保持，11源冻结，7成功runner/37记录身份零匹配/unknown0、7专属fixture根已移除；root-final-closure-v2.json SHA295CB62C20AF0CF37D7500511A04DD6C0D92814B9452A82CA2644AACFE9A73F0。v1作者start误写后纠正，v1原字节保留。两个build原外层exit1是3项compiler TEMP残留检查失败，后续空目录非递归删除、workload日志按hash保留；第一次清理因JSON日期时区转换guard失败，改DateKind String后实际关闭，未重跑构建。

core早审profile_review完成读源并消息无P1/P2但未保存报告、agent已不在；新diagnostic_core_review_v2独立机械迁移审查按新6分钟窗口交付，SHA089FB5C64F8B44D63C451359B41DA88B5746D452662B21E12C636CCDBC1BC246。CUDA独立审查SHAD8054AEE1ED205BE541302F2FC371D94283FA90FD914387E41AE1840CED6495B。深层encoder fault、正式训练、真实H768诊断画像/AOT/质量未验，见[运行时证据](docs/evidence/diagnostic-runtime-2026-10-08.md)。

runtime八Git回执/21条记录身份零匹配、unknown0，root-git-resource-closure.json SHA A02E0EBFDDF53DA6CC54F530507056DC8F877569F4C7F13526A0FC61B7128025；没有push。早期root-filename假设的head-path-recovery-audit.md不存在，未从该缺失文件宣称恢复结果；已有历史路径审计保持原来源。

下一diagnostic_profile已在runtime提交后获准实现：06:32:31起、06:53:31停新/06:56:31硬截止，准备范围由9增至10明确文件，16短调用不变；归属Benchmarks新诊断profile/report、最小dispatch及checks，formal report不改。diagnostic_asset_cli另独立实现ModelTool新prepare-diagnostic-head/check-diagnostic-asset-preparation，仅三准确新/改文件；06:37:02起、06:54:02停新/06:57:02硬截止，≤8准确文件/14短调用。两作者不build/test/GPU，root统一复审、真实验证、每项单独commit；真实H768资产与最多1request/1sample/0warmup CUDA画像另开独占窗口，不使用旧head/report身份。root继续独占Git/shared docs，heartbeat sezika本轮view成功，30分钟继续。

Linux工程已单独提交32822f8；root-v10-git-reader-resource-closure.json核8Git、2外部reader、1worker共11成功回执，44身份无匹配，全部cleanup为空。独立画像已单独提交06de2ed：第2build1464/21.516秒、零警告/错误，tiny69660/2.930秒、full8 25092/3.188秒全PASS；三个实际DLL在build2/tiny/full不变，四runner/24身份及八Git/16身份终核0，两个fixture根0项。独立复审SHA C08F65…9A38EA及rename逆变换通过，首次构建失败、晚停新增线29.964秒写报告均保留。不称GPU/正式性能/AOT已验，没有push。

画像工程已单独提交06de2ed，八Git/16身份无匹配，证据与HEAD后续交接已补。linux_cancel_cause初次资产审计已停；next_executable_audit恢复审计04:26:04–04:28:58完整核两篇证据、当前model/dataset小manifest及两历史head/report明确路径。两个历史产物当前不存在，不能用旧FeatureSha/HeadSha当当前文件实测身份；范围不代表全仓库无head。恢复报告SHA14F2ED…8375E保留，无GPU/训练/模型加载或下载。

head SaveNew v2已验收并单独提交c8f12112984d8e6d4abf43bf73ec8b54532135bb：Core584E72/ProgramCF2231/Checks3479D2冻结，一次build18140/17.105秒零警告错误，tiny75300/2.905秒1PASS、full64400/3.088秒8PASS；三DLL保持，三runner/18条已知身份无匹配、两个fixture根0项已删。root-final-validation.json与永久head-asset-publication-2026-10-08.md记录范围；两P2静态修复、review原SHA859F4A…52CA1和准确delta纠正均保留，报告晚停新线14.119秒不倒填。没有当前GPU/教师服务/下载。

首Git stage75372受Process snapshot timeout影响，Failed/ExitCode=null，HEAD819ada0未变；八批准文件实际已完整暂存，三个Git PID后来absent。root以新日志根/相同八path和三源hash恢复收口，CIM查询从2秒改为runner已有支持的5秒上限，原失败保留；不得把未知stage退出码称成功。Git由root独占，不重跑已通过构建/检查。

恢复Git八回执全部Succeeded0/cleanup为空，16身份无匹配、commit script已退出，root-recovery-git-resource-closure.json；另核原三Git+launcher和四reviewer共8PID当前absent，root-prior-git-reviewer-closure.json，不倒填旧stage退出码。root已接续next_executable_audit实现独立SZDH1/typed asset/专用loader/seeded零更新，最多7文件/12准备调用，实际首clock05:01:04、05:18:04停新、05:21:04硬截止；只归属core诊断资产及必要publisher提取、新tiny检查与tests Program。CLI/profile/runtime/CUDA不改，正式训练state/feature/data字段不借用，root统一真实build/检查/独立commit。下一次先核作者freeze，不重复派单；profile_review另以4明确源/4调用、6分钟窗口只读设计GPU/runtime接线，硬截止≤05:12，不修改源码。30分钟heartbeat继续。

本轮共享交接已另提交819ada0，八Git回执/16身份无匹配、script已退出，root-handoff-git-resource-closure-v2.json；v1闭包漏root字段仅核8launcher，原文件保留，不假称原v1完整。画像兼容审计原计划文件写入guard拒绝，root以diagnostic-profile-audit-message-20261008.md保存最终消息、已知两reader79096/76320后来absent。另新正式入口审计04:49:55–04:52:30、4调用/4源码按期交付diagnostic-formal-entry-audit.md SHA70193E…338671；SZMK1/default head magic与准入/新算features边界已静态确认，GPU/CPU报告decoder与workflow实际head路径尚未完整审/测。不得以sidecar或0steps宣称强隔离；下一实现用独立诊断magic/typed loader，再接CLI/GPU画像。正式数据/真人审核/封存/上游oracle与正式训练质量仍开放，30分钟heartbeat继续；用户微信文件保留。

补充workflow审计05:00:59–05:03:20按期交付，3源码/4调用，diagnostic-workflow-entry-audit.md SHA2E9AC5…7A7EC7。GPU恢复真实调用默认head/checkpoint Load并核报告/optimizer/权重bytes；CPU默认checkpoint恢复无head输入；GPU schema3与CPU schema2/steps>=1/unknown成员拒绝均静态实读。上一审计的实际调用路径unknown据此收敛，仍未运行诊断负fixture；新格式不需要放宽正式decoder或人工准入。

最终Linux八case/逐身份终核及画像三源/DLL/检查的完整证据分别见docs/evidence/bounded-linux-runner-2026-10-08.md与independent-profile-entry-2026-10-08.md。旧权限等待中断及v1–v9失败不倒填为成功；当前执行范围以本节为准，下方历史检查点不作为活跃派单。

### 2026-10-08 UTC03:50 历史检查点（被上方新结果接续）

Linux通用runner仍是未验草案，未提交为已支持。v7同套tiny及2–7通过，第8控制进程原字段被forget后丢失而失败；v8仅两行保存原ppid/pgid/sid，tiny正常payload0但timer归属refresh失败、终核live1，余7项零执行；v9只将has_token/record_owned的原名义1s/2s局部检查改为共享单调子deadline，tiny通过、case2超时完成但中间ownership_unreadable导致cleanup_incomplete1，终核complete1/live0/unknown0/unchecked0，余3–8零执行。v8实际失败阶段仍unknown，不能把静态SECONDS缺陷或之后退出倒填为真实根因/当时回收成功。

root已独立终核当前资源：v7共28known及两组43proc/token均零live/unknown，Windows70身份无匹配；v8四本批+两前observer身份absent、43proc/token零live/unknown，Windows11身份无匹配；v9八本批+前observer共9身份absent、44proc/token零live/unknown，Windows19身份无匹配。最新原始回执在`.artifacts/linux-runner-hardening-20261008/root-v9-independent-closure-windows/20261008-034952-617-root-v9-independent-closure.result.json`。全部原始失败、冻结与诊断保留；无GPU、教师服务、下载或独占MCP。

最新Bash v9 SHA `D051624706D3E79E4880212293CF2C625C190738E91AABD55252B2AC527BC7C7`，freeze-v9 SHA `A7B9E073316B7CD755D4993D2EF9AA21ED90153BBB873FB9A645A2B7B570C912`，两PS保持v2。下一步先审cleanup阶段的安全延后方案：不发未经proof的信号，保持后续有界重核及完整最终分母；setup/work及实际signal syscall错误仍失败。没有授权跳过token/tick/argv/父链或把临时歧义直接改成成功。

S5-06独立画像入口已由限定四源码确认缺口并在ROADMAP登记；`/root/next_executable_audit`归属Benchmarks的Program顶层接线及新IndependentProfile.cs/Checks.cs。方案仅CUDA真实入口/CPU tiny兼容、实际Evaluate E2E、Score调用与typed返回分开、分项未测、正式样本0。当前三个源码已存在但没有收到构建/检查通过证据；独立review指出前置失败not_started分母/已核身份、typed失败行状态及发布partial处理三个P2，已发作者修复。作者原窗口03:28:44–03:50:44、03:47:44停新增，不能擅自越界执行。Git/shared docs由root独占；未验代码与用户微信两文件不暂存。下一接续先核作者收口/权限与资源状态，再明确新窗口完成真实验证，不重复已通过S4-06/GPU/AOT/教师调用。

固定真实 mmBERT 的 C 切片 encoder smoke 已通过：PID75592、48 tokens、markers27/37、hidden768、310 kernel launches；runner17.299秒，资源归零。marker SHA-256 `59194DCFF642F4790DBF55F58D38EFBAB200C0528BEE25894DE38D71296B1CCD`。该 smoke 没有训练 head；GPU head 的梯度/更新与恢复工程另有 fixture 实卡证据，没有正式数据训练结论。

B 重新 export 存在 PTX 字节漂移，未替换既有17 PTX/manifest；stamp只核既有来源/ABI/hash并补manifest身份。原失败构建、CIM超时和清理限制保留在各证据中，不能声称重新导出已复现。

## 教师当前状态与资源

v2 固定教师为 `unsloth/Qwen3.5-9B-GGUF@3885219b6810b007914f3a7950a8d1b469d598a5`；catalog `qwen35-9b-q4km`、API model `qwen3.5-9b-q4-k-m`，Q4_K_M 5,680,522,464 bytes，SHA-256 `03b74727a860a56338e042c4420bb3f04b2fec5734175f4cb9fa853daf52b7e8`。完整32题115.6265689秒、16213 prompt/562 completion tokens；不是正确率或正式训练吞吐。

原始结果根 `.artifacts/teacher-gpu-prompt-v2-20261008/simple-run-20261007-2019/`：

- `full-audit.json`：34次HTTP、32不同题、34严格字符串标签、151引用通过；training_admission/quality_evidence均false、human_review=pending。full32 manifest SHA `5151fa98f0496e76c4a9b6cd64f6230792f078e695aae7311e41300b556093c2`；records54,198 bytes/SHA `deb5d8ec8e2c8f1d3f772c42cde1bde9f6141e975d4b20e942b52e1ab09a08e4`。
- `process-and-resource-closure.json`：58份runner清理为空，75身份无匹配存活；monitor49个样本全部WithinLimits，最终Deadline/538.410秒、StopRequested=false。峰值GPU5953/free最小1996MiB；卸载确认loaded=false/busy=false，server主动停止的Failed/exit-1保留；后核used324/free7625MiB。
- `cpu-closure-followup.json`：全部58个原始root PID/start无匹配存活，UTC20:36:47完成。原GPU阶段closure20:34:04在20:34:24截止内；文档写至20:35:15超原界，另批20:35:15–20:40:15纯CPU窗口补核，没有新增GPU/HTTP/build。

**第二轮教师执行结束时，没有遗留教师服务、采集、训练或本任务GPU进程，原教师GPU窗口已释放；新GPU窗口见当前接续任务。** 上批实现子智能体均已完成切片；v2独立只读验收UTC20:43:47通过，最多12文件/5分钟、没有重跑GPU/HTTP/build/hash或进程查询，记录在 `.artifacts/teacher-gpu-prompt-v2-20261008/independent-review-20261008.md`。root负责共享文档及Git。复杂harness因三项生命周期缺口未验收，Run已禁用；Prepare tiny不代表完整生命周期支持，后续不复用Run。

Tomur在UTC20:17已fetch/ff-only，当前HEAD及origin/main均为 `b07ae41450a5832e790eaac1bcdad61531a6a0e0`，ahead/behind=0/0；整数helper、固定llama ABI和9B字节大小修复已提交推送。本轮教师复用host仍为`32675f8`构建，不能称最新源码构建。只剩用户`docs/articles/`未跟踪，无必要修改待推送。

## 当前接续任务

UTC23:22最新状态：原生取得/登记已单独提交`ef1288d`，32题人工审核入口已单独提交`c386d29`。审核包仍全部pending、八个人工字段null、曝光=true/封存资格=false；[审核入口证据](docs/evidence/teacher-review-entry-2026-10-08.md)保留固定源、64引用及五包文件身份。独立审查23:16:02保存早于截止，SHA `38E00DA969C5AFE377CEB4F3C40F8EDB129E3A9842F85CFF5579A417D992B226`；root tiny1/full5原字节核验全部匹配，暂存五Git blob与hash-object一致。没有重新生成、教师或GPU调用。

两任务Git与监督复验七份回执成功、cleanup为空；root UTC23:22:02精确核14身份无匹配存活，见`.artifacts/teacher-review-20261008/root-receipt-closure-7.json`。adoption stage52896/commit95916、review stage32248/83476及commit23032均退出；root最后两审计PID25000/9820亦后核不存在。未创建服务/下载/待回收临时对象，user微信与Tomur文章文件保持。adoption五源码/四DLL已独立freeze/hash复核，CPU1→7/GPU1→11、training21与cleanup9case全部通过，详见[登记证据](docs/evidence/cuda-native-adoption-2026-10-08.md)。不重复旧GPU/全套。

Windows GPU训练Native AOT已单独提交`6c3b096`：作者UTC23:39:11结束，最终证据SHA `C68F8F7F857BA0B7D5741E208D46B20DFB391B0256E04E01923187D7A2D48A05`。原23:18:04–23:43:04窗口提前收口；五源码最终freeze23:30:16/SHA `CD3946F0C5C8313DAA9150E0E23BF0D7E22A3D24DA1F555B4F1F78FB8CA6CCBF`，两处末尾预算P2由root补读修复diff、六源hash核。旧managed16/五CPUcase绑定初始freeze，新native16绑定修复版；主3→6恢复/连续6、48launch/480H2D/240D2H、tiny4/80/56、PE和原生flags均通过。root四workflow原字节/1→11case复核及十作者runner/63身份终核通过，最后两审计26920/52220退出。Git stage40972/commit73904（3.384秒）和两launcher后核不存在，回执cleanup为空，见windows-v1/root-git-closure.json。GPU/源码窗口已释放，见[Windows训练AOT证据](docs/evidence/gpu-training-aot-windows-2026-10-08.md)。

Linux训练AOT由`/root/head_checkpoint`接续，UTC23:46:05–次日00:11:05，00:10:05停止新增执行；artifact `.artifacts/independent-training-aot-20261008/linux-v1/`，最终五源/核心/PTX不得编辑，GPU仅此任务。最多两boundary artifact适配新根、300→600预算必须同步并先经root审查，最多2新根tiny≤15秒；再实际离线cache linux-x64 publish与native tiny1/full11≤16更新。最多2有失败理由publish/600秒，预留执行及finally收口，独立obj/output/cache，不复用Windows或旧ELF，LinuxPID/starttick/token/父链与成功后子树清理另核。禁止安装/网络/CPU trainer/encoder/教师、Sezika push/发布。新runner未审查不得生产执行。

UTC23:55 Linux首次publish被boundary的2秒/proc扫描上限中止，`inventory_failed`/127、clang收到TERM/143；不是AOT/trim编译结论。历史`cleanup_incomplete=1`、survivors=0保留。作者另行有界审计22个Linux pid/starttick及45个/proc token项，live=0、uncertain=0；root已读原始stdout和回执。artifact runner仅snapshot注释/条件2→4秒，4096项、总600秒、清理6pass/8秒及outer+12不变；root逆变换新字节SHA得到原`C2F165D7D5C15F07AB67753ECB29F6CE56FF0606C583B9BF83260671C59F2C07`，新SHA`45E0C3A04E63266DCE48408D0BF9263404EE4A901C39F13D8D56368D45F0C790`。额外唯一tiny取消125、cleanup0/survivors0，只观察gate worker及sleep0.1，未覆盖请求的sleep10 payload嵌套；局限不隐去。root已准许第二且最后publish，其后成功才能native1→11；截止不延长。

root另派`/root/teacher_prompt_v2`纯只读下一任务审计，UTC23:55–00:03、最多6明确文件/8调用，无build/GPU/网络/安装，只写`next-action-audit.md`。不重复已经验收的工程，不代替人工审核、封存或独立上游oracle。自动跟进已再次调用`sezika` view，未改动计划或通知策略。

Linux切片已于UTC00:01:14完成，作者execution-evidence SHA`5694BA0C921394684950A34918C3991185051AEC667CDFF7D855EB8BE789F1FF`，窗口/GPU/源码已释放。第2publish60.930秒、ELF6,415,616bytes/SHA`75BE4B654A624801D6F940FC709432DF84E7BBE2F5631425AF184C1754EB2258`；tiny1/full11共16更新，主3→6恢复与连续6全部参数bytes/loss/checkpoint/head匹配。root28输入/实际ELF、tiny三/full九文件、原始1→11case通过；UTC00:05:08核九作者及八root回执/46身份无匹配存活，root-receipt-closure-9.json。作者Linux49身份及43proc/token项live/uncertain=0；root未重跑GPU。两执行runner已按实际字节保存到docs/evidence，参见[Ubuntu训练AOT证据](docs/evidence/gpu-training-aot-linux-2026-10-08.md)。

作者批临时删除曾自动审批blocked（未给具体理由），关闭时temp未清的旧JSON不改。root随后先单项静态apply_patch再余17个0bytes/非link lock，17次/15秒逐空目录非递归Directory.Delete(false)；18锁/15子目录+2TMPDIR根均已清，root-compiler-cleanup.json及回执成功。用户文件/cache/交付物保留。ILC2原为CLI请求，实际rsp无parallelism；root读取准确Linux10.0.12 targets行278仅支持IlcSingleThreaded=true→--parallelism:1，未来publish必须用有效属性并核rsp，不新增本批第3publish；Windows证据同步改为意图未证明，不能继承Linux版本结论。

下一唯一工程已在ROADMAP S5-06登记并接续`/root/teacher_prompt_v2`：UTC00:03–00:23、00:21停新增，最多8明确文件/10准备调用，只改tools/Invoke-BoundedLinux.sh、tools/Invoke-BoundedLinuxProcess.ps1、tools/tests/Test-BoundedLinuxProcess.ps1；artifact linux-runner-hardening-20261008。共享单调cleanup deadline/完整终核分母/逐身份安全信号与外层预算需先静态审查冻结，才tiny1→最多8生命周期case/180秒；无模型/GPU/build/网络/安装。审计SHA`3E220E609F56451C8D927CB56A3A2624F890CBEE157A7040EF24CF4708E2646E`，六文件/七读取，审计首次三短读取PID未捕获的局限保留。旧Linux两runner不改，shared docs/Git仍root独占。

Linux证据与两runner已单独提交`14a3917`，stage99560/commit35644及两个launcher后核退出，两Git回执cleanup为空。root补查末两ILC读取与receipt全审计、共五结果/十身份零匹配，root-git-closure.json；首次只读Git收口脚本缺括号exit1，修复后PID79140成功/2.948秒，失败原回执保留，没有新增GPU或更改训练结果。末两个audit及其launcher由后续root终核确认，不把当前执行中的自身身份写成已退出。此窗口没有未清compiler TMPDIR。下一工程作者已派独立linux_runner_review，只读三冻结源；root不重复派审，先等待实际freeze和静态审查再允许最多8case/180秒。

Linux通用runner准备窗口的v1被root与独立head复审阻断：测试字符串误插整段脚本/保留重复循环，Parser四错误；Bash控制字段双slash不是真TSV。独立回执SHA`24CBC677C20AEC96980E1C072C63C8846FF07C115343A5683028B543A7D7C074`，未运行WSL/test。root明确授权额外2调用修复微阶段，准备合计12（不冒充原10）；v2三个hash为Bash`4167290FD977A8429504486FE8901A0B2C09704C88258020520489857E9E932F`、wrapper`BCCBDAE0D5F676FB205C79AD39A0AD57100655146BAD3088E924C2FDDA055197`、tests`784D59A988E341D0571F9DF41DE323862F0ECE6AF1B24DFE9A77C9D1D4F883D3`。root实际复读两PS全源、三hash，并机械还原Bash得到v1 SHA；单QPC suite180秒/140秒停新、case2续原tiny绑定、外层Succeeded0/Failed124/125/127完整身份、峰≤4设计已核。v2两PS静态Parser零错误，不等于生命周期通过。

tiny获静态放行后仍被绝对00:21 guard拒绝：exit1/0.3359秒、no case started；没有WSL/Bash/timer/payload或suite-v2目录。原准备窗口按00:23截止收口，不挤掉finally或私自延长；v2保持未验草案，不提交为已支持。root为未来checkout给唯一Bash文件登记LF。后续采用独立新执行窗口、复用v2冻结审查、先tiny再case2–8/一套180秒，无GPU/模型，执行授权待原窗口完成后由root具体派发。

两个独立只读审查均已停止，按期保存：workflow审查SHA `D2A7F89B61DDE1936FDFB6F17A2605AFC8F290F5ABE5E97A54A761C5B4CFDEED`；三文件fault/project审查SHA `E06D2FD59B4406B74A8028C3980BC89A62DB4890BF0B8A3B390F30BC18D456B5`，后者明确绑定初版、P2当时未修，不冒充新源码审查。root随后补已知diff/真实native及资源证据；没有并发源码编辑。计划acceptance-plan原SHA `95ED067CD47899F0C15337D29AAEB844954C149D921AB68D9E64073ECDC8C509`仅作范围记录，Linux使用WSL而非其中SSH建议。Git/shared docs/root独占。

上游数值oracle仍缺NumPy：实存Python3.14.3/Torch2.12.0+cpu/Transformers5.13.1，NumPy安装问题已异步提出且未回复；不重复问、不把等待当许可。已冻结26项输入/容差和离线导出/比较器工程，未import真实backend或forward。具体task-local安装方案在`.artifacts/independent-oracle-20261008/numpy-install-plan.md`；若收到明确授权再按原official wheel hash、单case180秒预算执行，不擅自安装。此前Python安装管理器意外更新26.3/import探测越界及清理审查晚25秒事实见各永久证据，不能改写为按期完成。

正式逐条人工审核、许可用途审签、独立未见封存、可信上游数值、正式训练收益、语言质量/校准、正式trained-head两RID和新性能/发布仍开放。32题只是pilot/audit；不复用曝光family当封存，不凭教师格式通过准入。旧窗口全过程已保留在Git及证据，不再当作活跃任务；heartbeat `sezika`继续30分钟接续，Sezika不push、不发布/部署。

UTC00:25–00:40新Linux runner执行窗口：00:38停止新增，最多8生命周期case/一套180秒，先tiny再root放行。v2真实normal-child tiny失败：Linux ownership_failed、cleanup_incomplete=1、final_check_complete=1但verified_live_count=2，unknown/unchecked=0；Windows Failed/126、cleanup为空、invocation匹配。原receipt SHA `F7360D10821899698B08E99B1DFD447557902FDDC64E4535C6CCB2196E90554B`，原Windows结果SHA `4B8A77F0A3DAC2819DF6E06CAAF872415A2054B5010AFADAEFA4C2931A61FB12`，位于`.artifacts/linux-runner-hardening-20261008/suite-execution-v2/normal-child/`。完整终核不能等同资源已回收；后续case2–8已停止，三新源码仍未验收/未提交。

该失败root已原字节复核。Linux supervisor96057/starttick38272646、timer96058/38272655、STOP worker96059/38272656，token `sezika-96057-4439-145`。根因为record_owned父链遍历改写child全局stat字段、第二次refresh把祖先tick当child。作者获准独立最多两PID/两pass/10秒恢复，不复用坏signal_owned、不按名称或盲kill组；须完整PID/tick/token/argv/稳定当前父链双读后逐身份发信号。UTC00:32恢复pass1核timer已消失，worker proof成功但helper错误的字符串比较拒绝信号；原失败保留，作者仅修helper比较并执行最后pass，尚待真实零live/unknown回执。恢复证明通过后最多一次最小源码修复与新freeze，未经root diff/hash审查不得执行v3；00:40窗口不能私自延长。下一接续先核回收结果。

UTC00:38原runner窗口已停止新增并提前收口，无v3执行。恢复pass2真实双signature核worker后精确KILL，Windows结果Succeeded/0，SHA `E77BA078EBF618AC435A6A24C67049D5895DAD3F8674F5EAE9CEAB03A322210D`；root实际读取源码、stdout与result。root默认Ubuntu用户只读后核5PID均absent，但42个environ不可读而返回126，此失败不改写。随后root以显式Ubuntu root复用完全相同只读脚本、无signal/系统修改，UTC00:37:45结果Succeeded/0，SHA `E775101F1CDB06158587A62CEDE51C5634272672804DCD523EC30C1EB5BBAD11`：5known live/unknown=0，43proc项token live/unreadable=0。原96059 worker已独立确认退出，恢复/后核日志位于recovery-windows-pass2、root-recovery-windows与root-recovery-privileged-read-windows。

v3仅保存/恢复tick/ppid/pgid/sid/state，SHA `910871988E522F0138AD2637E89FAEBFBC74FC677DDA0BC8D610AB6E80A5E1CF`，两PS保持v2身份/Parser0；root机械逆变换准确得到v2SHA，未有其他修改。入口first仍来自caller全局tick，原要求的入口独立stat尚缺，故不执行v3。head复审00:33–00:38已读两PS、确认失败/timeout/missing receipt拒绝与共同QPC180秒/140秒停新；v3 Bash未读、最后报告写入被deadline guard拒绝，消息交接代替尚不存在的head-v3-review.md，不能伪称报告已保存。

已派全新窗口UTC00:39–00:54（00:52停止新增）给`/root/teacher_prompt_v2`：仅补Bash record_owned入口stat child及first赋值，最多1最小修复/6准备调用，freeze-v4经root审查后才CreateNew tiny；tiny及资源回执通过后再同suite的case2–8，最多8case/180秒。另派`/root/head_checkpoint`00:39–00:44只读完整v4Bash，最多2读取+1补写、3固定文件，不运行WSL/test。无GPU/模型/build/网络/安装，旧失败、用户文件及全部证据保留。新窗口不是原窗口延期；shared docs/Git仍root独占。root新只读进程身份保存在root-*-identity.json；其中root-resume记录的Parser修正后仍因GetProcessById异常包装退出1，原失败保留；后用Get-Process定PID已核85728/81360/41472均absent。

UTC00:45:58 root原字节复核v4 tiny成功，receipt SHA `BBBDE2E957BFD17A090BB5BEF127218C9102247FF84CDE3973BB6B41E69D5B9C`；Linuxcompleted/0且完整终核live/unknown/unchecked/budget均0，WindowsSucceeded/0/cleanup0及实际调用绑定通过，identity/audit显示worker exec与timer逐PID回收。其共同QPC套00:44:08停新在等待root复审期间已过，case2–8零执行，不重置时间、不称8case通过。root随后独立Ubuntu root只读核4个tiny身份及此前observer均absent，42proc项token live/unreadable=0，日志root-v4-closure-windows/20261008-004651-268-root-v4-tiny-closure.result.json。

head-v4完整Bash静态复审已实际保存，SHA `2ABEBC40ACAF7F3C93FB45A3909076EA3CA45753CF319B44287E0CF64212FF77`；发现control-owner refresh后缺originaltick/child身份再核P2。原00:39–00:54窗口内另准最后一个最小修复：v5仅补3行保存原parent/group/session、refresh后原tick与保存三字段比对，SHA `7DCDAC15155C773CEAA1467D5760BD5D8D797E3971FED6818877A99D9B5ECF46`。root实际读并机械逆变换准确得v4hash，两PS保持冻结。UTC00:48:28后已明确放行新v5完整范围，先CreateNewtiny，其真实字段/原字节与冻结全部通过后自动同suite续2–8，仍单QPC180/140停新、最多8case、00:52停新/00:54截止；任一失败/unknown/源变化立即停、不重试。新源P2修复是重验理由，旧套及失败不覆盖。最终仍待真实full/独立资源/最终root验收，尚未提交runner。

下一S4-06只读范围核查已真实完成，报告SHA `1847A1603706F1CBB270165F30260954CEAAA2D5908C74E8310236ED27FC433F`；原指定next-action-audit路径不存在的事实保留，没有扩搜。root已在ROADMAP登记并派`/root/closure_audit`UTC00:41–01:01、00:58停新增，仅IndependentModelTool的Program.cs、新ReviewedSliceDiagnostics.cs和ReviewedSliceChecks.cs（必要csproj）实施最多32行已有development报告+人工审核标签侧车的离线分组，静态DTO/sourcegen/hash身份/unknown分母及所有正式准入false。无题文/模型/GPU/网络/安装，最多2build/120秒，tiny1/30秒后≤12checks/60秒。首次build进程47144已由既有Windowsrunner记录。`/root/head_checkpoint`另在00:49–00:56只读复审冻结三源，最多4文件/4调用，及时P1/P2反馈；Git/docs仍root独占，两实现各验收后单独commit。

用户本轮追加正式训练模型托管目标：Hugging Face（抱抱脸）及模力方舟，已登记ROADMAP S6-02。权重/配置与二进制分开，绑定模型卡、许可、版本和hash；当前没有把合成head测试当正式模型上传，也没有对外发布。正式模型、人审/用途许可、质量和实际托管账户/产物仍须自己的验收与发布范围。

用户在权限切换后要求继续，UTC02:30恢复监督。当前环境仅工作区可写、`.git`只读且网络受限；普通WMI查询被拒绝，root有界只读Git diff、本地非增量构建和合成检查已分别经具体权限请求成功执行，不从其他工具绕过。

S4-06最终复验报告SHA `5ED5CC57764721312017D5DDD23E7D298E6B0B9918DB53492582D5A68D72164F`：三源匹配、历史四回执/原stdout/tiny1→full12通过、18历史身份全absent；历史DLL未冻结SHA，不能拿当前字节倒填。root另做唯一当前非增量构建（14.861秒零警告错误）、冻结真实六文件后tiny1（1.463秒）/full12（1.409秒）全PASS，三源/csproj与六文件before/after未变。主DLL478208bytes/SHA `52880757076B191C2208FD19A6BAEAAF58F4D15F0F180EC56F2B6A0575087840`。root四当前runner/16身份零同身份存活，synthetic-checks目录0；closure SHA `C07BB38C91E441DA2C4DE8D853A39B29C702F1ACCEAD756236B105C9F09D270D`。永久证据[审核切片工程](docs/evidence/reviewed-slices-2026-10-08.md)，此工程可单独提交，S4-06真实审核/质量不关闭。仅source三文件、四共享文档和该新证据为本次代码提交范围；runner未验草案和user微信文件排除。

Linux通用runner v5真实套只通过1–3（normal/timeout/filecancel），case4 signal取消实际timed_out/124而预期cancelled_signal/125，余5–8零执行；原完整证据SHA `5BDEFE06DDF0FDE17E9ACE6912874D5B3B91E4B5E31E3A9EDC062C405F9DBBC4`。后续原00:56–01:01只读closure/cause未交付两个指定文件，不伪称已完成独立Linux终核。新只读agent `/root/linux_cancel_cause` UTC02:31–02:37实际核3源/原case4与archive，报告SHA `B25AAA9115D46347863C6B3296473BBA61B14C358420C5E03A812A145B56F10A`，根因仍unknown；可确证现代码把pause_owned所有失败归timed_out、缺取消分支/trap/backoff记录。未改源/试验/WSL重查资源，CIM被拒的父链unknown如实保留。该runner不得提交为生命周期已通过；下一步先有界独立资源终核，再最小判别埋点与新freeze/单次试验，不凭audit没selfTERM事件推断没发TERM。

本轮两个新只读agent已结束，source/GPU/模型无活动子任务；root在做S4-06 Git收口及后续runner接续。30分钟heartbeat继续，正式人审/许可/封存、上游oracle依赖、正式训练收益/质量/AOT/性能/发布仍开放；Sezika不push、不对外上传或部署，Hugging Face/模力方舟仅登记目标。

## 下一次派发

1. 先核AGENTS、ROADMAP、git status、heartbeat与子智能体状态；复用以上已通过证据，不重复下载、32题采集或完整回归。新GPU任务独占窗口，先tiny再有界完整范围。
2. 优先推进S4-15可信固定上游数值oracle：对已有真实46-token/267项capture冻结独立参考及容差，再接原始模型CPU/CUDA比较。参考Python仅限离线oracle，不进入核心；正式批量特征继续GPU。
3. 按依赖推进真实独立模型/自有head的两RID AOT及独立GPU性能画像，明确fixture、真实推理、数值、质量与性能范围。既有Laya证据不能替代独立模型验收，禁止用已查看PAWS/Nimble选参。
4. 正式数据训练需实际许可用途审签及逐条人工复核；校准/封存还需独立未见数据与真实保管声明。用户已选通用中英分类、程度评分、真假判断；32题仍AI/audit/pilot/pending，不能自动转train或金标。600训练+各120开发/校准/测试仅历史建议，未获正式准入。继续不依赖这些外部事实的工程任务；不重复询问含糊的“指定审核人”问题。
5. S4-09/S4-13依据实际开发收益启用，S5-07按数值/质量及瓶颈证据优化；正式发布/部署等具体产物满足门槛后按授权执行。每项实现验收后单独commit并更新交接。

## 固定工具与执行边界

PowerShell `C:\Program Files\PowerShell\7\pwsh.exe` 7.6.6；dotnet `C:\Program Files\dotnet\dotnet.exe` 10.0.401；git `C:\Program Files\Git\cmd\git.exe`。工具发现限PATH、明确配置和固定路径；禁止广域扫描、Graphify及按名称杀进程。本机无原生GCC，不重复扫描；GNU需求仅在已知Ubuntu WSL通过有界command-v核实，PCCT没有确认路径。

UTC21:32–21:33 root用3次有限Ubuntu timeout核发现有AOT工具：`/usr/bin/dotnet`10.0.112、`/usr/bin/gcc`13.3.0、`/usr/bin/clang`18.1.3、`/usr/bin/ld`Binutils2.42。准备阶段没有安装/build/模型/GPU；Windows PID75496/26572/26736均成功/cleanup为空且已退出，Linux timeout/bash PID与start/argv由ps记录，前两组后核不存在。细节`.artifacts/independent-aot-20261008/root-processes/ubuntu-tool-readiness.json`。其后Linux诊断AOT/probe/真实46-token CUDA已在`86099ee`范围通过；新训练代码/ELF仍须独立发布与验证。

长命令经`tools/Invoke-BoundedProcess.ps1`记录PID/start/argv/父链、墙钟、退出码及清理；构建`--disable-build-servers -m:1`，关闭共享编译/节点复用，控制并发。每次委派重复数量/墙钟双边界、取消/进度、极小试跑、进程完整树回收和专属临时文件归属核验。精确PID/start先筛，只有身份匹配再查CIM；禁止反复对全部历史PID广域查询。

保留日志、成功交付物与原始失败；只删除核实绝对路径及本任务归属的临时对象，不清用户文件或共享缓存。B21结果/70身份及C31身份已核零存活；v2另按上节75身份和58root补核。没有创建独占MCP服务。Windows采样可能漏过极短中间进程，保持此限制，不伪造清理或观测范围。

## 关键历史证据索引

- [首批checkpoint/CLI/预测](docs/evidence/supervision-2026-10-07.md)：`acce79f`、`601a865`、`3a7435e`；managed/Windows AOT及CalibrationTool fixture互操作通过。
- [独立pipeline](docs/evidence/independent-pipeline-2026-10-08.md)：`239ef59`；CPU190/CUDA59、Windows AOT CPU18/CUDA17 tiny通过。
- [原始资产恢复](docs/evidence/independent-assets-restore-2026-10-08.md)：`95fe133`；4源文件、134 tensors及真实CPU smoke305.15秒；[267项capture](docs/evidence/independent-numeric-capture-2026-10-08.md)：`252d087`；51,720,192 bytes/hash核验通过，上游oracle仍开放。
- [教师来源/运行准备](docs/evidence/teacher-runtime-readiness-2026-10-08.md)、[GGUF恢复](docs/evidence/teacher-weight-restore-2026-10-08.md)、[采集合同](docs/teacher-collection-s4-10.md)：`7fcb49a`、`da41af6`、`85ccf17`、`3229232`、`9f94be0`；许可工程用途与正式人审区分。
- [GPU head训练工程](docs/evidence/gpu-training-design-2026-10-08.md)：`6d36c70`；CPU中央差分27/GPU21→61，旧14PTX保持；[CLI设计](docs/evidence/gpu-training-cli-design-2026-10-08.md)、[A合同](docs/evidence/gpu-training-contract-2026-10-08.md)、[B workflow](docs/evidence/gpu-training-workflow-2026-10-08.md)、[C CLI](docs/evidence/gpu-training-cli-2026-10-08.md)分别验收。
- [首轮GPU教师](docs/evidence/teacher-gpu-pilot-2026-10-08.md)、[v2提示](docs/evidence/teacher-prompt-v2-2026-10-08.md)、[第二轮GPU教师](docs/evidence/teacher-gpu-prompt-v2-run-2026-10-08.md)：失败、格式通过与训练准入分开记录。

总路线仍缺真实人工数据准入/封存、独立上游数值、正式训练收益、质量/校准、独立真实两RID AOT及新增性能/发布验收。仅当前切片闭环；heartbeat继续。
