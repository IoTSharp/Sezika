# Sezika engineering rules

- Sezika 是独立的 multilingual, non-autoregressive System 1 decision engine，使用 C# / .NET 10；核心部署目标为 Native AOT。
- 模型、CPU/GPU 算子及调度必须使用 C#。运行时只允许调用系统/显卡驱动 API，如 CUDA Driver；不允许 C++ bridge、ONNX Runtime、LibTorch、cuBLAS/cuDNN、远端 API 或关键词规则替代本项目推理。
- GPU 采用构建期 ILGPU 将 C# kernels 导出为 PTX/ABI，Native AOT runtime 通过静态 C# Driver 绑定执行。禁止在 AOT 运行时依赖 ILGPU 的 Reflection.Emit、IL 读取或动态 launcher；该路径通过原型前不得写成已支持。
- Sezika 核心保持独立，不内置业务动作执行器。
- 代码、模型、tokenizer、数据与校准文件分别管理许可、来源和版本。不得把模型权重嵌入二进制或 NuGet。
- 文档默认中文；README 是项目首页，ROADMAP.md 是唯一阶段计划，CHANGELOG.md 记录已完成历史，docs 保存设计与证据。
- 新能力先记 ROADMAP；草案/代码/数值对齐/真实推理/语言质量/AOT/性能分别表述。禁止伪造模型输出、benchmark 或“已校准”状态。
- probability、distribution concentration 与实际正确率不得混称；拒答和失败必须有明确语义，模型预测不能成为执行授权。
- 使用静态类型、source-generated System.Text.Json、显式类型判别。不得动态加载程序集、运行代码生成，或 blanket suppression 隐藏 trim/AOT 警告。
- 普通类型不重复加产品名前缀；程序集、命名空间和公开命令可以使用 Sezika。
- 默认不执行构建、测试、启动、训练、模型下载或发布；用户明确要求时执行对应范围，记录版本、命令、超时和证据。
- Windows 仅使用 PowerShell 7+，先核对版本。工具发现限 PATH、明确配置与已知路径，禁止广域目录扫描。
- 任何循环、轮询、重试或批处理必须有最大迭代/项目数与墙钟超时、取消及进度；生成循环先核对比较条件并用极小输入试运行。
- 记录任务创建的进程 PID、启动时间、命令行与父链，使用有界等待并回收本任务完整子树；禁止按名称批量杀进程。
- 临时文件仅删除核实过绝对路径与归属的任务对象；保留交付物与用户文件。委派子任务时重复这些边界与清理要求。
- 互联网请求直连失败时以单次调用方式使用 http://127.0.0.1:7890，不更改全局代理。
- 禁止加载、运行、安装或更新 Graphify 及其集成。
