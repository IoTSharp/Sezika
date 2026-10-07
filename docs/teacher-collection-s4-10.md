# S4-10 通用文本离线教师采集合同

本轮增加独立 C# `TeacherCollection` 模块、静态 JSON context 和有界合成合同检查。产品入口由监督者在 `Sezika.TeacherTool` 接线。它在 Sezika 核心以外调用本地 Tomur，只产生待人工复核的硬标签文件；不运行、训练或替代学生推理，不提供工具执行授权。本文件描述代码合同，实际构建、合成检查、教师身份准入与真实调用须分别记录证据。

## 已核实的 Tomur 源码与本地观察

只读源码为 `D:\source\Tomur`，HEAD `5cd90dd7565a4668016217a56cd2876383869560`，由监督者核对。相关文件：

- `app/Config/Defaults.cs`、`DataPaths.cs`、`app/Properties/launchSettings.json`：默认服务 `http://127.0.0.1:5137`，Windows 默认数据目录 `%LOCALAPPDATA%\Tomur`，支持 `TOMUR_DATA_DIR` 与显式 data-dir/path 配置。
- `app/Api/ApiRouteExtensions.cs:344,1261`、`app/Api/OpenAI/OpenAiChatCompletionRequest.cs`：OpenAI 文本请求支持 model、messages、stream、temperature、top_p、max_tokens、max_completion_tokens、context_size。DTO 没有 response_format、seed、logprobs；`app/Serialization/AppJsonSerializerContext.cs:249` 未启用未知字段拒绝，不能把额外字段当作已支持功能。
- `app/Api/ApiRouteExtensions.cs:3089`：非流式文本响应含 `choices[0].message.content`、model、object、usage；writer 固定 `finish_reason:"stop"`。该字段无法证明教师实际未因长度截断。
- `app/Api/ApiRouteExtensions.cs:2176,2251,3972,4011,4314` 与 Ollama DTO：chat/generate 的 format 字段未用于这些文本 handler，generate 的 template 也未使用。采集不使用这些未经实现的能力。
- `app/Models/ModelCatalog.cs:72`：`qwen35-9b-q4km`，9B、Q4_K_M、llama.cpp、GGUF，`unsloth/Qwen3.5-9B-GGUF/Qwen3.5-9B-Q4_K_M.gguf`，声明 8,953,803,264 bytes、Apache-2.0、期望 SHA-256 `03b74727a860a56338e042c4420bb3f04b2fec5734175f4cb9fa853daf52b7e8`。`ModelInstallService.cs:254` 为该相对 asset 构造 `resolve/main` URL，没有固定上游 revision。

后续来源核查由监督者协调：Hugging Face API 的15,916-byte响应将 `unsloth/Qwen3.5-9B-GGUF` 固定到 revision `3885219b6810b007914f3a7950a8d1b469d598a5`，其 `Qwen3.5-9B-Q4_K_M.gguf` LFS SHA-256 与上述期望hash一致，LFS size实际为 **5,680,522,464 bytes**。Tomur catalog 的8,953,803,264 bytes与该来源事实不符；生产前置核验改用5,680,522,464。此处记录上游LFS身份，不声称已经下载或本机重新hash权重，不修改Tomur源；source审查不构成人工数据批准、教师输出训练准入或正式许可发布。

本轮仅观察默认固定路径：`C:\Users\mysti\AppData\Local\Tomur\config\tomur.json`、`models\models.manifest.json`、`models\text\qwen3.5-9b-q4-k-m\Qwen3.5-9B-Q4_K_M.gguf` 不存在，当前 `TOMUR_DATA_DIR` 未设置。这不代表其他显式路径不存在模型，也不代表已探测本机所有教师服务。没有启动 Tomur、下载/哈希大权重、实际 HTTP 或教师输出。Tomur 仓库 Apache-2.0 代码许可证不替代模型和教师输出用途审查。

## 前置文件

采集要求 `--catalog` 指向一个 JSON，包含 candidate 对象。与旧 `validate-catalog` 字段兼容，但真实采集额外要求 hash-bound 小型证据文件：

```json
{
  "schema_version": 1,
  "candidate": {
    "id": "qwen35-9b-q4km", "service": "IoTSharp/Tomur", "model": "qwen3.5-9b-q4-k-m",
    "revision": "具体已核实的上游 revision",
    "model_sha256": "03b74727a860a56338e042c4420bb3f04b2fec5734175f4cb9fa853daf52b7e8",
    "source_url": "https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/具体已核实的上游%20revision/Qwen3.5-9B-Q4_K_M.gguf",
    "license": "Apache-2.0", "status": "verified",
    "endpoint": "http://127.0.0.1:5137/v1/chat/completions",
    "terms_evidence": "terms.json", "terms_evidence_sha256": "证据文件64位SHA256",
    "model_evidence": "model.json", "model_evidence_sha256": "证据文件64位SHA256"
  }
}
```

示例中的占位值不是可执行准入记录。revision 必须是40位 ASCII hex 的 HuggingFace commit；source_url 必须逐字绑定 `Uri.EscapeDataString(revision)` 后的指定 9B 文件路径，不接受可移动 branch/ref。真实入口核指定模型hash、Apache-2.0声明以及上游LFS的5,680,522,464 bytes；新权重版本需要另行审查合同后才能更改。内部synthetic检查继续使用1-byte虚构模型证据，该值仅在synthetic transport分支接受，不能作为真实资产证据。

`candidate.id` 固定为合同编号 `qwen35-9b-q4km`；`candidate.model` 固定为 Tomur `/v1/models` 返回的 API 模型键 `qwen3.5-9b-q4-k-m`。catalog 校验与采集前置校验分别核这两个字段，HTTP 请求、响应 model、terms/model evidence 和归档 teacher.model 均使用 API 模型键。不得把 candidate.id 当作可调用模型名；TeacherIdentity 原有 Id/Model 分别保留两种身份。

terms.json 与 model.json 都必须包含 schema_version=1、model、revision、model_sha256、source_url、license，与 catalog 相符。terms.json 还要求：

- `collection_permitted:true`、`use_scope:"offline_teacher_collection"`。
- `output_training_permitted` 与 `output_redistribution_permitted` 的显式布尔声明。
- `evidence_text`（上游条款及用途依据）、`reviewed_by`、有效 `reviewed_at_utc`。

model.json 还要求 `validation_status:"hash_verified"`、`model_bytes`、`verification_method` 与有效 `verified_at_utc`。这两项是外部提供的验证/审查声明，不能由工具自行填成人工认可。工具核验身份、证据 bytes/hash 和同一快照，不独立加载或重新哈希多 GB 权重，因此输出 `teacher.independently_verified=false`。即使条款声明允许训练，收集输出仍为 `training_admission=false`。

证据 paths 相对 catalog 所在目录；数据 assets 相对 manifest 所在目录。拒绝路径逃逸、绝对路径、驱动器/ADS、资产路径中的 reparse point。catalog/manifest/candidates 单文件最大 1MiB；每个证据、request、prompt 最大 64KiB。严格 UTF-8、JSON 深度 32、重复属性拒绝，哈希均从实际读取快照计算。

## 输入候选与硬标签

`--manifest` 使用 `datasets/teacher-general-v1/manifest.json` 合同：schema_version=1、protocol=decision-v1、1–32 条 audit pilot、AI drafted、人工审查与训练准入 pending、未调用教师、没有质量证据；candidates.jsonl 与每条独立 request/prompt 均通过 manifest 的 path/hash/bytes 绑定。候选须有独立 record/family ID、中英文 language、三种题型之一及 pending/not_called 元数据。

每条 request 使用现有 `IndependentDecisionV1Parser`，必须只含一个题、有效学生模型身份、合法 state。按题型拒绝多余字段，prompt 必须包含确切 request JSON 快照。候选映射：Choice 用原候选 ID；Score 用 levels 零基索引字符串；Boolean 按 false、true 顺序。

只发送实际 Tomur 支持的 `model/messages/stream:false/temperature:0/max_tokens/context_size:2048`，固定上下文预算，不采用服务默认的大上下文。temperature=0 不证明确定性、正确率或语言质量；prompt 的 JSON 指令不表示支持约束解码。响应 content 必须是一个完整 JSON 对象，字段严格为：

```json
{"question_id":"原题ID","status":"answered","selected_candidate_id":"原候选ID"}
```

拒答为 `status:"abstained"` 且 selected_candidate_id=null。不得包含概率、置信度、解释、未知或重复字段；拒绝代码围栏、错误题 ID、候选越界、多 completion、tool calls、无效 usage、非 stop finish reason。原始响应与有效可观察 usage/finish reason 保留；Tomur 固定 stop 的限制仍存在，解析完整标签不能证明教师质量。

## 有界执行与输出

```text
collect --catalog <catalog.json> --manifest <manifest.json> --output <新目录>
  [--max-records 1..32]
  [--request-timeout-seconds 1..120]
  [--total-timeout-seconds 1..1200]
  [--max-output-tokens 1..256]
```

只允许字面 IPv4/IPv6 loopback 且 path 为 `/v1/chat/completions` 的 HTTP(S) URI，无 credentials/query/fragment；不使用系统代理、cookie、认证或重定向。串行最多 32 次应用层 HTTP 发送，无自动 retry。每条进度记录 ordinal、状态、attempts、elapsed；支持 Ctrl+C 与外部 CancellationToken。响应最多读取 128KiB，溢出只保存受限原始前缀并记 failed，不能产生标签。

每条 records.jsonl 保留原 decision JSON/prompt、完整 HTTP 请求以及可取得的原响应文件及 SHA-256；记录模型与来源身份、题目/候选顺序、零基索引、usage、HTTP 状态、http_attempted、elapsed、失败代码。成功可解析标签为 review_status=pending，拒答为 abstained，其余为 failed；三类之和等于 manifest.total，全部 training_admission=false、human_review_required=true，不添加 human_review_id 或人工 approved。

输出父目录必须已存在。所有文件以 CreateNew 写入同父目录 GUID staging；manifest 最后写、发布前后核总取消状态、Directory.Move 整体发布新目录。普通单项 HTTP 失败可发布完整失败分母的待审核报告并 exit 3。总取消、磁盘/发布失败不发布完成目录；工作终止后用独立最多10秒归档预算，保留 `failure-audit-<目标名>-GUID` 新目录，只有 `audit.json` 与 `audit-records.jsonl`，不带正常完成 manifest。audit.status 为 cancelled/failed、training_admission=false、quality_evidence=false；全候选分母包含已完成 pending/abstained/failed、未完成 failed，并分别统计 attempted/not_called。未调用项 teacher_status=not_called、http_attempted=false；此前收到的原请求/响应/hash/usage 保留。若取消发生于正常原子 move 边界，先按本次已记 hash 删除正常 manifest 并将自建目录移离成功名称。非法前置输入没有教师调用，不生成成功目录。若磁盘/归属错误连完整 audit 都无法写入，保留现有自建证据目录并输出精确 incomplete_audit 路径与原因，不能伪称完整归档。

退出码：0=无 failed 的完整待审核采集（可含 abstained），3=存在 failed，124=总取消，1=前置/存储/发布失败。最大自建文件数由正常134项加失败审计最多2项组成，journal 硬上限137；任务保留失败审计交付物，合成检查在 finally 共享最多10秒清理，只删除已记自建且路径/大小/hash吻合文件和已记空目录，所有不匹配/reparse/非空保留对象均报回执。原生网络/文件 I/O 仍须监督者外层 bounded process 限时兜底，记录 PID/创建时间/命令/父链与资源回收；工作总预算最多1200秒，之后只允许该10秒 audit/finally阶段。

## 合成检查范围

`self-test-collection --max-cases 1` 先执行极小 trial，再 `self-test-collection` 最多 10 case、15 秒工作预算及共享10秒清理预算：成功、重复属性畸形标签、拒答、503、请求取消模拟、总取消、传输失败、响应超限、已存在目标保留、前置 hash mismatch。总取消 case 使用2题，在第1题完整合成响应/usage记录后取消，明确检查成功目录不存在、failure audit全分母=2、attempted=1/not_called=1、原响应hash/usage保留；其他 case 只有1题。归档另有最多10秒异常处理预算，外层40秒runner覆盖微型合成检查与清理。

首个 case 另核 candidate.id 被误填到 model 时前置拒绝、零 HTTP 与无输出目录；正常 handler 逐次核 model=`qwen3.5-9b-q4-k-m`，完整 manifest 核 teacher.id 与 teacher.model 分别保留合同编号与 API 模型键。该回归不新增 case 数量或 HTTP 上限。

检查使用内存 HttpMessageHandler 与 loopback 地址，仅测试 transport/文件合同；不启动 loopback server、不调用真实 Tomur、不下载/哈希真实模型。合成准入文档包含明确 synthetic-test-only 标记，只在内部 synthetic transport 路径允许，真实入口拒绝其 license/hash/bytes。合成报告明确 synthetic_transport=true、quality_evidence=false、independently_verified=false。临时目录是唯一 GUID 任务对象，finally 按自建 journal/hash 逐项回收。

实际源码构建/测试结果由监督者记录。本轮模块作者未启动构建、测试、服务或实际 HTTP。

## 监督者实际验收：2026-10-08

完成入口与项目引用接线后，监督者复核独立审查发现的总deadline发布竞态、清理边界、异常映射和取消归档顺序修复。根据固定上游API证据，将真实模型预期大小纠正为5,680,522,464 bytes，随后重新构建并重新运行适用检查，没有以旧构建结果验收新源码。

最终 `dotnet build tools/Sezika.TeacherTool/Sezika.TeacherTool.csproj -c Release --disable-build-servers -m:1 -p:UseSharedCompilation=false -nodeReuse:false`，PID96720，上限180秒，0 warning/0 error。产物 `Sezika.TeacherTool.dll` SHA-256为 `943CD693C0E05A881589894B4DB310607CC12D3F0052740C421FC251238ADB55`。命令由固定PowerShell7.6.6、dotnet10.0.401和 `tools/Invoke-BoundedProcess.ps1` 执行，日志位于 `.artifacts/supervision-20261008/processes`。

| 检查 | PID | 上限 | 实际runner结果 |
| --- | ---: | ---: | --- |
| 最终1-case合成trial | 63016 | 30秒 | exit0，1/1通过 |
| 最终10-case合成合同 | 46988 | 40秒 | exit0，10/10通过 |
| 旧离线合同1-case trial | 72492 | 100秒 | exit0，1/1通过 |
| 旧离线合同完整24-case | 95360 | 260秒 | exit0，24/24通过 |

最终取消case真实写出合成failure audit，total=2、pending=1/failed=1、attempted=1/not_called=1；监督者确认原响应hash与usage保留，没有成功目录。所有内存合成请求均明确 `actual_teacher_called=false`；未启动网络server，也没有读取或哈希真实GGUF。旧24-case检查使用生产大小修正前的构建，只作为旧离线命令合同回归；最终采集代码的验收以重建后的1→10检查为准。

外层trial后曾错误用未由runner设置的PowerShell `$LASTEXITCODE` 判定失败，造成后续命令未启动；trial的正式runner记录始终exit0。随后分别运行完整检查并从确切result文件的Status/ExitCode/CleanupErrors核验，未将该调度错误归为模型失败。5份上述runner记录均Succeeded/exit0/cleanup为空；登记PID查询无存活对象，两处最终GUID fixture目录已由finally清理。日志与证据保留，无独占MCP服务或其他任务文件清理。

工程合同已通过；真实教师模型与条款、实际本地服务、32题采集、人工训练准入及质量仍未验收。固定来源的小文件核查及默认loopback连接失败见[来源预检查](evidence/teacher-source-preflight-2026-10-08.md)。

同日接续固定context_size=2048请求预算，正常请求和取消归档重建请求一致，完成manifest包含maximum_context_tokens=2048。Tomur支持该字段已由固定源码核实；避免使用服务默认的大上下文。最终重建PID87004/上限180秒/exit0/零警告错误，新DLL SHA-256为 `0C0FC3F80DFE60F0DB4472AD4B714556E4A37A6C6E4E990661FE6180E84CECEB`。1→10合成检查PID51236/97132（各40秒上限）均exit0，并直接检查送入handler的HTTP body中context_size=2048、max_tokens范围与stream=false。三个登记PID无存活对象，两处新fixture目录已清理；此前无context字段的DLL hash和历史检查原样保留。没有重跑旧合同或宣称真实服务资源已达标。

## 同日接续：API 模型键修复验收

监督子智能体按主线程授权修复 candidate.id 与 API model key 的误等同，未修改 pilot 数据资产。真实模型固定 SHA-256、40-hex revision/source 绑定、Apache-2.0 与5,680,522,464-byte门禁原样保留。catalog、terms/model evidence、HTTP请求/响应及 teacher.model 使用 `qwen3.5-9b-q4-k-m`，teacher.id 保留 `qwen35-9b-q4km`。

| 验证 | PID | 外层上限 | 实际结果 |
| --- | ---: | ---: | --- |
| Release build | 13988 | 180秒 | exit0，零警告/错误 |
| 1-case synthetic trial | 71432 | 60秒 | exit0，1/1通过 |
| 完整 synthetic checks | 49192 | 60秒 | exit0，10/10通过 |
| validate-catalog 正确API模型键 | 43552 | 20秒 | exit0，independently_verified=false |
| validate-catalog 候选编号误作模型键 | 35312 | 20秒 | 预期exit1，拒绝身份且未写报告 |

两个synthetic运行均包含首个case的误用模型键前置拒绝检查；正常handler核确切model key，manifest核Id/Model分别保存。完整取消case仍total=2、attempted=1/not_called=1。CLI正反输入明确标记synthetic-test-only，只核旧离线校验命令，不充当生产准入、许可审查或真实教师证据。

固定PowerShell7.6.6/.NET SDK10.0.401，DLL SHA-256 `CCF65714E4D3E3EE853FBE806ECE3D2E44F12E4C18EC6BC442428D285B7D3557`。过程与 `model-key-verification.json` 保留于 `.artifacts/teacher-model-key-20261008-head-checkpoint`；5个runner结果无cleanup error，记录的8个PID均已退出，两个确切GUID fixture根目录已由finally移除。日志和明确的synthetic CLI输入/报告作为证据保留，不删除共享build cache。未启动服务、下载/哈希模型或调用真实HTTP，没有质量或训练准入宣称。旧model=id的准入文件将被新版校验拒绝；本次按范围保留既有pilot资产，实际采集需使用单独准备并审查的正确准入文档。
