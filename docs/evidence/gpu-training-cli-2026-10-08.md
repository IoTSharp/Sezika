# S4-07 GPU 训练 CLI 切片 C

状态：显式 CLI、准入/恢复预检、完整序列 CUDA exporter 与 schema3 评价 adapter 已实现，CPU 合同 tiny 2 项/full 22 项、GPU exporter tiny 8 项/full 12 项及固定真实 encoder GPU smoke 全部通过。当前没有已正式准入数据，未运行正式原始数据训练，不声称语言质量、性能或训练 AOT。

本阶段 UTC 2026-10-07 20:12:59 开始，绝对截止 20:32:59；最多八个命名实现、测试和独立证据文件。旧 CPU train-original 保持四项 resume 与 v2 合同，help 明确为小规模参考/诊断入口；正式/批量 GPU 入口为 train-original-gpu。新增 --device 0..15 默认 0，GPU resume 必须 checkpoint/SHA/head/report/SHA 五项完整提供，最多十六 option pairs/33 参数。未知、重复、缺值、非有限数和部分 resume 明确拒绝。

执行顺序是输出防覆盖→records/manifest 准入并持只读 lease→resume 报告 SHA/严格 schema3/正式 provenance/records/优化器预检→显式打开 CUDA device 并比较实际 execution→固定许可/版本/hash 的 model loader→以实际 model hidden width 和身份再次绑定旧报告→CUDA encoder 前向→GPU head SGD。包内 model.json、encoder.safetensors、tokenizer/tokenizer.json 被列入输出保护。正式入口不读取 pending 教师文件训练，不更改真实审核状态；所有诊断 accepted 文本仅在 GUID 隔离的 synthetic parser fixture 中模拟外部声明。

IndependentCudaFeatureExporter.Export 仅接受具体 CudaModernBertEncoder，调用其完整序列 Encode，不调用 scalar encoder 前向。loader/encoder 构造中的 scalar 对象只验证共享 config/weights 合同。完整 tensor shape、最多1024 tokens/2048 hidden、所有元素有限性及 marker 顺序/目标边界受现有严格 sequence builder 和新 exporter 检查；host 只提取 marker 行。至多32题/10分钟/取消/逐题进度，resident CUDA encoder 在 head SGD 前释放。合成 CUDA encoder→workflow 诊断保持 synthetic_fixture，不冒充正式原始 encoder/features。

evaluate-original adapter 严格支持完整 CPU schema2 或 GPU schema3，GPU 使用 BindApprovedIdentity 拒绝工程 fixture，随后既有实际 model/feature/head 生产 loader 绑定继续执行，head 的四项优化器 metadata 同报告核对。评价明确 evaluation_backend=scalar-modernbert-fp32-linear-marker-head-fp64，并单列 training_backend，避免将 GPU 训练身份当作 GPU 评价。旧 schema1 兼容读取路径保留。

显式 smoke-original-gpu 只读取固定、已审计 original package，以合成布尔题运行真实 CUDA encoder、完整 tensor/marker finite/hash 和资源归零检查；不训练 head、不发布正式训练三文件。它的 package/device/1..180秒 timeout 参数有明确未知/重复/缺值拒绝。

固定 PowerShell 7.6.6，dotnet 为 C:\Program Files\dotnet\dotnet.exe/SDK10.0.401。构建串行 --disable-build-servers -m:1/-nodeReuse:false/UseSharedCompilation=false，Invoke-BoundedProcess 记录 PID、启动、完整 argv、父链和 bounded timeout，finally 精确清本任务树。首 build PID89896 零warning/error；checks build PID68784 暴露旧 WorkflowTrainingReport 没有 SerializeBounded（失败日志保留），改为提取已经严格序列化 schema3 的 training 原 JSON bytes后，修复 build PID55064 零warning/error。

CPU tiny PID66772/35秒通过2项，full PID79332/35秒通过22项：device/未知/重复/缺值/非法数/partial resume、CPU四项兼容、pending 准入先于 package/CUDA、33项完整 GPU 参数、schema3 恢复预检、错误报告 SHA、旧 schema2 拒绝、schema2/3 评价后端区分、fixture provenance/unknown/duplicate 拒绝和预取消。真实旧 datasets/independent-v1-original 输入经 CLI PID56680/15秒以 training_admission_invalid 拒绝，三输出均不存在，没有加载模型或开启 GPU。

监督者确认教师已卸载/停止，再完成旧 CUDA 59 项回归（监督者PID79192通过，45秒上限），UTC20:30:36明确释放 GPU。其后本切片串行运行 exporter tiny PID35028（8项/3.372秒）、full PID56252（12项/3.095秒），各60秒上限。实际完整Choice/Boolean/Score序列的marker rows与小型scalar oracle在1e-4绝对+1e-4相对容差内，保留候选顺序；预取消零launch，encoder释放先于head SGD，schema3 2+2恢复等于连续4步，caller-owned CUDA资源归零。CPU oracle只属于数值诊断，没有正式训练CPU fallback。

真实固定 encoder GPU smoke PID75592以内85秒/外100秒的剩余阶段预算通过，实际17.31秒。model=sezika/mmbert-base-independent-s4，source=jhu-clsp/mmBERT-base@c5955035435e2bf121cde7f3c8863ef52ff35d82，license=MIT，encoder SHA=6168c84d2498cdacdcafee16b71a69b5c1636ad569bfabaf2855f5ce7dabd528，tokenizer SHA=197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356。实际UUID=134BF256A4B04FD4E3073C3D34F79CB4，48tokens/markers27,37/hidden768，marker features SHA=59194DCFF642F4790DBF55F58D38EFBAB200C0528BEE25894DE38D71296B1CCD，310kernel launches、owned_bytes=0、head_training=false、formal_training=false。该结果是只导出真实GPU encoder特征的工程smoke，不是正式数据训练或语言质量验收。

日志保留于 .artifacts/gpu-training-cli-20261008/logs，process-audit.json与closure-result.json保存本切片执行和精确PID/StartTime/command/parent复核。CPU/GPU checker 的临时目录具有独占 GUID、解析绝对路径及 prefix 核对，finally 只回收本次目录；共享 bin/obj、原失败和交付证据保留。GPU已释放。独立review发现的实际model hidden身份应在export前再次绑定的问题已修复，root另核Program smoke/评价metadata增量。Git 与共享计划文档由监督者维护。
