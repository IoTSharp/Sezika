# S4/S5 并行工程续审与统一验收（2026-10-03）

从 `81a027711356d1c15bbe3526567cfcfb22084034` 开始。本轮先读取 AGENTS、ROADMAP、工作区和提交状态，`git ls-remote origin refs/heads/main` 实际返回同一 SHA；工作区只有两份未跟踪的 `docs/wechat-introduction.*`，它们未修改、未暂存。使用三个子智能体按互不冲突的文件边界推进校准、教师和性能工具；主智能体负责独立 loader、开发指标、合并审查、构建/测试和提交。

本轮是工程合同与证据工具验收。**实际独立模型质量、校准、教师服务/采集、两 RID 真实模型 AOT 和性能优化收益仍 pending/blocked。** 本工作区的 `.artifacts/models/mmbert-independent/converted/encoder.safetensors` 不存在；没有重新下载模型、训练或调用教师。历史模型/数据许可声明及原始身份保持原样，本轮合成 fixture 不是模型输出或人工批准。

## 实质改动与文件边界

| 任务 | 实现/审计结果 | 文件与独立证据 |
| --- | --- | --- |
| S4-08 | 全样本 accuracy/F1/ECE 与 selective 分母分离；补 Boolean 负类召回、Score 期望 MAE；verified 要求有限基线差值、sealed_test 声明和 Score MAE；pending/rejected 保留失败观测；拟合/评价增加墙钟、工作量、取消和进度 | `Calibration*.cs`、独占校准测试；[校准续审](calibration-continuation-2026-10-03.md) |
| S4-10/S4-11 | 拒绝普通/嵌套/转义重复属性，严格 UTF-8 字节上限、同快照 hash、家族隔离、pending/invalid 全分母和不覆盖输出；catalog 声明不等于独立验证；未实现或声称真实采集 | `TeacherTool` 与独占回归；[教师续审](teacher-continuation-2026-10-03.md) |
| S4-15 | 固定来源/资产路径/字节数、完整 encoder 数值配置、单独 head/uncalibrated 状态和全部 134 tensor 的 F32 精确维度；拒绝重复/未知字段、链接/junction、路径穿越和非规范 tensor 名称；加载有 10 分钟总取消预算 | `IndependentModelLoader.cs`、`IndependentModelContract.cs`、独占 loader 测试及 `Verify-Preflight.ps1` |
| S4-06/S4-08 开发诊断 | 禁止不同 primitive 或局部 Choice 候选位置混合池化 AUROC；仅同映射 Boolean/Score 可计算；补全失败分母 accuracy、Boolean 负类分母和 Score 期望 MAE；Ctrl+C 可取消独立工具命令 | `IndependentModelTool/Program.cs` 的 `check-development-metrics`；仍为开发诊断，不是封存报告 |
| S5-06/S5-07 | 校验画像矩阵、原始数值类型/重复字段、完整身份、原始样本/分位数、覆盖率/吞吐分母、discovery/forward 和独立分项范围；只重验既有报告，没有优化或重采集 | `Validate-S5Profile.ps1`、独占回归；[性能续审](performance-continuation-2026-10-03.md) |

loader 原先只验证合法配置范围；修改 head count、attention 范围或 RoPE 后仍能沿用固定 encoder hash。本轮在读大权重前固定数值合同，并检查 tensor 维度顺序，不以元素数量相等替代布局。路径检查拒绝包及其祖先中的链接/junction；这是静态预检查，不能抵御另一个进程在验证后恶意替换文件的竞争。正式模型包、独立运行 session、上游数值 oracle、默认迁移和发布仍待后续验收。

## 统一验证

环境：Windows、固定 `C:\Program Files\PowerShell\7\pwsh.exe` **7.6.6**、`C:\Program Files\dotnet\dotnet.exe` **10.0.401**。未安装编译器或依赖；Native AOT 使用现有 SDK/MSVC。构建设置 `--disable-build-servers -m:1`，关闭节点重用/共享编译，限制并发。所有长命令经仓库有界 runner 记录 PID、启动时间、argv、完整命令行及父链；原始执行与源文件 hash 见 [执行索引](continuation-2026-10-03/execution-index.json)。表中耗时取 result JSON 的 `ElapsedSeconds`。

| 验证 | 墙钟上限 | 实际结果 |
| --- | --- | --- |
| 最终 `dotnet build Sezika.slnx -c Release -v minimal --nologo --disable-build-servers -m:1` | 240 秒 | PID 58688，13.493 秒，退出 0，零警告/错误 |
| 最终核心测试 DLL | 90 秒 | PID 89408，4.302 秒，**139 项通过**；缺模型的真实 tokenizer oracle 明确 SKIP，不能复用历史 350 项计数 |
| 资源测试 DLL | 90 秒 | PID 86816，5.256 秒，**35 项通过**，小型 scalar/SIMD/量化 fixture 的工作区、取消、卸载回归 |
| CUDA 诊断 DLL | 60 秒 | PID 73972，5.245 秒，**43 项通过**，实卡 tiny kernel/encoder 与资源回收；不是独立真实模型对齐 |
| `check-development-metrics` | 30 秒 | PID 51268，5.414 秒，合成映射/分母/Score MAE 检查通过，没有模型推理 |
| Windows Native AOT 合同 publish | 300 秒 | PID 77992，73.570 秒，退出 0，无编译/trim/AOT 警告 |
| AOT EXE `--continuation-contract-aot` | 60 秒 | PID 66404，3.625 秒，**64 项通过**，含动态代码关闭及 TPA 缺失的原生身份检查 |
| 独立 loader 公共入口预检查 | pilot 45 / full 120 秒 | 修正后先 1 项试运行，再 **3 项通过**：attention 身份、固定权重字节数、真实 Windows junction 逃逸分别被预期拒绝；full PID 76808，13.541 秒 |
| 教师合同 | pilot 45 / full 270 秒 | 1/1 与 **24/24** 合成案例通过，详见子任务证据 |
| 画像校验 | 外层 40 秒 | **35 项通过**：两个历史正例检查批次及 33 个非法 fixture 的预期拒绝；无模型执行 |

AOT 发布物位于本地 `.artifacts/continuation-20261003/win-contract-aot/Sezika.Tests.exe`，SHA-256 `3C918052B9654ED647EE5B1BECFAB12618ADC58BE0D074A28F29D4AFE663BD47`。它只验收新增 loader/校准合同 fixture 在 Windows 原生程序中的执行，不能关闭 S4-08/S4-15 的真实模型 CUDA、Windows/Linux 两 RID AOT 或性能验收。新增测试入口不会改变默认 CLI 或模型选择。

## 失败保留、边界与清理

首次 solution build（PID 51872）在 28.011 秒被 CIM 2 秒查询超时中断，未获得完整编译结果。旧 runner 清理时保留了一个退出竞争及未完成确认诊断；随后精确查询其记录的 PID/身份，确认均退出，最终再次审计全部主线程进程。runner 新增 **2–5 秒**可选进程查询上限，默认仍为 2，后续主线程显式选择 5；执行、查询、循环、进程数和清理仍有边界，不按名称杀进程。首次失败记录原样归档，没有改成成功。

新增预检查脚本首次试运行发现 PowerShell 参数插值后的冒号语法错误（PID 10796，退出 1，未进入脚本执行），改正为显式变量边界并通过 AST 检查后，重新先试 1 项再跑 3 项。失败记录保留。公共 loader 的小输入负例只使用本轮创建的 1 字节伪权重，不进入 encoder forward。

主线程归档 15 份执行记录并核对 **67 个记录的进程身份**，没有匹配的存活进程；教师/性能子任务分别在自己的证据中记录清理。预检查删除准确归属的临时包、链接及目标，先移除 junction 本身，不递归进入目标。测试创建的 GUID 目录已回收；日志、报告、AOT 产物保留为证据，不清空共享缓存或系统临时目录。Windows 快照可能漏过采样间隔内短暂存在的子进程，此限制保留，不声称完整无遗漏的系统级审计。

所有生成循环先核对比较退出条件并使用极小输入试运行；工具、回归和批次均有项目/字节/迭代及墙钟上限，取消/进度支持详见源码。三个子智能体均未提交、不修改共享计划/测试入口；主智能体统一接线、构建、证据及 ROADMAP/CHANGELOG。未使用 Graphify、远端 API 推理、native 数值桥接或未授权发布。

## 仍未关闭的验收

- S4-05 的来源许可、逐条人工审核与独立保管人 seal；现有候选审计仍 blocked，不自动批准或为封存集制造真值。
- S4-08 的真实 calibration 拟合、成对基线/置信区间/逐语言质量报告和模型部署证据；profile 字段只是输入声明，不能仅填字段取得发布授权。
- S4-10/S4-11 的实际 Tomur/模型 revision、接口、输出训练/再分发条款、资源预算和最多 32 条真实采集；离线 hash 格式审计尚未绑定原始响应附件。
- S4-15 的上游数值 oracle、独立 head/encoder 真实部署及默认入口迁移；当前已验收的合同不是真实模型验收。
- S5-06 的完整 CPU 矩阵、稳定尾延迟与独立模型画像；S5-07 的对应数值/质量门槛、实际消融收益与两 RID 回归；S6 发布及业务集成没有授权执行。
