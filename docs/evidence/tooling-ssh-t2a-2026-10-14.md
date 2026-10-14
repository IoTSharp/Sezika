# T2a：C# SSH/SFTP 与 Linux NativeAOT worker

日期：2026-10-14。范围为本机协议、完整代码/runtime 清单、真实 Windows→Ubuntu 回环、原生 worker 执行及进程回收；租用服务器、训练、模型/数据发布均 **NOT_RUN**。T2b 的 Git/LFS、T7 的旧调用者退役分别验收，本文不宣布整个迁移完成。

新增 `Sezika.RemoteTool`（SSH.NET、JIT）、`Sezika.RemoteWorker`（实际 NativeAOT）和独立 SSH 库的 `Sezika.SshFixture`。凭据由标准输入 typed envelope 或明确的 Credential Manager target 读取，不放入 argv/配置/回执。固定主机键、限定 host/root/path、原始链接类型检查、完整 bytes/SHA、no-overwrite 和专属恢复状态；不调用旧 Shell/Paramiko/helper。静态 worker 只接受固定 typed request，执行前后核全部 worker/payload 代码/runtime，再用 C# broker/watchdog 执行原生 argv。

本机完整原件根：`C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014`。源码候选 `ssh-candidate-v4/source` 共79文件；生产 SSH 与 fixture 的23个文件、Tooling 的3个必要 friend assembly 接线单独绑定。Windows 构建、Linux自包含发布零诊断；worker-v5 来自独立 `ssh-candidate-v1/source`，其实际依赖源码与v4相同，fixture helper变化不冒充 worker 重新构建。

| 实际检查 | 结果与原件 |
| --- | --- |
| Windows 合同检查 tiny1/full26 | `ssh-final-checks-v1/windows-remote-checks-{1,26}`，全部通过 |
| Linux 合同检查 tiny1/full26 | `ssh-candidate-v4/linux-checks-{1,26}`，全部通过 |
| Windows SSH/SFTP cases1–8 | `ssh-candidate-v3/win-case-{1..8}`，八项通过；这些 case 的全部实现与v4相同 |
| Linux SSH/SFTP cases1–8 | `ssh-final-checks-v1/linux-case-{1..8}`，内外实际执行通过 |
| Linux 实际 native case9 | `ssh-final-checks-v1/linux-case-9-v2-*`，完整payload192文件、467/512帧、handles0、内外exit0、EOF与cleanup通过 |
| Windows→Linux native tiny1/full2 | `ssh-crossos-worker5-v2/{tiny1,full2}`，两次实际回环均467/512帧、handles0、两端及外层exit0 |
| full2 四项身份拒绝与一个 manifest 拒绝 | 使用当前真实PID、原registration/ownership复制件；缺失注册、错误出生、错误scope、空父链分别命中精确门；原输入SHA不变。manifest突变在联网前拒绝 |
| 独立审查 | `ssh-crossos-independent-v1/audit.{json,md}`，22项已审阅绑定通过，原报告的pending原样保留；主智能体补证单列于下文 |

实际 NativeAOT image 为13,201,736 bytes，SHA256 `01814e1745d752f92d91152678e77930e61e6bb15062b3823f8226dd90d9c409`；dbg为22,869,360 bytes，SHA256 `b1700f991858014df9567eef0024962aeaed6bea062c359c3579947f3fa99fb3`。完整worker2文件清单SHA为 `7db1b509399a8c3c0d60c4bbe0af2a2b7ce20d8d61d5bac21bdfb3fdc9c6baf3`；payload192文件清单SHA为 `6ff6d84d68b5ff73a501aaa026f0ffaae0a0f353d4ac8df76105c998f151b388`。这是固定C# process fixture，不是模型推理。

主智能体独立补证在 `ssh-crossos-root-readback-v1`：full2 worker2/payload192实际Linux落盘文件全部通过 Windows WSL share 新句柄全文读取；另在Linux执行真实 `/usr/bin/sha256sum`，194行逐项严格匹配，合计118,707,945 bytes，包含dbg。原PS字典汇总导致首份 `Bytes=null` 的元数据保留，准确总量写在 `full2-readback-aggregate-v2.json`，不改原始报告。原始 request、worker-result 与执行结果、完整物理source/copy集合以及额外进程审计另见该目录后续补证。

补证已实际完成：`original-six-receipts-readback.json` 对tiny/full各三份Linux原始文件与Windows副本逐字节相等。Linux生产 `bundle` 命令重新完整捕获tiny/full的worker/payload四个物理目录及两个原发布目录，六份清单SHA均与原冻结manifest严格相等；实现逐文件全文读回并拒绝额外文件/链接。原UNC汇总检查的90秒超时保留，后续使用Linux实际生产校验器新请求，未扩大单次30秒/512文件上限。tiny1 worker首次捕获通过；payload首次被旧e6验收启动器的watchdog空argv竞态阻止，原 `UNKNOWN` 保留。新请求使用已验原生执行器后五份剩余清单全部通过，未修改SSH源码或原件。

实际Windows NativeAOT audit 对两个client与两个server outer回执读到12项身份，全部ABSENT、live0/unknown0；Linux NativeAOT audit 对两个server inner与两个native-result读到16项身份，全部ABSENT、live0/unknown0。原REGISTERED、EXITED与当时reaping观察文本仍在原件中，后来的ABSENT不回写历史执行结论。这里报告已审计的28条身份记录，不把原件重复引用扩大为新的进程数。

最终扩展Linux审计 `linux-complete-original-native-audit-v2.json` 还读取两个SSH内层执行器、case9的三份原生回执及上述旧启动器失败回执：10份原件、38条身份记录、20个不同PID均ABSENT、live0/unknown0；原失败仍明确 `OriginalStatus=UNKNOWN`、`CleanupStatus=UNKNOWN`。重复父/子回执中的同一出生身份分别计记录，不宣称38个新进程。审计不终止进程。

原失败保留：候选v1/v2的两次CS0104、跨系统v1的whole-OS-ancestor CLR拒绝/0帧/无native回执，以及case9首次把输出放进深层路径而在任何worker执行前被拒绝。case9-v2使用新浅层专属POSIX根及精确nested broker pins，未扩大512帧预算。所有检查用新的输出路径，失败证据、发布物和符号保留；不以socket关闭或ssh exit单独证明子树退出。
