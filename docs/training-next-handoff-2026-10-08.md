# 福海下一轮 GPU 训练交接

准备日期：2026-10-08，Asia/Shanghai。准备基线 `86951b1878825f8d2c378e92e9629de73b69db8c`。用户要求准备训练信息与交接，并在新会话开始运行和关注训练。本文件是执行交接，阶段计划仍以 ROADMAP.md 为准。

## 目标与授权

下一项为**同24题 GPU 可学性与候选位置诊断**：先评价已有72步权重在训练题上的表现，再完成一次预先固定768步的新实验、保存新权重、独立进程加载与实际验证。当前会话仅准备、提交交接和创建新会话；创建后由新会话独占源码修改、Git和GPU任务调度，当前会话不并发实施。

允许补充下述最小评价前置及必要构建、离线传输和GPU执行，不再逐项询问确认。其他 ROADMAP 任务继续暂停；本轮不采集新教师答案、不扩大数据、不搜索超参数、不增加encoder反传/LoRA，不上传模力方舟/抱抱脸，不push、发布或部署。正式质量、人工审核、许可发布、校准和封存仍单独验收，实验可以先执行。

## 已完成证据与诊断缺口

上一轮真实实验见 [永久证据](evidence/real-gpu-head-experiment-2026-10-08.md)。24条训练题、8条已曝光开发题，零初始化、72次GPU更新；2304个head参数实际改变，encoder冻结。train CE `1.229626456896464 → 1.1749068026741345`，dev CE `1.2687934339046478 → 1.2772859781980515`，开发题教师一致2/8。已有训练、磁盘保存和新进程验证不是待执行任务，不重复上一轮训练。

逐题开发输出：4条Choice全部选择第一候选；Boolean两题都预测false而教师为true；Score一致1/2。训练Choice的四个答案位置各2条，Boolean真/假各4条，不能据此认定训练标签多数偏置。Score训练题没有0标签，开发Boolean没有false题；现有样本不能证明代表性质量。

目前报告没有train24逐题预测，无法区分未充分拟合、候选位置影响和表示能力不足。现有 `Sezika.ExperimentTrainer` 可支持768步，但Infer只评价development8。需要以下最小工程前置：

1. 独立只读评价命令/报告，读取已有head/checkpoint/report，输出train24完整逐题logits、概率、教师目标排名、一致率、CE及中英/题型分组。旧v1训练报告原字节保持，不往旧报告追加字段。
2. 对8条**训练Choice**各做一次固定左循环换序：候选文本与ID一起移动，通过ID重映射教师目标；重新渲染、tokenize、真实GPU encoder编码，不通过重排旧特征冒充换序。记录原/新ID顺序、token/marker身份、预测ID和目标排名。变体只用于诊断，不加入训练、不改变原24/8划分。
3. 新评价报告静态类型及source-generated JSON，保留旧v1读取/验证；记录真实模型、数据、权重、特征与执行身份、完整分母和失败。先做有界tiny合同检查，再用实际输入执行；fixture不能当真实训练证据。

## 固定模型、数据和产物

| 项目 | 已核身份或路径 |
| --- | --- |
| encoder | `jhu-clsp/mmBERT-base@c5955035435e2bf121cde7f3c8863ef52ff35d82`，H768/22层/134 tensors |
| encoder SHA256 | `6168c84d2498cdacdcafee16b71a69b5c1636ad569bfabaf2855f5ce7dabd528` |
| tokenizer SHA256 | `197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356` |
| 实验manifest SHA256 | `b8c8d5e018c2bc0029ba100f8f46272ac4cb3b1efea27280ff449f685778b954` |
| records SHA256 | `a015a8fa182ee62223cafc307c618c515a19e90f843290db9dfb53a56b9a041c` |
| provenance SHA256 | `4504b0fbfa666be2f5fe62d5f568c5e7739e8d0c0713bdd97b1473bc2c0cb356` |
| 教师 | 实际GPU `qwen3.5-9b-q4-k-m`，量化revision `3885219b6810b007914f3a7950a8d1b469d598a5`，固定GGUF hash见旧报告；本轮无需再启动教师 |
| 旧head SHA256 | `2C93C6A9FD28C491AE5718F00C81C5FFB08859FC7F85C6A8F4DF6B209A5D5AAD` |
| 旧checkpoint SHA256 | `5C604C9510658373EB2933C2D82FAE0FF0E798D8837C59D3B66E5BE7A63C3528` |
| 旧训练report SHA256 | `C6B065CD0C66AA4544C0AF277C539D82A4E7932C66CD41AAAD37CFC1A0D30EE7` |

本地固定底座：`D:\source\Sezika\.artifacts\models\mmbert-independent\converted\`。

本地三文件数据包：`D:\source\Sezika\.artifacts\real-gpu-experiment-20261008\data\`，准确文件为 `experiment-manifest.json`、`experiment-records.jsonl`、`input-provenance.json`。原数据保持 `human_review=pending`、`teacher_output_training_permitted=false`，独立声明 `user_authorized_experiment=true`；不自动改为正式approved。

本地旧产物：`D:\source\Sezika\.artifacts\real-gpu-experiment-20261008\retrieved\output\run-03\`。永久原字节报告：`docs/evidence/real-gpu-head-experiment-20261008/{training-report.json,validation-report.json}`。

远端旧任务根：`/root/sezika-experiments/20261008-real-head-v1/`。`package/`是固定底座，`data/`是三文件数据包，**最终执行工具是 `bundle-v3/Sezika.ExperimentTrainer`**，旧产物为 `output/run-03/`。复用这些只读资产，避免重复下载1.2GB encoder；新工具/日志/权重使用新目录，不能覆盖旧bundle或run-03。

建议新任务目录：本地 `D:\source\Sezika\.artifacts\gpu-learnability-20261008\`，远端 `/root/sezika-experiments/20261008-head-learnability-v2/`。创建前检查不存在或核实已有任务身份；若已有部分执行，先读回执接续，不能盲目重跑。

## 连接与已核资源

目标SSH：`root@211.149.170.42`，端口 `2222`，主机 `digisyn-gpu01`，内网 `158.16.16.120:22`。22000是独立跳板管理口，不用于训练。复用现有密钥和已确认host key；不用向用户再次索要连接信息，不输出密钥/密码。

已核Ubuntu24.04.4，GPU名称RTX4090，驱动报告总49,140MiB（约48GiB），UUID `GPU-e786e313-bc92-a63d-a833-f4e48cbafb66`；驱动580.173.02、CUDA Driver API13000。上一轮空闲15,294MiB，owned GPU峰值1,309,369,952B、host约2.72GB。以上是历史实测，**新阶段启动前重新确认实际UUID、空闲显存≥5120MiB和资源可用**；资源不足暂停GPU阶段，不能停止既有服务来腾空间或退回CPU批量执行。

已核本地PowerShell `C:\Program Files\PowerShell\7\pwsh.exe` 7.6.6；dotnet `C:\Program Files\dotnet\dotnet.exe` 10.0.401；git `C:\Program Files\Git\cmd\git.exe`。上次远端工具是linux-x64 self-contained/runtime10.0.12，无全局安装。新工具复用现有runtime来源并冻结实际版本；不得用Windows托管进程运行Linux自包含文件。

## 执行规格与停止条件

本轮只有一个固定新训练配置：24train，冻结encoder，三行head从零初始化，SGD **768次更新（32×24）**、LR0.001、seed17、`strict`、显式token上限1024。这是新实验，不是从旧72步恢复；现有CLI不支持resume。8dev保持只诊断，不用于挑选步数或参数。

阶段A：已有72步权重的train24及8条Choice换序只读GPU评价，最多32个真实encoder forward，独立新日志和评价报告。

阶段B：新工具冻结并发布后，只启动一次固定768步训练，记录初始化与终态train24/dev8完整GPU CE、真实更新数、参数变化及GPU峰值；保存新head/checkpoint/report并加载评价train24/dev8/换序8。允许复用**本次实际GPU导出的、身份一致的原始特征**计算逐题head评价，换序必须重新GPU编码。阶段最多72个真实encoder forward，不能自动增加轮数/配置以追求某个分数。

阶段C：独立新进程从磁盘加载新权重与报告，重新GPU编码、完整CE、train24/dev8及换序8评价，最多72个真实encoder forward，校验磁盘/设备参数和逐题输出一致。每项原始输出保留，不截JSON最后一行。

三个阶段分别：SGD≤300秒（仅阶段B）、CLI总≤460秒、Linuxrunner工作≤480秒+独立清理8秒、外围timeout492秒+TERM后3秒、Windows外层≤530秒。owned GPU≤3GiB，单进程host≤4GiB，GPU负载并发1。每个阶段只执行一次；失败保留原因、真实更新数和回执，不自动重试。前置工程可纠正明确错误后重验，但每次重验设独立新预算和日志。

评价不预设结果通过。若train拟合仍弱或换序影响明显，报告事实及后续可选修正；本轮不能自动扩到更多数据、MLP或encoder反传。训练集拟合也不代表泛化或正式正确率。

## Runner、SSH与资源回收

长命令使用 `D:\source\Sezika\tools\Invoke-BoundedProcess.ps1`，建议 `ProcessQueryTimeoutSeconds=5`，记录PID、创建时间、完整argv、父链及任务归属，所有成功/失败/取消/超时均finally核验并只回收本任务完整子树。所有循环/轮询/重试/搜索同时有最大数量与墙钟、取消和进度；先人工检查比较退出条件并做tiny带超时试运行。禁止广域找工具、按进程名杀进程或Graphify。

机器级SSH脚本传送器：`C:\Users\mysti\.codex\skills\usm-gateshell-ssh\scripts\invoke-ssh-bash.ps1`。Bash用单引号PowerShell here-string，通过stdin和结构化参数发送；禁止PowerShell可展开双引号拼接Bash。包装器必须立即捕获并返回 `$LASTEXITCODE`，不能裸用无界WaitForExit。

旧任务专用修复包装器 `D:\source\Sezika\.artifacts\real-gpu-experiment-20261008\Invoke-FuhaiScript-v2.ps1` **只允许旧目录中的脚本**。新任务需单独准确范围的包装器，不扩宽旧guard。SSH参数沿用BatchMode=yes/ConnectTimeout=8/StrictHostKeyChecking=yes/ServerAliveInterval=5/ServerAliveCountMax=2。网络直连失败再单次代理7890，不能全局代理影响内部连接。

已执行的旧成功脚本仅供读取合同：

- `.artifacts/real-gpu-experiment-20261008/remote-train-root-v3-attempt3.sh`
- `.artifacts/real-gpu-experiment-20261008/remote-validate-root-v3.sh`
- 旧Linuxrunner `/root/sezika-experiments/20261008-real-head-v1/Invoke-BoundedLinux.sh`，已执行版本SHA256 `534a54a2136e6e27a49e69c3f7b792ec90ec2fd8f89fdecf9a96b9065507fc79`。

不要直接再运行这些成功脚本，也不要运行 `remote-execution-preparation/` 中run-01/token256的prepared草稿。新任务核runner实际hash，使用新任务token与日志；Linux记录identity.tsv/result.tsv/payload stdout/stderr，支持cancel.request，独立后核PID/start ticks/argv/父链。

验收result至少包含completed/payload_exit0/final_check_complete1/cleanup_incomplete0/verified_live_count0/unknown_count0/unchecked_count0/cleanup_budget_exhausted0。收据字段通过仍需独立终核已登记身份、Dispose返回及GPU bytes/alloc/modules/release failures归零，不能只凭Windows exit0认定完成。

下载/日志/temp均登记精确归属，删除前核绝对路径；仅清本任务不再需要的对象，保留旧失败、用户文件和新交付物。上一轮最终资源回执 `.artifacts/real-gpu-experiment-20261008/root-local-resource-closure-final.json` SHA256 `9BFA3D659D9316E436EE12B32EC5B822EF664B281B112971C629A2FCE92BC7E1`，53回执/142记录/146PID无matchingLive或unknown；这是已核记录范围，不声明全机零进程。

## 提交、交接与30分钟关注

新会话先读AGENTS.md、本文、HANDOFF.md、ROADMAP.md、git status和子智能体状态；监督者可并行委派前置工程与只读审查，必须给准确文件归属并重复上述数量/墙钟/PS7/进程/temp/Graphify边界。GPU执行独占，Git由新会话监督者独占。

当前未提交暂停文件：ROADMAP的诊断CLI登记hunk、diagnostic-runtime证据、Benchmarks/IndependentModelTool相关诊断源码，以及用户 `docs/wechat-introduction.md`/`.html`。保留原样，不借新训练提交带入这些文件，不reset/stash其他会话工作。

前置评价工程审查及适用验证后单独commit；真实768步实验/新权重验证完成后，永久保存原始报告、产物hash/位置/冻结配置/失败和资源回收证据，更新ROADMAP/HANDOFF并单独commit。权重独立管理，不嵌入二进制或Git源码包。其他正式验收标志保持false，不将教师一致率叫金标正确率。

现有heartbeat `sezika` 每30分钟将转到新会话，只监督本轮既定范围、未完成审查/提交/回收及断线接续；运行中遵守自身预算，不因定时触发重复派单或训练。无变化保持安静，仅实质完成、失败、证据损坏或确需用户处理才通知。全部本轮闭环后停止新增训练，等待新指令。
