# S4-08 校准工程续审（2026-10-03）

状态：工程实现及统一构建/合同测试已通过；真实校准与封存质量仍 pending/blocked。

审计发现三个合同缺口：verified profile 接受缺失基线/Score MAE，非有限差值可避开比较；verified manifest 可包含 pending profiles；拒答阈值改变 accuracy/F1/ECE 的样本分母，导致这些指标与全样本 NLL/Brier 无法直接比较。

本轮修复限定在 `src/Sezika/Calibration.cs`、`CalibrationProfiles.cs`、`CalibrationConfidenceIntervals.cs`、`tests/Sezika.Tests/CalibrationContinuationChecks.cs` 和校准文档。没有修改模型/profile 数据，没有运行真实模型或拟合实际 calibration split，没有教师调用、下载或对外发布。

## 实现和针对性检查

- verified 必须有 sealed_test 声明、随机/多数类两个基线差值、NLL/Brier 成对差值；Score 必须有 MAE；可选数值非有限时 fail closed。
- pending/rejected 保留失败观测，verified manifest 要求全部 profiles verified。
- full-split accuracy/F1/ECE 与 selective accuracy/count 分离；Boolean 负类召回及 Score 期望 MAE 有独立类型语义，无分母时可空。
- 评估/拟合有样本、类数、step/work product、总墙钟、取消和进度上限；先以 2 条 × 2 grid steps 的极小 fixture 验证进度；额外 identity 候选、极小正温度和精确零支持有合同测试。
- 测试覆盖空/null 概率、NaN、错误 Boolean 类数、取消、墙钟超时、数值聚合溢出、基线缺失和拒答分母；synthetic passing snapshot 仅测试验收代码，不能作为真实模型通过证据。

## 验证记录

执行环境已核对为 PowerShell 7.6.6，固定入口 `C:\Program Files\PowerShell\7\pwsh.exe`。工具发现只使用 `Get-Command dotnet`，得到 `C:\Program Files\dotnet\dotnet.exe`。没有子任务发起的长命令、后台进程、下载或临时目录；短工具命令已正常退出。统一构建与测试由主线程的有界进程执行器运行，本子任务复核了 result JSON 与 stdout，没有重复并发启动 build。

| 验证 | 命令/范围 | 真实结果 | 进程与证据 |
| --- | --- | --- | --- |
| Release solution build | `dotnet build Sezika.slnx -c Release -v minimal --nologo --disable-build-servers -m:1` | exit 0，0 warnings / 0 errors | PID 66704，2026-10-03 12:11:06 +08:00 启动，result JSON elapsed 116.734 秒，墙钟上限 240 秒；`.artifacts/processes/20261003-041104-669-continuation-solution-build-v2.result.json` 与同名前缀 stdout |
| 核心合同测试 | `dotnet tests/Sezika.Tests/bin/Release/net10.0/Sezika.Tests.dll` | exit 0，139 checks 通过，其中本轮 29 项校准检查全部通过 | PID 66340，2026-10-03 12:13:30 +08:00 启动，result JSON elapsed 5.231 秒，墙钟上限 90 秒；`.artifacts/processes/20261003-041329-341-continuation-core-tests.result.json` 与同名前缀 stdout |
| Patch whitespace | `git diff --check`（本子任务 tracked 源码/文档范围） | exit 0 | 短命令正常退出 |

两份统一验证 result JSON 均有 PID、启动时间、完整参数和父进程记录，`CleanupErrors = []`。进程快照的短暂子进程可观测性限制保留在原始日志，空 cleanup error 不等于证明快照覆盖了每个短暂进程。当前工作目录没有真实模型资产，核心 stdout 明确为 `SKIP: mmBERT tokenizer oracle`；本次 139 checks 不能写成历史含模型资产环境的 350 checks，也不能作为真实模型 oracle、实际校准或新模型 AOT 支持结论。主线程另行开展的 Windows Native AOT fixture 验证如成功，只能证明该工程 fixture 的编译/执行合同，其结果由统一报告归档。

## 剩余限制

这轮没有代表性获准 calibration/sealed_test 数据，不能拟合新 profile 或关闭 S4-08。Evaluator 要求同一 slice 固定类数；Choice 候选语义必须由数据工具先隔离，不能把不相关候选位置池化为分类质量。split 字段是输入声明，不能证明封存；baseline 差值和 artifact 同样需要真实流水线及人工审核。置信区间仅提供统计工具，未绑定为 verified profile 的强制字段。零回答的历史 `SelectiveRisk = 0` 保留兼容性，消费者须检查 CoveredCount/SelectiveAccuracy。语言/领域切片、AUROC、OOD 与高置信错误的完整真实报告、真实模型数值、CPU/CUDA 与两个 RID AOT 回归仍未验收。
