# GPU head 训练器异常清理工程验收

本次只验收 S4-07/S4-12 的异常清理工程。固定合成 marker features 在真实 CUDA Driver/RTX 4070 上执行；没有正式数据训练、真实 encoder 前向、教师调用、PTX 重导出或 Native AOT publish。正式训练、批量教师与特征计算仍使用 GPU。

## 修复与边界

`IndependentCudaMarkerHeadTrainer` 的最外层 ownership finally 覆盖 initial 创建后的 order、集合和 GPU 准备；packed 的复制、取消检查、Allocate 与 H2D 全在独立清零 finally 中。只清自建临时副本，返回权重和输入 resume 保持其所有权。

buffer、复合 kernels owner 及部分 kernel 构造失败共用一个独立 5 秒 recovery clock；最多 39 buffers、40 顶层 owner、17 modules。每次 native 调用前及最后一次返回后检查时钟；native 调用无法被该时钟抢占，仍依赖外层有界进程。超限、超期、首个 managed 清理异常、release failure 增量及纯 managed 归属摘要均有明确语义。

主训练异常以原对象、类型和原始抛出帧保留。没有主异常但释放未闭合时拒绝返回成功 state。CUresult 非零导致 owner 保留；SafeHandle closed 不代表 module 卸载成功。trainer 不销毁 caller device，清理以调用前资源为基线；caller context 销毁结果与历史 release 错误分开记录。通用 CudaKernels.Dispose 及其部分构造失败也使用上述预算与诊断。

故障接缝只对 Diagnostics.Tests 内部可见，生产默认 null，单 scope、有限阶段、非成功 CUresult 单次消费，Train finally 自动复位。以下 MemFree/ModuleUnload/期限是显式注入，不能称驱动自然故障或可抢占 native hang。没有实际 native binding throw 或 OOM 实测。

独立审查另发现既有 Allocate/LoadModule 的 native 成功后 wrapper 创建/集合登记 OOM 缺口：managed counters 可能看不到未登记的 native 残留。本提交不修复该缺口；后续独立分配登记工程另行验收，不能把本次正常 counters 恢复当作全部早期 OOM 覆盖。

## 最终冻结与验证

源码 UTC 2026-10-07 22:40:07 冻结，七文件身份在 `.artifacts/trainer-cleanup-20261008/source-freeze.json`；监督者终核七项均匹配。主要源码身份：

- trainer：`A3DB0C81D340D87B52184BD83541B98F05523D2DE4ED4B96CDD7FF46A87020BE`
- recovery：`0722FBEF52C7A7CF4133F2A8A7F2A7746484F8544A9D620B7CD1066CD20DACC7`
- cleanup checks：`53EB0B15F99862BF138FC43F20976BEF335BFC37A765ADF8AEB90BECD8FDCE01`

Windows PowerShell 7.6.6、.NET SDK 10.0.401。三次 build 均零警告/错误；最后一次 PID 93232、runner 实际 12.137 秒，原始结果为准。禁用 build servers/shared compilation/node reuse、`-m:1`，独立 artifacts 目录。

最终 DLL：

| 文件 | SHA-256 |
| --- | --- |
| Sezika.Cuda.Diagnostics.Tests.dll | `6FAD84AE036294402107857A65E4BACE32D52B042EDF8F283524D56D9F52B07F` |
| Sezika.Cuda.dll | `5A678656789A6AE519C7FD9EBD75A7EADEDCA8E29C9EF8E8357D59BA74340C57` |

先极小后完整：旧构建 CPU 1/3、GPU tiny1；最终构建 CPU 1/3、GPU tiny1/full8，另一次既有 `--independent-training-tiny` 21 检查。累计 CPU fault contracts 8 case、新 GPU 清理 10 case、build 3 次，均在原阶段数量界内。没有重跑 61/31/59 全套。

最终 runner：

| 入口 | PID | 实际 runner 秒 | 范围 |
| --- | --- | --- | --- |
| final-cpu-tiny | 14368 | 3.603 | 1 个非法 fault 参数 |
| final-cpu-full | 61140 | 3.191 | 3 个非法 fault 参数 |
| final-gpu-tiny | 93452 | 2.569 | caller 4 bytes/1 buffer/1 module 基线 |
| final-gpu-full | 105688 | 3.534 | 其余 8 个 fault/取消 case |
| existing-training-smoke | 68988 | 3.883 | 21 项真实 GPU 小训练/恢复/取消回归 |

上表耗时只描述有界诊断执行，不是 benchmark。实际 UUID 为 `134BF256A4B04FD4E3073C3D34F79CB4`，profiling=true；所有清理报告 `aot_verified=false`、`data_mode=synthetic_fixture`。

| 实卡 case | 关键实际结果 |
| --- | --- |
| initial progress sentinel | 原对象/原 ThrowProgress 帧保留，initial/packed 清零；caller 原资源及内容仍可用 |
| first step sentinel / cancellation | 各 4 launches/8-byte D2H；原错误/取消保留，归属回到基线 |
| packed before allocation | 已复制 packed 清零，17 modules 释放 |
| initial before order | 使用真实 warmup 得到的 resume；副本清零、输入不变 |
| injected MemFree | 保留 8 bytes/1 buffer、failure +1，其余 owner 继续释放；context 后归属零，历史错误仍在 |
| injected ModuleUnload, no primary | 已完成 2 steps/8 launches/52-byte D2H，保留 1 module/failure +1；明确 training_cleanup_incomplete，context 销毁成功 |
| injected recovery deadline | 1 owner 后停止，真实残留 648 bytes/7 buffers/17 modules；caller 最终 context 回收 |
| partial kernel constructor | 第 3 module 登记后抛原 sentinel，实际 3 modules 回收 |

完整原始结果、stdout/stderr、PID/start/argv/父链均在 `.artifacts/trainer-cleanup-20261008/processes/`。全部 11 份结果 Succeeded/exit0、CleanupErrors 空。caller context 销毁后，9 个最终 case 的 bytes/buffers/modules 全零。

## 独立复核与资源

子审发现原测试仅检查栈含 Train，无法捕获 `throw primary` 的回归；最终版本新增原 ThrowProgress/TrainingCleanupFaults.Reach 帧断言，第三 build 和最终实卡已覆盖。监督者补读最终 recovery/Trainer/测试变化，核冻结源码与 DLL。

独立审查原窗口 UTC 22:34:59–22:40:59；最后保存工具返回 22:41:24，超出 25 秒，不能称该审查按期完成。其已停止，无 build/GPU/服务/tmp 或 tracked/Git 修改；原记录 `.artifacts/trainer-cleanup-20261008/independent-review.md` 保留。该静态记录不代替最终运行证据。

监督者极小/完整 receipt audit 使用明确 1/20 结果、128 身份、30 秒内部/45 秒外部界。完整 audit UTC 22:43:32 核 11 结果/41 去重身份，无匹配 start-time 存活；证据 `root-processes/receipt-audit-full.json`。其 tiny/full PID 77476/64988 随后均不存在。只查明确任务身份，没有按进程名终止、广域工具扫描或清空 TEMP。

作者 GPU 窗口 UTC 22:43:01 已释放。任务运行成功/失败/取消均经过 bounded runner；新 scripts、logs、报告是保留的证据交付物，不作为临时对象删除。后续 Native AOT 训练及正式数据、语言质量、校准和性能仍开放。

作者 UTC22:49:41 最终执行报告为 `.artifacts/trainer-cleanup-20261008/execution-evidence.json`，SHA `B93547A866A46E09742F95E24256746530C6EB28D51A7BBF992DD2660CB99AE8`。复用监督者上述11/41身份终核；8个明确compiler argv rsp及其3个MSBuildTemp目录全部已不存在，无TEMP枚举或删除。工程提交`2800cbe`，Git stage/commit PID100132/77492及root审计PID均已精确核退出。后续修改须使用新的源码/构建身份，不能沿用本批DLL验收。
