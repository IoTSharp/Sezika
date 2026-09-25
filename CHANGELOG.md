# Changelog

## Unreleased

- 完成 S3-08 固定 Laya 0.3.20 的独立 oracle：隔离环境和现有权重先完成 1 条 smoke，再捕获 46 条真实参考（42 answered、4非法拒绝）。新增 `OracleCapture` 执行 C# 生产引擎，`OracleCompare` 在原文件身份/哈希校验后显式比较子集，保留覆盖率和完整合同差异；负向回归验证 token 变更和无效选择会被拒绝。
- 实现 S3-09/S3-10 共享 `PromptSequenceBuilder`：题型前缀、原始候选顺序、Python JSON 文本语义、mask 清理、Boolean false/true 映射与默认描述；256 前缀和 1024 总长度分离，默认 strict，显式 laya_compatible 截断并返回轻量诊断。tokenizer 支持取消，移除 bytes/4 估算 token 的提前拒绝。
- SIMD/CUDA 分别完成 46 条真实捕获，其中38条数值输出与4条非法拒绝通过冻结参考合同；scalar完成18条三题型×中英×短中长核心矩阵。Choice 1/33、Score 1/11 四条数量限制差异单列，完整上游合同未通过。最终 solution build 零警告/错误，prompt/runtime、资源、CUDA 诊断分别137/35/43项通过；见[实测证据](docs/evidence/laya-parity-2026-09-25.md)。本轮未训练、下载权重或发布，修正路径的AOT、质量与性能仍须独立验收。
- 重测修正输入的token覆盖率：PAWS strict/compatible均250/250，Nimble strict306/324、compatible324/324；Nimble严格拒绝的18条均涉及说明或候选前缀裁剪，state无裁剪。外部题文及完整raw报告留在本地忽略目录，仓库保留无题文派生统计与哈希；这些统计不代表模型答案正确率。
- 推进 S4-05 的 `DatasetTool audit-splits`：添加来源/许可/用途准入、家族/实体/Unicode fingerprint/近重复及衍生关系隔离审计、已查看评测内容隔离和封存声明检查。构建通过，5条原创样例实际返回预期blocked/exit3，识别10项许可/人工复核/封存阻断；不代表真实训练数据已获准或测试集已封存。
- 推进 S5-06 的 `Benchmarks --mode profile`：增加短/中/长与 1/8/32 问输入矩阵，复用当前共享输入构造器，记录真实pipeline token、strict拒绝、覆盖率及独立阶段计时，报告schema v2。工具已随solution构建通过，尚未执行真实性能矩阵或新AOT验证，没有新增性能结论。Coverage和NumericParity同步区分256前缀/1024总长度及渲染版本。
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
