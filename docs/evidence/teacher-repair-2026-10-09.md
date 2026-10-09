# 歧义题修订、删除与盲审证据

2026-10-09，Asia/Shanghai。用户明确要求使用sonnet_vip模型修复歧义题，修不好则删掉，再新建会话开始下一步。本轮已经完成数据修订和教师盲审，GPU训练尚未启动。

## 实际结果

| 分组 | 原题复用 | 修订且盲审通过 | 新版本排除 | 新版本保留 |
|---|---:|---:|---:|---:|
| train | 137 | 25 | 30 | 162 |
| development | 41 | 12 | 11 | 53 |
| 合计 | 178 | 37 | 41 | 215 |

新版本含140道Choice、75道Boolean；Score没有新增监督。原32题继续单独作为已曝光回归，不混入215题。沿原family/split保留，无重分组和跨组family泄漏；不硬凑原256/192/64。

同一 `sonnet.vip / gpt-6.1-sol / max` 对78问题题提出39修复、39删除。root对修复提案做语义核对，另排除 `boolq-train-row-094`（把是否有姓名换成文本是否记载姓名）及 `boolq-validation-row-058`（把是否存在组合换成电影中存在虚构组合）；这是AI语义检查，不是正式人审。其余37题在不传建议答案/修题理由的独立请求中全部获得clear/determinate、无需再改题，且答案与修订提案一致。Choice复审前按 `SHA256(repair-order-seed17|record_id|candidate_id)` 的固定、标签无关顺序重排，重映射答案ID/index。

修订只改question字段，原state.text、语言、family、split均保持；原来源、旧标签和256题全分母保存在账本中。删除只表示从新训练版本排除，不删除原始证据。修订/删除逐题原因见永久目录中的 `repair-deletion-report.md` 和 `data-v3/derivation-ledger.json`。

## API与身份

冻结plan从UTC `2026-10-09T05:04:25.2050566Z`起5400秒，最多28逻辑批/56HTTP尝试，每批≤6（修题首批2）、并发2、每逻辑批传输≤240秒。真实完成14修题批、7盲审批，共21次HTTP请求、21个HTTP200、21批严格验证成功，无网络重试；修题及复审队列于UTC05:26:31.948全部收口，在原窗口内完成。原33批成功审核只重解析本地字节，未重发。

请求/响应原字节、hash、逐批result、Python传输身份和PS7 guardian回执全部归档。密钥仅在内存中从当前CC Switch选中sonnet_vip读取，不修改Codex全局配置；上游模型身份仅是网关响应声明。没有将任务数据发送至其他提供商。

## 数据包与验收边界

永久证据 `docs/evidence/teacher-repair-20261009-final`：332个原字节文件，合计8,281,934 bytes，另有inventory；绑定前轮complete/inventory。工作包 `.artifacts/teacher-repair-20261009/data-v3`，永久镜像在证据目录的 `data-v3`。

- manifest SHA256：`E8E74CE0BBF1A72DA5FCDF4DCAD9D302513DC5DC42CB548BFD21D2569621AEB6`
- derivation-ledger SHA256：`55F3226946831C4B9CBD7F6EE10D1194764860C8343A9386CAA40C678CE24EC0`
- 合同：`sezika/teacher-labelled-experiment-data/v3`，schema_version3；实际分母215/162/53。

打包重新解析前轮33份原响应、本轮14修题/7复审原响应，并验证原请求hash、完整分母、答案映射、来源原文及分组不变。另一个静态核验脚本检查文件bytes/hash、215条catalog/provenance、40组审核request/response原字节hash、唯一ID及256题派生账本，通过。该包是新合同候选包，现有原生CLI尚未支持v3，不能冒充v2直接训练；新renderer/tokenizer strict1024长度还未测，旧最长914不适用于新题版本。

正式人审、教师输出许可发布、校准、封存、模型质量、新head AOT/性能保持pending/false。这里的AI审题条件通过不等于真实正确率或训练质量通过。数据源许可保持原MASSIVE CC BY4.0/BoolQ CC BY-SA3.0及原快照身份局限，不增加发布授权。

## 资源收口与下一步

真实进程终核22回执、112身份记录、93唯一PID，matching_live0/unknown0；打包/核验/closure共用进程随后另核absent。18条历史记录缺少观测argv，保留局限，不能用当前退出补造历史完整性。所有API guardian真实Succeeded/exit0；修题00前的无HTTP探针也保留。未创建训练GPU进程、未停已有服务；任务日志作为证据保留，未清空全局TEMP。

用户要求的新会话将接管必要C# v3合同、实际分母报告、新长度检查与固定一次24576updates训练，再独立新进程加载验证。具体工程、资源、回归、禁止重跑与所有预算见 `docs/training-next-handoff-2026-10-09.md`。原训练/教师任务不再新增请求，旧会话在新会话创建后交出Git/API/GPU独占权。
