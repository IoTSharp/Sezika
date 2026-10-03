# 独立 decision-v1 runtime facade 证据

本轮为 S4-15/S4-12 增加 `IndependentDecisionV1Runtime`。它接收同一独立 encoder package 和已经由训练报告绑定的 `IndependentMarkerHeadAsset`，在本地构造严格 `decision-v1` 序列、运行 C# encoder、提取每个显式 mask marker 的完整 hidden row，并以三行线性 head 计算有限 softmax 分布。返回值包含模型身份、题型、候选 ID 原顺序、概率、选择位置和 `uncalibrated` 状态；分布集中度不表示正确率，也不授予业务执行权限。

`Load` 要求调用方提供 `MarkerFeatureIdentity`、feature hash 与 head file hash。模型加载或 head 身份校验失败时释放已加载 encoder；运行期间共享取消令牌和最多 5 分钟 deadline。runtime 只拥有独立资产，不调用 legacy Laya loader。

同时，`IndependentDecisionV1Parser` 对缺失字段和错误 JSON 类型统一返回结构化 `DecisionException`，而保留重复字段、未知字段、题型和候选范围的专用错误码。

## 定向验证

| 命令 | 结果 |
| --- | --- |
| `dotnet build tools/Sezika.IndependentModelTool/Sezika.IndependentModelTool.csproj -c Release --no-restore` | 0 warning / 0 error |
| `dotnet run --project tests/Sezika.Tests/Sezika.Tests.csproj -c Release --no-restore` | 148 PASS |

构建和测试均通过 `tools/Invoke-BoundedProcess.ps1`，各自设置 60/90 秒超时、CIM 查询超时 5 秒；最新结果分别保存在 `.artifacts/independent-runtime-build6` 和 `.artifacts/independent-runtime-tests5`。本轮创建的 PID 及其完整子树均已退出，`CleanupErrors=[]`。

这项 facade 尚未证明独立上游逐层数值 oracle、真实质量/校准、CUDA 或 Native AOT；这些门槛仍按 S4-08/S4-15 状态保留。
