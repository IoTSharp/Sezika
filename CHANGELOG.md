# Changelog

## Unreleased

- 新增修正输入路径的真实质量审计：固定权重 C# CUDA 实际完成 PAWS strict 250 题（170 正确，68%）和 Nimble compatible 324 题（137 正确，42.28%）；Nimble strict 另处理全部 324 题，306 次真实推理、133 正确、18 次 forward 前截断拒绝。报告绑定输入、实际 token、marker、logits、forward 次数及程序哈希。独立 Laya 参考只覆盖 PAWS 1 + Nimble 45 题，46 条数值/预测均通过冻结合同；Nimble 完整运行中的对应 45 行另与 C# capture 核对一致。完整题集参考、中文切片和校准仍未验收，Nimble Boolean 负类召回仅 1/57；见[质量证据](docs/evidence/quality-aligned-2026-09-26.md)。
- 新增 `NumericParity --trace-oracle` 真实逐层诊断，单次显式选择最多 3 条冻结参考并校验生产 pipeline 的输入与输出；先完成英文 Choice 61-token smoke，再完成中文 Boolean 384-token、英文 Score/中文 Choice 各 960-token 三后端运行。四条输出通过冻结容差，每条 SIMD/CUDA 各完成 27 个 checkpoint 与本次 scalar 的完整张量诊断。内部层误差原样记录，真实近并列为 0，S3-06 全门槛未完成；见[诊断证据](docs/evidence/s3-diagnostics-2026-09-26.md)。
- 完成 S3-10 修正输入路径的 win-x64 / Ubuntu WSL2 linux-x64 Native AOT 长输入回归：两 RID 的 SIMD/CUDA 各完成46条捕获，38回答+4非法拒绝通过冻结合同；scalar各3条960/1024-token长输入通过；两个原生输入测试程序各138项检查通过，四次最终AOT发布无编译/trim警告。新增 `OracleCapture --require-aot` 与运行时身份记录，修复中间托管DLL继承禁用动态代码开关后被误计为AOT的门槛漏洞；两RID负向检查均exit2且未创建输出目录。归档程序/源文件哈希、比较报告和进程清理证据，保留4条候选数量合同差异；见[完整证据](docs/evidence/input-aot-2026-09-26.md)。本项不增加语言质量、校准或性能结论。

- 完成 S3-08 固定 Laya 0.3.20 的独立 oracle：隔离环境和现有权重先完成 1 条 smoke，再捕获 46 条真实参考（42 answered、4非法拒绝）。新增 `OracleCapture` 执行 C# 生产引擎，`OracleCompare` 在原文件身份/哈希校验后显式比较子集，保留覆盖率和完整合同差异；负向回归验证 token 变更和无效选择会被拒绝。
- 实现 S3-09/S3-10 共享 `PromptSequenceBuilder`：题型前缀、原始候选顺序、Python JSON 文本语义、mask 清理、Boolean false/true 映射与默认描述；256 前缀和 1024 总长度分离，默认 strict，显式 laya_compatible 截断并返回轻量诊断。tokenizer 支持取消，移除 bytes/4 估算 token 的提前拒绝。
- SIMD/CUDA 分别完成 46 条真实捕获，其中38条数值输出与4条非法拒绝通过冻结参考合同；scalar完成18条三题型×中英×短中长核心矩阵。Choice 1/33、Score 1/11 四条数量限制差异单列，完整上游合同未通过。最终 solution build 零警告/错误，prompt/runtime、资源、CUDA 诊断分别137/35/43项通过；见[实测证据](docs/evidence/laya-parity-2026-09-25.md)。本轮未训练、下载权重或发布，修正路径的AOT、质量与性能仍须独立验收。
- 重测修正输入的token覆盖率：PAWS strict/compatible均250/250，Nimble strict306/324、compatible324/324；Nimble严格拒绝的18条均涉及说明或候选前缀裁剪，state无裁剪。外部题文及完整raw报告留在本地忽略目录，仓库保留无题文派生统计与哈希；这些统计不代表模型答案正确率。
- 推进 S4-05 的 `DatasetTool audit-splits`：添加来源/许可/用途准入、家族/实体/Unicode fingerprint/近重复及衍生关系隔离审计、已查看评测内容隔离和封存声明检查。构建通过，5条原创样例实际返回预期blocked/exit3，识别10项许可/人工复核/封存阻断；不代表真实训练数据已获准或测试集已封存。
- 推进 S5-06 的 `Benchmarks --mode profile`：增加短/中/长与 1/8/32 问输入矩阵，复用当前共享输入构造器，绑定实际 pipeline 输入、应用程序集、预算及独立阶段计时。普通 .NET CUDA 九行各三次正式采样，long-32 p50 为 40.464 秒，数值 smoke 与资源归还通过；CPU SIMD 八行各一次采样，long-8 为 141.973 秒，long-32 在 discovery 达到单请求 300 秒期限后失败，无正式样本。失败矩阵保留全部九行分母，末尾数值/对象回收检查未执行，独立 CPU smoke 单列。这些小样本不代表新 AOT 基准、稳定尾延迟或语言质量，见[性能证据](docs/evidence/s5-profile-2026-09-26.md)。
- 加固证据工具的真实性检查：离线 capture 负例增加一次正常成功基线，再逐项核对八种预期错误，9 项验证通过；Benchmark 增加托管宿主与动态代码状态判断，普通 DLL 和禁用动态代码开关的 DLL 都在模型加载前被 `--require-aot` 拒绝，两个负例通过。修正部分 pipeline 调用后中断时的报告标签，以及 CUDA 分配峰值的重置区间口径；最终相关构建为零警告/错误，已测旧程序和原报告哈希原样保留。
- 完成 S5-04/S5-05：新增 W8A32 encoder/head、统一后端注入、CUDA 时间/显存遥测及有界基准/证据汇总工具；固定真实模型的 Windows `win-x64` Native AOT scalar/SIMD/int8/CUDA 四后端覆盖 1/8/32 问、每项 5 次正式采样，Ubuntu WSL2 `linux-x64` 四后端各通过 3 问、1 次采样的真实模型 AOT smoke。两 RID 的 AOT 发布无警告；最终 solution build 为 0 警告/0 错误，核心/资源/CUDA 测试分别为 37/35/43 项通过。见 [S5 证据](docs/s5-performance-aot.md)及[原始报告](docs/evidence/s5-2026-09-24/)。
- 记录当前性能限制：W8A32 慢于 SIMD，保留 FP32 原权重并增加量化缓存，没有总内存下降；WSL smoke 不代表裸机 Linux 性能，小样本分位数不构成稳定尾延迟结论，多语言质量仍未验收。
- 新增独立 CPU `inspect`/`predict` CLI 和 `DecisionModelRuntime.Load/Evaluate` 类库入口，支持模型目录、文件/stdin JSON、三种 typed decision、输入预算、取消和结构化错误；修复并发 Dispose 清空活跃推理权重的竞态。固定真实 Laya/mmBERT 的中英文三问题请求已通过运行验证，仍返回 `uncalibrated`。
- 完成 S1-04 的有界模型包发布目录、loopback HTTP 断点 Range 下载、SHA-256 校验、安装清单、staging 原子提交与 lease 卸载；完成 S2-03 的 scalar/SIMD encoder 对齐、取消/deadline、workspace 和并发矩阵；完成 S3-03 对齐契约与 S3-04 session/资源预算及 unload 生命周期；建立 S4-02 中英原创 fixture 数据卡、split/provenance manifest 和 S4-03 的 hash 绑定 `pending_measurement` profile 与冻结门槛。S1-04 不下载真实模型；真实中英 primitive 参考数值和多语言质量指标仍未验收。
- Added the real mmBERT marker-head request path (`ModernBertDecisionEngine`) with bounded total-token/deadline/head budgets and Choice/Score/Boolean typed smoke coverage. Added negative SafeTensors tests for overlapping, out-of-bounds, and unsupported-dtype ranges, plus CPU win-x64/linux-x64 Native AOT smoke samples. Kernel artifact generation is now BOM-free and newline-stable, with byte-identical PTX/manifest regeneration evidence.
- Made the core library packable as the development `Sezika.0.1.0-dev` NuGet package with repository documentation and license notices included.
- The pinned model loader now checks every manifest tensor's dtype/shape entry and raw-byte SHA-256 before constructing the encoder or decision head.
- Added bounded request parsing, duplicate-property rejection, SafeTensors model assets, the pinned Apache-2.0 Laya/mmBERT package manifest and tokenizer oracle, scalar FP32 encoder/head, typed Choice/Score/Boolean engine, calibration metrics, ILGPU 1.5.3 build-time PTX/ABI artifacts, resident CUDA encoder/head, and Native AOT GPU smoke evidence. The ignored model artifact is not a release or multilingual quality claim.
- 建立独立本地 Git 仓库、项目定位、参考分析、架构与路线图。
- 加入 .NET 10 类库工程及 typed decision 契约草案。
- 明确纯 C# CPU/GPU 与系统驱动例外，记录 ILGPU 构建期导出 PTX、Native AOT CUDA Driver 执行的设计及原型关口。

真实模型 CPU/CUDA 数值、Windows 四后端 AOT 基准与 Ubuntu WSL2 四后端真实模型 AOT smoke 已有记录；多语言质量与裸机 Linux 性能仍未验收。
