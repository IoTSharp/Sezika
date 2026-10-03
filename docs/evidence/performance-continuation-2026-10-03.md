# S5-06 / S5-07：画像证据校验续审计（2026-10-03）

本轮推进可执行的证据校验工具；没有运行模型、训练、CUDA kernel、AOT 发布或性能消融。S5-06 的 CPU 完整矩阵仍未验收；S5-07 对应模型质量、数值、部署及真实成本收益门槛保持 blocked。此前历史测量身份和小样本限制不变。

审计发现旧 `Validate-S5Profile.ps1` 会把字符串样本转换为 double、只核对行数而不检查声明矩阵的唯一覆盖，且没有重算覆盖率、吞吐和 min/max。旧汇总还可能在选择 instrumented E2E 阶段后从空 encoder 字段输出 `stage_ms=null`。当前工具要求 `full` 的独立 encoder/head 完成测量，明确保留 instrumented E2E 和 encoder/head 两种观察字段；E2E-only 行保持 stage 未测，不能以 fallback 混淆口径。

新增校验包括 JSON 数值类型与范围、完整代码身份/模型 hash、输入 JSON hash、唯一矩阵、正式样本数和分配样本、min/max/nearest-rank 分位数及吞吐重算、请求/问题覆盖分母，以及 discovery 实际 token/hash/marker 和 instrumented forward 完成数。合法 rejection 只能是 runtime token 预算或 token 长度错误，不能携带正式样本/吞吐。汇总保留采样设置、模型/代码/输入身份、detail、预算和环境供后续人工比较；不生成优化、质量或 AOT 结论。

在 PowerShell 解析之前，严格 UTF-8 的 byte snapshot 先经 `JsonDocument` 遍历，逐对象用 Ordinal 比较转义解码后的成员名并拒绝重复字段，含所有嵌套数组/对象；最多 64 层、131072 节点并受剩余墙钟与取消约束，文档在 `finally` 释放。preset、候选数、forward、actual token/type/marker 和 token 总数均拒绝字符串冒充数字。

验证环境为 Windows，PowerShell **7.6.6**。不构建 C# 或争用 CPU/GPU 推理预算。先单报告小输入验证循环退出条件，再执行有界回归；全部循环使用比较条件、明确最大行数/样本数/负例数和总墙钟检查，PowerShell 取消与进度可用。

最终针对性回归结果：**35 项通过**，包括一份真实 smoke、四份真实报告批次，以及 **33 项明确篡改的非法 fixture 被按预期拒绝**。四份源报告均来自 `docs/evidence/s346-continuation-2026-09-26/`：

- `profile-simd-short1.json`
- `profile-simd-long1.json`
- `profile-cuda-short1.json`
- `profile-cuda-long32.json`

它们是既有真实测量，未重采集，也未汇池样本。负例覆盖字符串/Boolean/负数样本、重复维度/行、错矩阵、覆盖分母、吞吐、min/max、输入/code hash、非 Boolean AOT 标记、不完整 discovery/instrumented、actual token hash、token 总数、错误 detail、缺分项、资源失败、错误状态、rejection 残留正式样本，以及字符串 preset/candidates/forward、根字段重复、转义 status 重复、嵌套转义 questions 重复。非法 fixture 只用于校验器回归，不是模型输出或性能证据。

执行命令（项目根目录；重复运行须另选未存在的输出名）：

```powershell
& ./tools/Invoke-BoundedProcess.ps1 -FilePath 'C:\Program Files\PowerShell\7\pwsh.exe' -ArgumentList @(
  '-NoProfile', '-File', 'tools/Sezika.Benchmarks/ValidateProfileChecks.ps1',
  '-OutputPath', '.artifacts/performance-continuation-20261003-checks-duplicates.json', '-TimeoutSeconds', '30'
) -TimeoutSeconds 40 -LogName 'performance-validator-duplicates'
```

runner 结果保存于 `.artifacts/processes/20261003-041428-887-performance-validator-duplicates.result.json`，身份/标准输出/标准错误分别同前缀。PID **61420**，父 PID **46068**，启动时间 **2026-10-03T04:14:30.2983547Z**（北京时间 12:14:30）；结果为 `Succeeded`、退出码 0、runner 墙钟 **8.14 秒**，内部回归墙钟 **4.3557004 秒**。runner 保存完整命令行、创建时间及子进程链，`CleanupErrors=[]`；结束后的 PID 检查没有发现该进程存活。回归没有启动子命令，自己的 GUID 临时目录在 `finally` 删除，输出 `temporary_directory_removed=true`。结果与进程日志保留作任务证据，没有清理用户文件、共享缓存、模型或交付物。

最终执行身份 SHA-256：

| 文件 | SHA-256 |
| --- | --- |
| `tools/Validate-S5Profile.ps1` | `fe7b6baf5565c130569be97dec9db2490d792b82a45fc92146c3f258c6a8028c` |
| `tools/Sezika.Benchmarks/ValidateProfileChecks.ps1` | `e76970e0e6afc2550eefac29210cc9894492e029b61cec59b59491c46e0c1a4a` |
| `.artifacts/performance-continuation-20261003-checks-duplicates.json` | `03fb5cebf17b5a9ac784fa47da9f6ea26b9a8650a49716a0ec0a8ecb5e206e0e` |

没有改核心算子、推理语义或权重，没有声称优化完成。后续 S5-07 实际实现仍必须先取得对应模型数值和质量门槛，在固定输入/预算下逐项比较真实成本，并完成取消/卸载与两 RID AOT 回归。本次报告自述身份检查不能证明历史程序集仍在原路径，也不能证明 `native_aot=true` 的报告已经独立验收。
