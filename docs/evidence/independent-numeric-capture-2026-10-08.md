# 独立 encoder CPU 数值捕获验收（2026-10-08）

本次完成开发期捕获工具、失败审计与固定46-token真实CPU输入捕获。没有独立上游oracle，不能据此关闭数值parity、语言质量、校准、性能或发布门槛。用户要求正式训练与批量工作使用GPU；本次单case CPU用于数值核验。

## 实现和修复

工具接入 `Sezika.slnx`，复用真实 `ModernBertEncoder.Trace`，保留完整请求、四段序列输入、token/marker、输入资产、执行程序身份和F32张量。输出只发布新目录；267项checkpoint、有限值、顺序、shape和最终hash全部满足后才发布。原子Move后取消、不可逆内存peak超限仍能保留不完整审计；五个只读输入lease覆盖hash与实际forward，Windows/Unix边界分别声明。

首个tiny自检PID100584退出124：encoder的3秒内层deadline被频繁OS内存查询耗尽。失败输出保留，没有扩充预算。监督者将OS resident采样限制为每100ms至多一次，取消/deadline仍每次检查，synthetic超限传感值仍每次检查；下次采样的PeakWorkingSet保留此前峰值。该采样不是持续资源隔离或实际peak测量。

## 执行结果

PowerShell7.6.6，.NET SDK10.0.401；全部通过 `Invoke-BoundedProcess.ps1`，关闭构建服务器、单构建节点、真实捕获 `DOTNET_PROCESSOR_COUNT=4`。没有子进程、网络、模型下载或训练。

| 过程 | 根PID | 结果与runner墙钟 |
| --- | ---: | --- |
| 修复前工具build | 28180 | 0警告/错误，15.527秒 |
| OS采样修复build | 53844 | 0警告/错误，6.333秒 |
| 极小self-test | 98736 | 1/1，2.818秒；内部20/外部40秒 |
| 完整self-test | 39948 | 8/8，2.481秒；内部20/外部40秒 |
| 真实46-token capture | 85808 | exit0，12.600秒；内部120/外部150秒 |

自检覆盖真实tiny非零trace、NaN/Inf、名称/shape/重复、缺checkpoint、目标冲突、metadata后取消、实际Move后取消回退及持续合成peak失败恢复。负向partial均如实保留；没有分配4GiB。

## 真实身份与产物

固定 `jhu-clsp/mmBERT-base@c5955035435e2bf121cde7f3c8863ef52ff35d82`；实际converted encoder SHA-256为 `6168c84d2498cdacdcafee16b71a69b5c1636ad569bfabaf2855f5ce7dabd528`。原始权重SHA是来源声明，不是此工具重新读取原始pickle的结果。输入来源、转换许可边界见原始资产恢复证据。

- 工程case `independent-smoke-46-v1`：46 tokens、marker位置25/35，token hash `BAB3A41CDB62FC4D5E3353D3D88F6BBDCBD0D3CEC9C3DB537841F09604D2D80B`。
- 267个F32 checkpoint，payload **51,720,192 bytes**；监督者在30秒/267项范围内重新核每份长度/hash，0.607秒通过。
- 最终hidden SHA `1D94C9DA34D7FEC59A714A55ADA65001FE18F0B480B87C3542C32762B5644325`，与既有真实smoke相同；marker vectors SHA `B42B6E93A5A1233AA8E4AD3511F092FFD46220D4DF51381A1CCCA6942705D6F1`。
- 捕获DLL SHA `17C4FC9D16A8B75FEB188803DD8CB8091CB964A8E9BD8E6409B5B85F19081ADB`；执行executable和companion DLL分别记录。
- 输出 `.artifacts/supervision-20261008/capture-original-46-v1/capture.json`，tensor-000至266；独立复核 `capture-verification.json`。
- 自检输出 `capture-self-test-sampled-1`、`capture-self-test-sampled-8`，原始过程日志在同任务 `processes/`。

所有runner结果无cleanup错误；提交前按记录创建时间、命令和父链复核任务身份。没有按名称杀进程或删除用户文件；partial与日志作为证据保留。子智能体最后静态检查超过原10分钟任务边界的事实保留，不将其表述为时间验收通过。

后续需固定上游实现、环境、张量及数值容差并独立比较；本捕获只证明C#实际输出完整和可追溯。
