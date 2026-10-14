# Oracle 批次调度

`Sezika.Tooling oracle batches --request <OracleBatchRequest.json> --output <新回执.json> --timeout-seconds <秒数>` 按冻结 plan 的顺序执行参考、实际捕获、比较，并分别保存原生进程回执和输出 SHA。当前实现的最终跨平台验收尚在进行；整体进度以根 ROADMAP 和分项证据为准。

新的参考路径调用 `Sezika.ReferenceTool laya`，要求原始五资产、完整源码和运行包、实际编译及执行证据，标记 `port-derived`、`upstream_executed=false`。历史参考采用单独的冻结 index，不重新运行旧脚本。实际路径调用 `Sezika.OracleCapture --backend cuda`，需要另有 GPU 执行授权；本次编码验收仅运行显式内部 simulation，未启动 GPU。

请求固定模型、plan/contract、全部程序源码和运行文件的 bytes/SHA；Linux 还须固定完整 C# broker 包。每轮最多256个批次、每批最多46条，总期限60..1740秒；总预算不足时停止后续阶段，保留未处理分母。既有输出不能覆盖。恢复只接受相同任务 fingerprint、完整三阶段回执及未改变的输入、输出、程序和源码，不重复执行已完成批次。

比较器退出1只代表已验明的数值差异；超时、取消、输入输出不完整、清理失败或证据不符立即中止后续批次。`references.json` 和 `actuals.json` 只收录已完整结束的批次，评分另行执行。调度成功、simulation 或部分覆盖均不表示模型质量、校准、AOT 推理或完整数据验收。

内部 `SimulateAsync` 只供测试，所有回执和捕获均标记 `SIMULATION_ONLY`；公共 CLI 没有切换成假执行器的参数。原始模型、历史失败与原生登记文件保持原字节。
