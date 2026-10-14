# C# SSH/SFTP v3 回环测试服务器

仅用于 T2 的可重复协议检查，不是部署的服务器。固定官方 Microsoft.DevTunnels.Ssh 3.12.42（Microsoft、MIT，源码 commit `e65278de0bf73b7730d82a9469714677f5fc496a`），NuGet 原始 metadata 另保存在本次 dependency 回执。

每次启动一个 case：`Sezika.SshFixture --output <新的专属目录> --case <1..8>`。1 为 tiny 真实 upload，2 完整 download /拒覆盖，3 远端 partial upload 恢复，4 本地 partial download 恢复，5 错误 host key 在认证前拒绝，6 链接文件/祖先拒绝，7 错误完整 SHA 与断线，8 真实 SSH 公钥认证与完整下载。必须先实际跑1，再串行各 case。

case9 是另有预算的 Linux native worker 集成：`--case 9 --worker-directory <已冻结LinuxRemoteWorker-NativeAOT发布目录> --payload-directory <已冻结LinuxProcessFixture发布目录>`，输出用新的浅层专属 POSIX 根，例如 `/tmp/sezika-ssh9-<id>`。先复制并独立核完整发布工具清单，worker/payload 的目录 manifest SHA 明确进入 config 和 request。控制器通过 SFTP 独立完整读回核固定 NativeAOT worker image，worker 再执行前后核 payload 全部代码/runtime 文件。完整真实 RemoteDispatch 通过 SFTP 交付 typed request、SSH 执行固定 NativeAOT C# worker、SFTP完整绑定回执读回并核全部 payload PID 已回收。只有此 case 才接受严格固定的 `worker --request <portable path> --sha256 <64hex>` exec；最多512 SFTP frames、60秒、8 connections、每目录256条、128个创建过的句柄（同时仍限32）。仅实际固定 native worker image 的 SFTP 读上限等于其实际长度且必须<=32MiB；其他文件仍限1MiB。单次启动会报告实际 native image bytes。本机授权测试不运行训练/模型/业务程序。

固定 127.0.0.1 随机端口、60 秒、最多8个连接、256个 SFTP 请求、每文件1 MiB、32个同时文件/目录句柄与64个创建过的句柄。服务端的 v3 INIT、OPEN/READ/WRITE/CLOSE、RAW LSTAT/READDIR、REALPATH、普通 no-overwrite RENAME、SETSTAT 与 SYMLINK 由 C# 实现；密码/公钥认证和密码学 SSH2 transport 使用独立官方测试库，客户端使用真实 SSH.NET。backend只映射任务内 `server-files` 到一个 `/fixture` 根。

host/client 临时 RSA key 仅在内存，client私钥只送当前进程里的真实 SSH.NET auth；不写私钥、系统 SSH 配置或用户 authorized_keys。连接、SFTP 请求、认证与剩余句柄实际计数，关闭 socket/channel/stream 后出新回执。小型原始字节和失败证据是交付物，保留在准确 task 根。cases1..8不启动进程；case9的固定 C# native fixture进程使用已登记 broker/watchdog与完整树回收。没有模型计算，也不安装 system sshd、Docker image 或服务。

失败诊断仅保留最多8条服务端异常的 SFTP 操作号、测试路径、异常类型和最多512字符消息；普通不存在状态不计入。客户端失败堆栈写当前 fixture 的 stderr，认证凭据和私钥不输出。

Windows 上的 SFTP link fixture 用独立 virtual link metadata 而非假冒操作系统符号链接：真实 wire 的 LSTAT/READDIR 报告 S_IFLNK，REALPATH/STAT 解析该指向，使客户端祖先拒绝与 canonical-path 行为可以交叉验证。它只表示该协议边界，不能替代目标 OS 真实 link 验收。

实际服务器、GPU/训练和发布是独立验收门槛；八项 SSH/SFTP fixture 不升级 native-worker 生命周期，case9 与跨系统实际回执分别描述其范围。尚未实际跑的 case 保持 **NOT_RUN**。

跨系统检查用 `crossos-server --request <typed-json> --output <new-report>` 和 `crossos-client --request <typed-json> --output <new-report> --maximum-checks <1|2>`：Windows 真实 SSH.NET 经回环连接 Ubuntu fixture，执行固定 Linux NativeAOT worker。两端请求必须给出当前 `ProcessEvidenceDirectory`，绑定执行器实际 `registered.json`、原始 ownership marker、环境 scope、原生自身出生身份、父链与 argv；不枚举整个操作系统的祖先。full2 先以当前真实 PID 检查缺 registration、错误 birth、错误 scope、空 parent chain 四个拒绝门，再做实际 roundtrip 和联网前 manifest 单项篡改。tiny1 明确不运行这四个负例。

服务端最长120秒，客户端60秒，SFTP仍限512帧；精确 session STOP 文件停止服务。完整 worker/payload、请求、原始执行回执与下载副本须独立读回；Windows/Linux 外层执行回执及最终原生进程审计另外验收。2026-10-14 的证据见[本机与跨系统检查](../../docs/evidence/tooling-ssh-t2a-2026-10-14.md)。
