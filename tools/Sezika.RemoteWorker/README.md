# 固定 NativeAOT C# worker

正式 NativeAOT publish 仅从 SDK 发布清单排除明确的 `Sezika.Tooling.pdb` 与 `Sezika.Tooling.runtimeconfig.json` 引用元数据；Tooling 代码已编入 native image，native symbols 保留。不修改原始候选，也不放宽一份 image 和对应 symbols 的包准入条件。

此项目只链接既有 worker、typed DTO、路径与完整 bundle 校验逻辑，复用 `Sezika.Tooling` 的原生 broker/watchdog，不依赖 SSH.NET 控制器。必须使用实际 Linux NativeAOT 发布 image；JIT 启动被拒绝。控制器经 SFTP 完整核该 image 的 SHA 后，才允许执行固定的 `worker --request <绝对JSON路径> --sha256 <可信SHA>`。

运行前后核 worker 与 payload 的精确全目录文件集、bytes/SHA 和固定 manifest SHA；自包含 JIT payload 允许独立使用完整冻结运行时。原执行回执和完整 worker 回执绑定请求/清单/实际进程结果。构建、NativeAOT、真实 SSH 调度和资源回收仍分别验收，没有实际回执的项目保持 NOT_RUN。
