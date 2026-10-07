# Ubuntu WSL2 独立模型 Native AOT 诊断验证

2026-10-08，S4-15/S4-08 的 Linux 诊断切片已通过：复用 Windows 样例的冻结源码，在 Ubuntu WSL2 `linux-x64` Native AOT 中完成固定原始 mmBERT encoder 与未训练内存 head 的唯一 46-token CUDA 推理、预取消和资源回收。它与 [Windows 验证](independent-aot-smoke-2026-10-08.md)共同覆盖这两个诊断发布物；不关闭正式 trained-head、上游 oracle、训练 AOT、代表性质量、校准或性能验收。

## 构建和实际身份

源码基线为 `b837be7`。六个样例源文件未改，source-freeze SHA 为 `8F8D7683DD20408C75D69ADD697B0E20E979231C479D7989ED82A1B589F5BBD9`；root 已独立核对。该清单仅覆盖六个样例文件，程序只记录附件 bytes/hash，不解析其内容或证明全部传递依赖。Linux 编译结束后才释放 core/CUDA 源码冻结窗口；后续训练器修改不能自动继承这份旧 ELF 的验收。

Ubuntu `/usr/bin/dotnet` 10.0.112，已核 GNU gcc 13.3.0、clang 18.1.3、ld 2.42；Windows PowerShell 7.6.6。唯一 publish 使用专属 `build-artifacts` 和 `publish-linux-x64-v1`，禁用 build servers/shared compilation/node reuse、单节点。RestoreSources 指向确为空的任务 local 目录，清 RestoreAdditionalProjectSources、禁 NuGet audit，只复用现有 cache；三个项目 restore 各 334 ms，无安装、下载依赖、远端 restore 或新的权重下载。

| 对象 | 实际记录 |
| --- | --- |
| ELF | 5,084,552 bytes，64-bit little-endian x86_64 |
| ELF SHA-256 | `F355C79A2B98402F670617E46BA00DE4BF8226BB69F090BE4658326C0FC828F4` |
| 真实报告 SHA-256 | `6D36644B29FE67AFE20BCD8C677C20006C67B8B4880E49DE54AE910FF220A7A8` |
| probe SHA-256 | `DA32987AE4F8DFD8F006B33B1748D9C2A759019C6D2228A4148407045D71DF45` |
| Bash 执行边界 SHA-256 | `AC979A61F98FBA71F5D7181FA904B008642FF60A269E947E64844C9FBBD3F255` |
| PowerShell 入口 SHA-256 | `E030FBA561FF3C700EB33666E2EAAB89BB0F58B55895838CE3950FBEADFD1ED0` |

唯一 publish 的 Windows PID27004，runner 43.589 秒、Linux 41 秒，exit0；实际编译输出无 warning/IL warning。Native self-check 先 tiny1，再 full8，均 exit0 且 `--require-aot` 通过，实际覆盖 Linux x64 `fstat` file-ID、零字节 partial、CreateNew/Move 后取消回滚与清理。它们是合同 fixture，未 forward。随后执行一次 driver probe 和一次真实模型 case；每步分别核 Windows 与 Linux 回执、cleanup/survivors，而非只用 Windows launcher 退出判定。

真实模型 Windows launcher PID54268、Linux PID92698，runner 38.662 秒、样例内部 32.8292387 秒。报告实际 `DynamicCodeSupported=false`、`DynamicCodeCompiled=false`、TPA-null=true，进程 executable SHA 与上述 ELF 相同；backend 为 `cuda-independent-linear-marker-head`，缺 CUDA 不 fallback。RTX 4070 Laptop、Driver13020、SM8.9、UUID `134BF256A4B04FD4E3073C3D34F79CB4`。

原始 revision、encoder/tokenizer/request/token/marker 身份及 3×768 F32 内存诊断 head 均与 Windows 相同，head memory SHA 为 `CB9E10A535EB0EC2F801F78AEC54842176D3E9CA0D9BB6F7DD0EA9DB91A9C991`。重新 tokenize 逐项匹配 46 tokens、markers `[25,35]`。实际 Choice 分布 `[0.4992040700482931,0.5007959299517069]`、selected index1、uncalibrated；数值不是正确率、正式训练收益或完整网络数值 parity。

实际 profiling 记录 311 次 kernel launch、H2D 1,227,767,992 bytes、D2H 552 bytes；552 是唯一序列的 46×3 F32 logits readback，不是 46 个 case。预取消新增 launch0、owned bytes1,227,767,808/135 allocations/34 modules 前后相同。peak owned1,229,919,136 bytes；释放后 owned bytes/allocations/modules/release failures/workspace/active workspaces 全0，model/device disposed=true、cleanup errors=[]。profiling 含逐 kernel 同步，时间不作 benchmark 或裸机 Linux 性能结论。

## 执行边界、失败与资源

Windows `Invoke-BoundedProcess` 仅监督 wsl.exe；Linux 自身使用实际 ready 握手、继承 task token、PID/starttick/UTC/argv/PPID/父链/session/group journal、有限轮询及幂等 trap。进程 inventory≤4096项/2秒、owned≤1024、父链≤32层/2秒、运行≤4×budget+16轮及budget秒、250ms backoff，清理≤6pass/8秒。只对归属 token 和原 starttick 再验证通过的逐 PID 发信号，成功、失败、取消、超时都后核已记录身份，不按名称或盲目 kill group。

准备阶段先 normal/timeout/cancel tiny。补 supervisor 身份与 unknown depth 后，root 新一轮发现合法退出竞态：环境读取失败被误标 cleanup_incomplete；旧 timeout/cancel 虽 survivors0，仍不能通过。修复后对同身份区分 live、absent/reused/zombie、unknown；仍存在但不可读不误称退出。最终 fix-v2 三项均 cleanup_incomplete0/survivors0；正常 exit0、超时124、取消125分别保留，不能统称所有 Windows 回执 Succeeded。

两份冻结执行源码已按实际 bytes/hash保存到 [Bash 脚本](independent-aot-linux-2026-10-08/bounded-linux.sh)及 [PowerShell 入口](independent-aot-linux-2026-10-08/Invoke-LinuxBoundary.ps1)。这是本次证据源码，执行目录固定为 `.artifacts/independent-aot-20261008/linux-smoke-v1`；脚本使用新 run 名及 CreateNew，原失败与成功日志不覆盖。

协作型 payload 必须保留 token、不 daemonize/double-fork/清空环境。Bash/proc 不提供 pidfd/cgroup 原子性，不能保证恶意逃逸、SIGKILL supervisor 或校验到 kill syscall 间竞态下的通用隔离；原快照也可能漏极短 helper。4 GiB host/3 GiB CUDA 是观测闸门，in-flight native 调用依赖外层期限。正式范围不从这些诊断限制推断。

模型执行窗口为 UTC22:16:08–22:32:08，约22:28主动收口；最多一次 publish、各一次 tiny/full、一次 probe、唯一模型 case及两次资源终核。源码/数据/资产身份与原始日志保存于 `.artifacts/independent-aot-20261008/linux-smoke-v1/`。精确 Linux 后核26个已记录身份全部 absent、matching live/zombie/unknown均0；作者的21个Windows身份记录/18个PID亦不存在，随后终核自身的root26864/launcher50408已退出。root 在UTC22:31独立核六个明确执行回执，22个PID/start记录（20个PID，含launcher及root字段）无匹配存活，记录`root-processes/linux-execution-windows-closure.json`。root 已重新核 ELF/真实报告 SHA、原始 publish/self-check 输出及原始资源回执，没有重跑模型。

作者最终 `linux-execution-evidence-v1.md` SHA 为 `D4265A494D105CCD1F52EA49BA811E2D1D66024513BBCAA59E0B67D4FCC28B56`，来源/binary 绑定 JSON SHA 为 `4DD5C2A0D3DDD7D9248086D90533A1AF5541E142C40137B2A6F801AE3B5A5A55`。所有预算内交付、首次失败、Linux日志和资源记录均保留；没有遗留任务服务或需要清理的任务临时对象，自检目录已经 finally 精确删除。

全部 head_file/training/formal_training/training_data/feature_cache/quality/parity/calibration/benchmark/license_approval 标志均 false。CPU仅 tiny合同与单case诊断准备，未实际 encoder CPU forward。GPU窗口已释放并转交单独训练器清理加固；正式人工数据/封存、NumPy安装许可及真实上游参考仍为独立依赖。Sezika仅 commit，不 push/发布/部署；30分钟监督继续。
