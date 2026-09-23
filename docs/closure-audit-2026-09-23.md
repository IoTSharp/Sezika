# Sezika 路线图闭环审计

审计日期：2026-09-23。审计目标是把实现、验证证据和路线图状态逐项对齐，不把 smoke、代码存在或构建通过写成阶段完成。

| 阶段 | 当前状态 | 本轮可复核证据 | 仍缺少的完成条件 |
| --- | --- | --- | --- |
| 0 研究与契约 | ✅ 已完成 | 固定参考版本、协议边界、仓库规则和 Tomur 对接设计已提交 | 无；后续契约冻结仍按版本策略执行 |
| 0.1 GPU/AOT 最小关口 | ✅ 已完成 | 14 个 PTX、ABI/hash manifest、非整齐 vector-add/GEMM、资源回收和 win-x64 CUDA/tiny AOT smoke；16 个生成物重生成后 SHA-256 完全一致 | 这只证明最小关口，不代表完整 GPU encoder 或性能阶段完成 |
| 1 资产与 tokenizer | 🚧 进行中 | 170 个真实张量的文件及逐张量 hash、SafeTensors F16/F32 加载；8 个多脚本 tokenizer oracle；重叠/越界/dtype 负向测试 | Tomur 模型目录的安装/断点下载生命周期、发布资产和完整 manifest 下载验收 |
| 2 纯 C# encoder | 🚧 进行中 | 真实 22 层 FP32 scalar encoder；267 个 trace 节点；CPU/CUDA head 最大误差 `9.536743E-07` | 固定逐层参考 oracle、明确容差夹具、完整取消/超时/unload 资源矩阵 |
| 3 决策闭环 | 🚧 进行中 | `ModernBertDecisionEngine` 使用真实 marker 序列、两层 head、温度和总 token/deadline/head 预算；真实模型 Choice/Score/Boolean typed smoke 通过（84 tokens） | 与独立参考实现逐 primitive 数值对齐、完整并发/取消/unload 矩阵和正式 CLI |
| 4 多语言与校准 | 🚧 进行中 | C# head trainer、temperature fitter 和完整指标计算器存在 | 许可明确的中英数据、实体/时间隔离 split、训练/校准/测试报告和质量门槛证据 |
| 5 AOT/性能 | 🚧 进行中 | CPU tiny win-x64 与 Ubuntu WSL linux-x64 AOT smoke；CUDA win-x64/tiny smoke；CUDA Driver resident encoder/head | 真实模型 CUDA/Linux AOT、SIMD/量化误差、1/8/32 问 p50/p95/p99、RSS/显存/吞吐和取消延迟矩阵 |
| 6 Tomur R22 | 🚧 进行中 | Sezika 核心契约可被宿主静态引用；Tomur 对接设计文档 | Tomur 固定包/provider、Catalog/pull、API、身份校验、取消/卸载、只读工具和跨仓库端到端证据 |
| 7 开源发布 | 🚧 进行中 | `Sezika.0.1.0-dev.nupkg` 开发包已生成并包含 README、LICENSE、NOTICE 和第三方声明 | 正式版本、签名/NuGet 发布、CLI、模型卡/数据卡、真实模型跨平台发布与示例闭环 |

## 本轮验证命令

在 PowerShell 7.6.6、.NET SDK 10.0.401 下，`dotnet build Sezika.slnx -c Release --nologo` 通过且为 0 警告/0 错误；测试项目报告 18/18；模型工具报告 `Verified 170 tensors, model and tokenizer SHA-256`；真实模型 smoke 报告 `real_typed_decisions passed`、CPU/CUDA head `max_abs_error=9.536743E-07` 和 `cancel smoke passed`。Ubuntu WSL 使用 .NET SDK 10.0.112，CPU tiny linux-x64 Native AOT 发布物运行通过。

上述证据不能支持把阶段 1–7 改成 `✅`。路线图必须继续保留这些阶段的进行中状态，直到表格右列的条件有独立记录；尤其阶段 4 的质量报告和阶段 6 的 Tomur 宿主链路不能用本仓库 smoke 代替。
