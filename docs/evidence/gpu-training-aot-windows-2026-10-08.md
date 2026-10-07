# Windows GPU head 训练 Native AOT 证据

2026-10-08。作者窗口 UTC 23:18:04–23:43:04，实际于 23:39:11 收口。测试工程基线 `c386d29`；五个测试源码/项目文件变更，核心 trainer、workflow、report、Driver 和 17 个 PTX 未改。此项证明合成 H4 head 的 GPU 更新与磁盘恢复在 Windows Native AOT 中实际运行，不证明正式数据训练、真实 encoder 反传、语言质量、校准或性能。

## 固定范围与源码版本

固定 Choice/Score/Boolean 各一训练样例，候选 2/3/2、28 个 feature floats；另一个不同 ID 的 Boolean 开发样例。H4、12 个 FP32 参数、seed17、学习率0.05、最多6累计步。首次和连续训练实际传 resume=null，冻结核心源码的 `new float[checked(3 * hiddenSize)]` 给出零起点；这项是源契约，未冒充零权重 GPU readback。没有调用 CPU seed trainer 或 CPU encoder，开发评分保持实际 `scalar-linear-marker-head-fp64` 诊断 backend。

CPU 准备 tiny1/full3 及一次 managed 原生 gate 拒绝，共五 case；managed GPU tiny/full 共16更新。独立审查发现 cleanup case/slice 与 adoption slice 最后返回后的预算检查缺口，作者在原五文件范围补 token/墙钟检查，用第二 build 验证。首版 freeze 和 DLL 保留，旧 managed 结果绑定首版；没有重复 managed GPU 更新。新 native tiny/full 共16更新覆盖修复版。

- 初始 freeze UTC23:25:00：`source-freeze-initial.json` SHA-256 `A1715C5B6C3CDA124192A3FF7A049ED61A6C57326D2F81C5CF9F6C47E381C4CF`。
- 最终 freeze UTC23:30:16：`source-freeze.json` SHA-256 `CD3946F0C5C8313DAA9150E0E23BF0D7E22A3D24DA1F555B4F1F78FB8CA6CCBF`。
- root 完整审查新测试与 Program 入口；独立审查另外两测试和项目文件，旧版发现如实保留。root 补读已知修复 diff，五最终源码与核心 trainer 共六 hash 匹配。

## 实际 Windows 发布与运行

PowerShell7.6.6、SDK10.0.401；MSVC为固定VS18路径，cl文件版本19.51.36260.0。两次 managed build 零警告/错误；唯一 win-x64 publish 使用独立 obj/output、PublishAot、单 MSBuild 节点、ILC并发2、AOT/trim警告视为错误，50.428秒通过，没有 suppression。硬时限452秒，预留原生执行与收口预算。

原生 PE 6,612,480 bytes、CLI header RVA=0，SHA-256 `4CC4A3AF01BBB4CF200A7DFFF632CDC9C3C0DA54876D473F8DC9DF747B2D4021`。真实运行 `IsDynamicCodeSupported=false`、`IsDynamicCodeCompiled=false`、TPA=null，运行时报告的 ProcessSHA 与 PE 一致。原生 gate 在任何 CUDA Open 前；managed 用原生参数实际 exit1 且没有创建输出目录，不作为未处理失败。静态 imports/publish inventory 已记录；imports 清单不是完整 runtime 模块清单。

实际设备 RTX4070 Laptop GPU，UUID `134BF256A4B04FD4E3073C3D34F79CB4`、Driver13020、compute8.9；执行身份绑定17项静态PTX与manifest/source hashes。tiny 一更新实测4launch、80-byte H2D、56-byte D2H，owned buffer峰700bytes。

full 主 case 的三段训练均从真实 report/checkpoint/head 生产 loaders 保存/恢复，不用内存 state 冒充磁盘输入：

| 调用 | 初始/完成步 | 实际更新 | launches | H2D bytes | D2H bytes | buffer峰bytes |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| first | 0→3 | 3 | 12 | 160 | 72 | 780 |
| resumed | 3→6 | 3 | 12 | 160 | 72 | 780 |
| continuous | 0→6 | 6 | 24 | 160 | 96 | 780 |

主对照合计12更新、48launch、480-byte H2D/240-byte D2H。三步后三个 row 均非零；恢复与连续的12参数bytes、LastLoss、checkpoint/head SHA相同，首三输入文件前后 hash 不变。三个报告分别记录，elapsed/resume 不要求相同。FeatureSHA `5CDE6110E325070F1CBED9932BA9CF6F69A9812040AC5573A446B068CB55434C`。这些时间/计数用于身份与执行核验，不是 benchmark 或质量收益。

full 共11 case：上述主对照、预取消、六cleanup与三adoption。预取消零训练/上传/读回/发布；cleanup实际更新为0/1/0/2/0/0，另有caller vector一次非训练launch，测量包含该探针。false-release历史错误和context成功回收分别记录；adoption三例在真实buffer/module/context成功后注入预建异常，零更新。正常profiling event路径实际运行；没有声称AOT中覆盖event故障、release诊断OOM全部历史场景、真实heap OOM或native hang。

11 case的context实际销毁；有字段的终bytes/buffers/modules均0，adoption终events为0，主workflow另断言events归零。不可抢占Driver调用只在返回后检查预算，外部runner负责进程时限。

## 独立复核与资源

最终作者证据 `.artifacts/independent-training-aot-20261008/windows-v1/execution-evidence.json`，142,864 bytes，SHA-256 `C68F8F7F857BA0B7D5741E208D46B20DFB391B0256E04E01923187D7A2D48A05`。包含两freeze、四二进制、17PTX、每环境完整case、九保存物和tiny三文件及真实来源绑定。

root 四次有界workflow原字节审计核managed/native tiny各三产物、full各九产物，全部匹配；PE及其他三DLL原字节hash再核通过。native tiny/full原始日志共1/11个case的运行时flags与context实测一致。root最初误以taskRoot为runner基址导致一次只读路径失败，纠正为`windows-v1/processes`后通过，没有新增GPU调用。复核见同目录`root-*-identity.json`、`root-native-*-cases.json`及root-processes回执。

作者十runner（一个有意gate exit1），cleanup为空；51登记身份精确后核无匹配存活。十个compiler argv中的确切rsp及父目录均已不存在，没有TEMP枚举/删除。root UTC23:42:32另核十作者结果与六复验runner、共63身份，无匹配存活；最后两root审计PID26920/52220后核不存在。GPU已释放，源编辑停止；没有新教师、下载、服务或独占MCP，不清理用户文件与共享cache。Windows快照可能遗漏极短中间进程，保持此限制。

Windows合成head训练AOT切片已闭环；Linux须用最终冻结源码新发布ELF和独立资源窗口验收，正式trained-head/正式数据/上游数值/质量/校准/性能/发布门槛仍开放。
