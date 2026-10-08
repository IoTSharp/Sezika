# 福海真实 GPU 决策头实验

2026-10-08 已完成一次真实“训练→保存新权重→加载使用→独立进程验证”闭环。冻结真实原始 mmBERT encoder，纯 C# CUDA encoder 导出 32 条真实文本特征，再用实际教师硬标签训练三行线性决策头。这个实验更新了 2304 个 F32 head 参数，encoder 的 134 个 tensor 保持冻结；不使用 SZDH1 诊断头或 synthetic 特征训练。

## 来源与资源

底座 `jhu-clsp/mmBERT-base@c5955035435e2bf121cde7f3c8863ef52ff35d82`，独立模型 ID `sezika/mmbert-base-independent-s4`，H768、22 层。encoder 1,227,772,549 bytes / SHA256 `6168c84d2498cdacdcafee16b71a69b5c1636ad569bfabaf2855f5ce7dabd528`；tokenizer 17,525,329 bytes / `197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356`。来源许可 MIT 工程核查已保留，发布审签仍独立。

32 条中英 Choice/Score/Boolean 标签来自实际 GPU 教师 `qwen3.5-9b-q4-k-m` / 固定量化 revision `3885219b6810b007914f3a7950a8d1b469d598a5`，原请求/响应 64 个字节实物由监督者核 hash。各语言首两条 Choice、首一条 Score、首一条 Boolean 固定为 development，其余 train；24/8、32 个独立 family，分法在训练前冻结。派生 manifest SHA256 `b8c8d5e018c2bc0029ba100f8f46272ac4cb3b1efea27280ff449f685778b954`、records `a015a8fa182ee62223cafc307c618c515a19e90f843290db9dfb53a56b9a041c`、provenance `4504b0fbfa666be2f5fe62d5f568c5e7739e8d0c0713bdd97b1473bc2c0cb356`。

原数据维持 `human_review=pending`、`teacher_output_training_permitted=false`、答案曝光和非封存；另登记用户授权实验，不制造人工 approved。开发 Boolean 两题都为 true，Score 缺 0 标签，不能用于代表性质量结论。

福海连接经用户指定聊天及其项目 runbook 核实，目标 `digisyn-gpu01` / Ubuntu 24.04.4。RTX 4090 驱动报告 49,140 MiB 总显存、初始可用 15,294 MiB；现有其他服务使用的显存和进程不调整。驱动发行版本 580.173.02，CUDA Driver API 版本 13000，UUID `GPU-e786e313-bc92-a63d-a833-f4e48cbafb66`。实际训练和 fresh validation 的 owned 峰值均 1,309,369,952 bytes；进程峰值分别 2,724,786,176 / 2,724,184,064 bytes，低于 3GiB GPU / 4GiB host 上限。

## 固定参数与实际结果

零初始化三行 head，seed 17，learning rate 0.001，总共 72 次实际提交更新（3×24）；无恢复、无开发集调参循环。32 题渲染后真实 token 长度为 103–920，使用 strict 1024 上限，完整保留题文和候选。特征计算、CE、梯度、SGD、重载推理均为 CUDA，CPU 只作 tokenizer/文件合同/微型数值参考和聚合。

| 完整 GPU CE | 训练前 | 训练后 | 磁盘重载 | 独立进程重载 |
|---|---:|---:|---:|---:|
| train 24/24 | 1.229626456896464 | 1.1749068026741345 | 1.1749068026741345 | 1.1749068026741345 |
| development 8/8 | 1.2687934339046478 | 1.2772859781980515 | 1.2772859781980515 | 1.2772859781980515 |

8 条 development 经加载新 head 的 GPU pipeline 实际推理，两次推理逐条 logits/probabilities/选择一致；教师一致数 2/8（25%）。训练损失下降，开发损失略升；这是未人工审核、已曝光开发题上的教师一致率，不是金标正确率。此结果不支持质量改善或正式可用结论，不扩大训练以选择开发集结果。

训练 feature SHA256 `44DF5374A9A39C1B064A7AC8E34F1B4C13488F274A45DCCB4B837DB1A2EA145F`；最终 2304 个 F32 参数 SHA256 `3C15FFF5DADBBF423DF21BD4DFEB0DAD141A1469CE77C39A85C69187BDFF9B6A`，实际非零，CPU/head 文件/GPU 参数读回一致。报告中的最后一步 loss `1.4564497470855713` 是更新前选中单样本，不替代上表完整 CE。

| 真实磁盘产物 | bytes | SHA256 |
|---|---:|---|
| experiment-head.asset（SZMH1） | 9619 | 2C93C6A9FD28C491AE5718F00C81C5FFB08859FC7F85C6A8F4DF6B209A5D5AAD |
| experiment-training.checkpoint | 9619 | 5C604C9510658373EB2933C2D82FAE0FF0E798D8837C59D3B66E5BE7A63C3528 |
| experiment-training-report.json | 原字节见归档 | C6B065CD0C66AA4544C0AF277C539D82A4E7932C66CD41AAAD37CFC1A0D30EE7 |
| 独立 validate 原 stdout | 原字节见归档 | 1B49EF8859131ADFB582BD380739C7F99D64E5C88F042C1A417B4A2C1B435A1F |

远端保留 `/root/sezika-experiments/20261008-real-head-v1/output/run-03/`。本地对应 `.artifacts/real-gpu-experiment-20261008/retrieved/output/run-03/`，固定底座仍在 `.artifacts/models/mmbert-independent/converted/`。权重不嵌入程序或 NuGet。原报告/独立 stdout 与两个 Linux result 原字节另归档于[实验证据目录](real-gpu-head-experiment-20261008/)。

## 执行与失败边界

工具 v3 冻结 SHA256 `D8A3826B4AC68125628A680C07EE5786F260384C560DE9E6E58A968505EA7CA9`，独立审查精确重建 v2，确认仅必要长度/前置变更。最终 publish PID84132、8.546 秒、无警告/错误；输入合同 tiny1/full8 与真实 tokenizer lengths1/32 通过。196 文件 bundle 实际远端 SHA 全匹配，v3 只改变工具 DLL/PDB；静态 17 PTX/ABI 与核心/CUDA DLL 保持。

真实 train Linux `train-03` 24,400ms（含清理）；fresh process `validate-03` 24,090ms。这两个进程分别重新装载 encoder、数据和 GPU context；validate 从磁盘读取 report/checkpoint/head，并重新 GPU 导出特征、完整 CE 和八题推理。Windows SSH 包装 PID19992 / 91460 分别实际 Succeeded0，28.939 / 28.114 秒；Linux complete/exit0/最终完整，live/unknown/unchecked/cleanup/budget 均零。时长只作执行预算证据，不是正式性能验收。

原失败全部保留：首次构建四行括号语法错误后修复；Windows 错用 Linux self-contained runtime 后改用本机 managed runtime；首真实 `train-01` 因 token256 在首 encoder forward 前失败，0更新；旧 SSH 嵌套包装漏返回码造成 Windows0，补显式返回后用实际 remote exit7 验真。`train-02` 在 runner 早期 setup 返回、没有 identity/result/payload 文件及训练执行，原具体 guard 原因未知；独立有限前缀探测随后 exit0，未倒填原因。`train-03` 使用同一训练参数和已测1024上限完成唯一成功实验，没有把这些失败算成训练完成。

训练/fresh validation 均实际 context/model Dispose 返回，bytes/allocations/modules/release failures 归零。独立 Linux 后核六个 runner 的 22 条身份记录（含重复）全 absent；Windows 37 回执、126 记录、135 PID 无匹配存活/unknown。两个 compiler TEMP 的空子目录精确删除，workload 日志按 hash 保留；专属 check 空目录和 setup-only probe 已清理，交付物保留。历史短 reader native-child 登记缺口不扩成全机零残留声明。

PowerShell 固定 7，所有长命令使用有界 runner；Linux工作480秒/清理8秒/外层492+3秒，Windows530秒，SGD300秒。SSH 脚本经技能 stdin wrapper 传送，显式退出码传播；不安装全局 runtime，不停止既有服务，不 push/publish/deploy。所有正式准入、人类质量、许可发布、校准、sealed test、新 head Native AOT、性能和 publication flags 保持 false。其他 ROADMAP 任务继续暂缓。
