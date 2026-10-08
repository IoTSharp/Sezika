# Stage B / C 独立审查（2026-10-08）

结论：接受本次唯一的真实 GPU B/C 执行、保存、磁盘重新加载与独立进程复核证据。Stage C 与 Stage B 的完整 `observations` 子树逐项序列化相等，未发现阻止记录本次实验结果的工程问题。本结论不构成模型质量、校准、Native AOT、训练扩展性或性能验收；不建议自动追加训练。

## 审查范围与边界

- 本审查只读取监督智能体提供的固定实验材料，未编辑源代码、正式文档、模型、数据或实验输出，未使用 Git、GPU 或网络，未重跑训练和推理。
- 约束为最多 24 个唯一证据输入、20 分钟、10 个短批次且每批次不超过 45 秒；本报告生成前读取了 23 个唯一证据输入、8 个工具批次。无需扩大范围。
- Windows 命令使用 `C:\Program Files\PowerShell\7\pwsh.exe`，实际版本 7.6.6。本审查没有生成循环、长驻进程、下载、临时目录或临时文件；本报告是交付物。
- 首次完整 B 数值审计使用了 PowerShell 保留变量 `$Error`，因只读变量错误而中止。随后改成任务专属变量重新执行只读审计并通过。该错误没有触发 GPU 重跑，也没有修改实验材料。
- 源冻结及最终执行脚本已在 `final-code-review.md` 独立审查。源冻结 SHA-256 为 `26A4C4A051F249E3BC9A89082D915E605DBA4E15C54079023B3F10127785DBAB`；脚本清单 SHA-256 为 `46430C7F3C74F4FDD10BC15416C5644CC46545B36F9963F7FDD88C8D46F388FA`。

## 原始证据

B 本地副本位于 `.artifacts/gpu-learnability-20261008/stage-b/`：`experiment-head.asset`、`experiment-training.checkpoint`、`experiment-training-report.json`、`experiment-learnability-evaluation.json`、`result.tsv`。

B wrapper 原始日志与 Windows 回执分别为 `remote-b-logs/20261008-092459-118-learnability-B.stdout.log` 和同前缀 `.result.json`；独立 B closure 为 `closure-b-logs/20261008-092631-033-independent-stage-b-identities.stdout.log`。

C 原始材料位于 `.artifacts/gpu-learnability-20261008/retrieved/`：`output/stage-c-evaluation.json`、`output/run-768/` 的四项 B 输出、`.artifacts/stage-c/` 的结果/身份/监督进程/日志文件，以及 `logs/independent-final-closure.tsv`。

C Windows 回执为 `remote-c-logs/20261008-092726-259-learnability-C.result.json`。最终 Linux closure wrapper 原始 stdout 为 `remote-closure-logs/20261008-092904-129-learnability-closure.stdout.log`。

以上路径均相对于 `D:\source\Sezika\.artifacts\gpu-learnability-20261008`；前述已带 `.artifacts/gpu-learnability-20261008/` 前缀的路径相对于仓库根目录。

## 已独立核对的产物身份

| 产物 | SHA-256 |
| --- | --- |
| B head | `7B4CE09F22AD80E175030F2A73FC878974347D53259DC16D3727B92E7F3184A4` |
| B checkpoint | `972D6CADDF5743CDA7EAD4CA0F3D657D572DF15B8710A36ADC659B892547EB92` |
| B v1 training report | `74CE86F566DE7357A0643FFA476AF1D3F651B1FBE83B0BAA8A73A9919456BCAA` |
| B evaluation | `8CFC1257FA44DFCA62CF119324F46C2083A70D24517EE3FFA4C0863F1CAAF43A` |
| C evaluation | `11C7CEC6399D33E1C9A85A97DF37E80C7D9A7168073B3EFA9A7CA3484E4A0EEA` |

`retrieved/output/run-768/` 中四项 B 产物的实际哈希与上述 Stage B 副本一致。head、checkpoint、training report、GPU CE 的最终参数身份一致：`C09F73C98139BFF1D7F429343F08CD5EFC0FDAF28492B07802940B6C3A0C9D8D`。

head/checkpoint 尾部 checksum 之前的最后 9,216 字节独立解码为 2,304 个 F32 参数，全部非零。零初始化张量独立重算哈希为 `2D07A41AE992770085117E9815300BFD0730745883E60B24AAAD5E69DFC087AE`，与 GPU baseline 身份一致。独立解码的 head 元数据为 seed 17、学习率 0.001、768 步、最后所选样本更新前 loss `0.8507111668586731`、参数数 2,304。

监督智能体提供的 archive SHA/大小未作为本次独立审查输入，故此处不声称已复核 archive 本身。

## Stage B：真实训练与数值一致性

B 是本次唯一一次零初始化 head 的 768 次更新，seed 17、学习率 0.001、strict 1024，encoder 冻结，实际执行 48 次 encoder forward。没有以 Stage A 的 72 步旧 head 继续训练。

40 行输出分为 train 24、dev 8、swap 8。逐行与原始 `experiment-records.jsonl` 核对了 split、语言、题型、目标 ID/索引映射、候选文本哈希。独立重算 token 和 marker 哈希；8 个 swap 的 token 与 feature 哈希均发生变化，且原始行链接正确。

独立根据 logits 重算概率、排名和 CE，最大概率误差为 `5.55111512312578E-17`，GPU CE 与主机 NLL 最大误差为 `1.26198303140868E-07`。

| 样本范围 | agreement | 平均 CE |
| --- | --- | --- |
| 零 head baseline train | 不在本表推断 | `1.229626456896464` |
| 零 head baseline dev | 不在本表推断 | `1.2687934339046478` |
| 768 步 final train | 17/24 | `0.9920696603755156` |
| 768 步 final dev | 1/8 | `1.2740284875035286` |
| final swapped Choice | 4/8 | `1.3999227583408356` |

原始 train Choice 为 6/8，平均 CE `1.1979709044098854`；Score 为 6/8，Boolean 为 5/8。按语言统计原始 train 为 zh 8/12、en 9/12。dev 仅 zh Score 1 行成功：Choice 0/4、Boolean 0/2、Score 1/2。

B 的原始 Choice 预测覆盖位置 1/2/3，不能沿用 Stage A 的“全部预测 index 0”结论。交换候选位置后，8 行中 6 行所选 ID 改变，agreement 从原始 6/8 降至 4/8，位置敏感性仍然存在。

训练集 CE 降低、agreement 升至 17/24，说明本固定实验存在可学习信号；训练集尚未拟合完成，dev agreement 1/8 且 CE 比 baseline 略高，不支持泛化或语言质量通过的结论。probability、分布集中度与实际正确率仍须分别表述。

## Stage C：独立进程从磁盘重载

B payload PID/start tick 为 `758287/483768626`；C 为 `763061/483783365`，证实为不同的实际进程。C 完整参数包括 `validate --diagnostics fixed-left1`，绑定上述 B training report 与 evaluation 哈希。

C 状态为 `fresh_process_disk_loaded`，`FreshDiskOutputsMatched=true`、failed=0，重新构建 encoder 并实际执行 original 32 + pipeline dev 8 + swap 8 = 48 次 forward；日志无 SGD 更新进度。

比较先从第一行进行最小检查，再检查全部 40 行，最后检查整个 `observations` 子树。B/C 的全部行以及完整子树是精确序列化相等，不只是满足容差：覆盖 CE、feature/device parameter 身份、tokens、markers、文本哈希、logits、probabilities、ranks、IDs、groups、position comparisons 和 development pipeline。C 原始 stdout 的完整 JSON 主体与磁盘 C report 相等，仅有末尾换行差异；没有用最后一行摘取代替完整日志。

这证实本次 head/checkpoint 的保存与磁盘加载在独立进程中保持一致。该 CLI 实验未包含 Native AOT 发布运行，不能据此声称 AOT 已验收。

## 退出、资源与独立清理

B Linux wrapper 为 completed、payload_exit=0，总耗时 26,150 ms，cleanup 3,720 ms；C 为 completed、payload_exit=0，总耗时 26,370 ms，cleanup 3,720 ms。两阶段 cleanup/final/live/unknown/unchecked/budget 字段符合约定，fixture 为 none。

B Windows receipt 为 Succeeded、exit 0、PID 57308、ElapsedSeconds 31.108、timeout 530 秒、cleanup 空数组。C Windows receipt 为 Succeeded、exit 0、PID 53104、实际 ElapsedSeconds **32.69**、timeout 530 秒、cleanup 空数组。C CLI 时间为 21.1358814 秒。

B/C 的 model/device Dispose 均为 true；退出时 owned bytes、allocations、modules、release failures 均为 0。两阶段 GPU peak 均为 1,309,369,952 字节；B host peak 2,724,798,464 字节，C host peak 2,726,555,648 字节。实际 CUDA Driver API UUID 为 `E786E313BC92A63DA833F4E48CBAFB66`，对应 `GPU-e786e313-bc92-a63d-a833-f4e48cbafb66`，driver version 13000。

独立 B closure 的 13 个已登记身份行均为 absent。最终 closure 的 18 个已登记身份行均为 absent，summary 为 known=18、live=0、unknown=0、files=11；两份 closure 中 GPU free 均为 15,294 MiB。最终 wrapper 输出亦确认旧 v1 training report checksum 为 OK。

closure 的范围是 `registered_task_identities_only_no_signal`，不是全机进程扫描；短命中间进程的观测范围限制仍然保留。最终 closure stdout 中仍存在的 llama-server、python 和 Tomur 属于其他工作，审查未触发任何信号或清理。B/C 各自 Windows 回执已独立核对；整项任务最终 Windows closure 属于监督智能体的最终化范围，本报告不替代该回执。

## 可接受的记录与不可扩大的结论

可以记录：唯一 768 步 GPU head 训练完成；四项输出身份一致；实际 CE/概率数值复核通过；C 在独立进程中从磁盘加载并保持全部观测一致；已登记 Linux 任务身份清理通过，GPU headroom 回到 15,294 MiB。

必须继续明确：train 17/24 尚未充分拟合，Choice 位置敏感性 6/8，dev 1/8 且 CE 略有恶化。不得标为语言质量、校准、AOT、性能或完整训练能力已通过，也不得将预测作为业务执行授权。没有批准或执行任何追加训练、发布、推送或部署。
