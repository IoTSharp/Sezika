# Windows 独立模型 Native AOT 诊断验证

2026-10-08，工作区 `D:\source\Sezika`。S4-15/S4-08 的这一工程切片已通过：Windows Native AOT 中使用真实固定原始 mmBERT encoder、生产独立 loader/pipeline/runtime 和固定内存诊断 head，完成一次真实 CUDA 类型化推理、预取消及释放检查。head 未训练；本结果不关闭上游数值、正式训练、语言质量、校准、性能或正式模型两 RID 验收。

## 实现和身份

新增 `samples/Sezika.IndependentModel.Smoke` 的六文件样例，静态引用 C# core/CUDA，加入 `Sezika.slnx`。没有改变产品 CLI 默认路径、依赖原 Laya head、使用 CPU fallback 或引入动态 launcher。JSON 使用静态 DTO/source generation，AOT/trim 警告没有被抑制。

实现窗口为 UTC 2026-10-07 21:30:49–21:50:49；作者在截止前冻结源码并释放 GPU 窗口。构建时基线为 `01d23f3`，六个新增样例源文件另以清单冻结。监督者独立核对六文件 path/bytes/SHA 与清单一致，并重核 exe 和真实报告 SHA。

| 对象 | 身份 |
| --- | --- |
| 原始 encoder | `jhu-clsp/mmBERT-base@c5955035435e2bf121cde7f3c8863ef52ff35d82` |
| 原始 PyTorch 权重 SHA-256 | `8EA64EC1EA4EB8FCA0FC14B69A2AE571DE6BFBC25FD214BB932DD4ABA6A3A04E` |
| 转换 encoder SHA-256 | `6168C84D2498CDACDCAFEE16B71A69B5C1636AD569BFABAF2855F5CE7DABD528` |
| tokenizer SHA-256 | `197D4CC5406EE12CC50C8B5511F2393CC32D9DB321545979CE041C1199178356` |
| 六文件 source-freeze SHA-256 | `8F8D7683DD20408C75D69ADD697B0E20E979231C479D7989ED82A1B589F5BBD9` |
| native exe | 5,258,752 bytes；`ED19B03C00C3BAA9E75966019899FFEAFA1F57E51E0BE384A16050831759BA4C` |
| 真实 CUDA 报告 SHA-256 | `A529790C5003AF58E68ABFE714CD5C8727868F42A9C26B59E946596BC7E7C680` |

固定实现 capture 来自 `.artifacts/supervision-20261008/capture-original-46-v1/capture.json`；request SHA 为 `75B7055B38C78E7563D1D14DE9BC5F4EE9D63A7FF31E637B8C77F3EBF41D9917`。生产 tokenizer 重新构造后逐项匹配 46 tokens 和 markers `[25,35]`，不是用 fixture 代替真实输入。原权重、config、source lock、输入持 read leases 到执行结束，原权重实际 hash 已核验。

诊断 head 为 3×768 F32、9216 bytes、seed 0；算法 `((i*17+11)%101-50)/10000f`，i=0..2303。实际 `CopyWeights` payload 的 little-endian SHA-256 为 `CB9E10A535EB0EC2F801F78AEC54842176D3E9CA0D9BB6F7DD0EA9DB91A9C991`；算法摘要为 `384FBC41D7D5E58D67C5AE779AF56F1A8ACAF816101522C61897296A5132515B`。容器 manifest/feature/file hash token 仅表示诊断 descriptor，不能称作训练文件或正式准入声明。原初始化数组清除后，自检仍核 head 内实际权重摘要。

程序只记录 source-freeze 附件 bytes/hash，不解析清单，也不自行证明源码到 binary 的关联；`source_freeze_contents_validated_by_program=false`。六样例源码关联由监督者清单核验及唯一 publish 回执建立，不能把这份六文件清单称为全部传递依赖的冻结。

## 验证与真实范围

PowerShell 7.6.6，Windows .NET SDK 10.0.401。构建关闭 build servers/shared compilation/node reuse，单节点执行；外部命令由 `tools/Invoke-BoundedProcess.ps1` 记录 PID、启动时间、命令、父链、timeout、stdout/stderr 和清理。没有重跑历史完整回归或真实 CPU forward。

| 验证 | PID | 实际结果 |
| --- | --- | --- |
| 首次 build | 4556 | CS1061：SelectedIndex 误写为 Score，失败日志保留 |
| 三次修复构建 | 37180 / 90824 / 63632 | 成功，最终零警告/错误 |
| managed tiny1 / full8 | 30336 / 29692 | PASS；均为合同 fixture，无 forward |
| 六文件 freeze | 94968 | 成功，监督者再以 tiny1→6 文件核验 |
| 唯一 win-x64 Native AOT publish | 104028 | 成功，runner 23.706 秒，无 warning |
| native tiny1 / full8 | 21380 / 91364 | PASS，要求真实 AOT 标志 |
| native CUDA driver probe | 63016 | 两元素 `[4,6]` PASS，owned 归零 |
| 唯一真实 native CUDA case | 43932 | PASS；runner 8.897 秒，内部 5.7823164 秒 |

真实报告的 `DynamicCodeSupported=false`、`DynamicCodeCompiled=false`、TPA-null=true。实际 backend 为 `cuda-independent-linear-marker-head`；RTX 4070 Laptop、8 GiB、CUDA Driver 13020、SM 8.9、UUID `134BF256A4B04FD4E3073C3D34F79CB4`。返回真实有限 Choice 分布 `[0.4992040700482931,0.5007959299517069]`、selected index 1、uncalibrated；数值不具有训练收益或正确率含义。

启用 CUDA profiling 后实际记录 311 次 kernel launch、H2D 1,227,767,992 bytes、D2H 552 bytes。预取消新增 launch 为 0，前后 owned 1,227,767,808 bytes、135 allocations、34 modules 完全相同。active peak owned 1,229,919,136 bytes，小于 3 GiB。runtime/model 释放后 owned bytes/allocations/modules/release failures/workspace bytes/active workspaces 均为 0，model/device disposed=true，cleanup errors 为空。

运行只允许一个 46-token case，内部 100 秒、runner 120 秒；probe 内部 20 秒、runner 30 秒。4 GiB host/3 GiB CUDA 仅为观测闸门，不能称硬内存隔离。profiling 含逐 kernel 同步，上述时间不能称 benchmark、尾延迟或优化收益。

失败输出保留独立 audit；报告 CreateNew/Flush/Move 不覆盖，Move 后取消回滚按实际 file ID/hash。零字节 partial、取消或 peak 已超限时的恢复使用独立最多 5 秒预算；leases finally 最多 16 项/5 秒。Windows 文件身份、错误 JSON、诊断 head 与原子发布均有 8 项 tiny/full 合同检查。

## 监督者资源终审与接续

UTC21:55 的独立终审先 tiny1，再完整 12 份明确 runner 结果、45 个去重登记及 launcher 身份。11 份 Succeeded、首次 build Failed 均符合保留记录；全部 cleanup errors 为空。先精确 Get-Process PID/start 筛选，只有匹配才允许核 command/父链，没有匹配存活身份、termination 或专属目录顶层 `.tmp-*`。两次终审 runner PID35604/30112 成功；终审脚本内部最多 30 秒/12结果/780身份/64目录项，实际约 5 秒。

证据根 `.artifacts/independent-aot-20261008/head-checkpoint/` 保存六文件 freeze、唯一发布物、probe/真实报告、12 份日志回执及作者 `implementation-evidence.md`（SHA `7FE772A3908684A604FEF3540D4F7311D7C4BEA160722C73DF8A522703BA182E`）。监督者证据在 `root-processes/source-freeze-audit.json`、`windows-process-closure-1.json`、`windows-process-closure-12.json`。只核登记的 Windows 身份及任务目录；原始进程快照可能漏过极短中间进程，不虚构全机器历史观测。没有独占 MCP 服务，GPU 窗口已释放。

Linux 仅完成固定 Ubuntu 工具发现，未 publish/run 或核 CUDA Driver；将另开有界窗口复用这一样例。原始上游 oracle 的 NumPy 安装许可仍 pending。正式 trained head、人工数据/封存、真实上游 parity、质量/校准、训练 AOT 和性能分别继续验收；本切片仅 Windows 诊断工程闭环，Sezika 不 push/发布/部署。
