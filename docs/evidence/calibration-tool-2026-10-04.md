# S4-08 校准工具证据（2026-10-04）

`Sezika.CalibrationTool` 只处理已经物化的概率 JSONL。它要求完整记录和两个 split 的 SHA-256、固定的 `calibration`/`sealed_test` 行数、冻结的 `abstain_below` 拒答阈值，以及模型、tokenizer 和 prompt schema 身份；温度只在 calibration split 拟合，sealed test 同时输出校准前后指标。manifest 中若提供 `manifest_sha256`，工具会先核对它与文件实际摘要，再进入评估。超时或取消不会提交完整报告，退出码为 124；已存在的输出文件也不会被覆盖。

fixture-only manifest 的 gate 固定返回 `not_eligible`，即使合成数字满足阈值也不能宣称真实模型质量或校准；该状态和 `failed` 都返回退出码 3。`accepted_for_quality` 仅表示输入已通过外部准入状态，不能替代真实模型、数据许可、人工复核、封存和质量门槛。

## 定向验证

| 命令 | 结果 |
| --- | --- |
| `dotnet run --project tests/Sezika.Tests/Sezika.Tests.csproj -c Release --no-restore` | 148 PASS（含 fixture gate、split/hash 失败路径） |
| `dotnet run --project tests/Sezika.CalibrationTool/Sezika.CalibrationTool.csproj -c Release --no-restore -- self-test` | PASS：重复 JSON 属性在反序列化前拒绝 |
| `dotnet build Sezika.slnx -c Release --no-restore` | 0 warning / 0 error |
| `dotnet publish tools/Sezika.CalibrationTool/Sezika.CalibrationTool.csproj -c Release -r win-x64 -p:PublishAot=true --self-contained true` | Native AOT 成功，无编译警告 |
| 发布后的 `Sezika.CalibrationTool.exe self-test` | PASS |

上述进程均通过 `tools/Invoke-BoundedProcess.ps1`，设置了明确墙钟上限并记录 PID、argv、父链、结果和清理状态；结果保存在 `.artifacts/processes/`，发布目录为 `.artifacts/aot/calibration-tool-win-x64`。本证据不包含真实独立模型权重、真实教师调用、人工质量准入或 sealed test 封存，因此不关闭真实质量/校准门槛。
