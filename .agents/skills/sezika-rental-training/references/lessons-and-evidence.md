# 故障和证据纪律

完整历史失败、reader修正、模型路径和当时结果见 [原始说明](../../../../docs/history/tooling-skills-20261014/sezika-rental-training/references/lessons-and-evidence.md)。原日期、任务、输入/模型SHA及成功值只作定位，不作为新任务默认值或预期输出。

- 参数使用结构化argv；Unicode、空白和literal shell文本保持原参数，不拼接远端命令或解释器。
- TAR只去掉准确的./前缀，拒路径逃逸、链接、设备、重复与遗漏；原失败解包目录保留，同一可信归档另选新目录验证。
- 题文/结果按唯一record ID核完整集合及内容；原行序与train→dev报告顺序不同不自动判错。训练seed/sample-visits顺序仍单独验。
- 使用带offset的真实时间；不将已经解析的日期再次按本地文本Parse，也不沿用过期租期。
- PID必须绑定出生时间/ticks、boot、完整argv和父链；被无关进程复用时不杀新进程。历史未知不因后来退出而倒填成功。
- 修改工具、schema、reader、PTX/ABI、权重或实际输入字节生成新候选/SHA，并重新验证受影响门槛；原FAIL、UNKNOWN、partial、日志和模型不覆盖。

失败停止依赖阶段，先安全保存和准确任务资源回收。公开证据移除认证、敏感连接/argv、原始/派生训练记录和详细逐题输出；原件留在ignored任务根。完整模型是外部资产，不进入技能、程序集、NuGet或代码提交。

报告区分配置、代码、本机simulation、协议、实际目标/GPU、数值、语言质量、校准、AOT、性能与发布可见性；缺证据保持NOT_RUN。退出0与HTTP健康不构成真实业务或模型验收。
