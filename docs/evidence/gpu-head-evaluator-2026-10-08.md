# GPU 决策头完整交叉熵观测

本项增加只读 `IndependentCudaMarkerHeadEvaluator`：使用已有静态 C# PTX/Driver 的 Linear 和交叉熵 kernel，对所有输入题目逐条计算 CE。它没有 SGD 或参数更新；训练由独立 trainer 执行。支持 1–32 条明确冻结的特征，完整分母、GPU 上传前后参数 F32 字节/hash、调用者 context 归属、三 GiB owned 上限、300 秒工作和独立五秒清理。

独立源码审查发现的调用预算 600→300 修复已纳入实验工具；evaluator 本身两源冻结后保持。源码 SHA256：

| 文件 | SHA256 |
|---|---|
| IndependentCudaMarkerHeadEvaluator.cs | 3AAC93968DBD87F00A6FCB1BC8E55B2532A8FF9D54369C3623B84F265F46055E |
| IndependentCudaMarkerHeadEvaluationChecks.cs | FEAD6E7530A47045ED591723E17C5BA61A54EC2FD2DC46A248A9A68D7382D9D1 |

监督者在 PowerShell 7.6.6 / .NET SDK 10.0.401 上发布 self-contained linux-x64 / runtime 10.0.12。实际 Sezika.Cuda.dll SHA256 `65C53949E7092560CF1D0014F6DEBE60F5A118EF666539C01E6CECE33B3D28A5`；后续工具 v3 只改变工具 DLL/PDB，这个 CUDA DLL 保持原字节。

福海 `digisyn-gpu01`、Ubuntu 24.04.4、RTX 4090、UUID `GPU-e786e313-bc92-a63d-a833-f4e48cbafb66` 实际运行 tiny 1 项和完整 3 项：零 head 三题型 CE、非零 head 对三个微型 CPU 数值参考、首次真实 CE 后取消/资源回收。最大绝对误差分别 `1.904654323148236E-09`、`8.323756972572482E-08`。两个进程实际 Dispose 返回，owned bytes/allocations/modules/release failures 均零；峰值 owned 796 bytes。它们明确是 synthetic 数值检查，不是模型训练或质量验收。

Linux 两 runner 各 `completed / payload_exit=0 / final_check_complete=1`，live/unknown/unchecked/cleanup incomplete/cleanup budget exhausted 均零。监督者随后核包括它们在内的六个 runner 的 22 条身份记录（含重复记录），全部 absent；Windows 37 份回执、126 条进程记录、135 个 PID 无匹配存活/unknown。资源终核仅覆盖已登记身份，历史短 reader 的 native-child 记录缺口保留，不宣称任意进程沙箱或全机零残留。

原始证据保留于 `.artifacts/real-gpu-experiment-20261008/`：`evaluator-source-freeze.json`、`independent-training-review.md`、`remote-numeric-logs/20261008-074128-632-numeric-tiny-full.result.json`、`retrieved/.artifacts/numeric-{1,3}/`、`retrieved/final-closure.tsv`、`root-local-resource-closure.json`（SHA256 `06C3AB62BA4F003148C05340123F452BD9F9AE690938E46F7DDB381F9CF7322F`）。真实实验训练另见[训练闭环](real-gpu-head-experiment-2026-10-08.md)。
