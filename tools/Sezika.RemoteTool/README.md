# 固定主机键的 SSH/SFTP 与原生 worker

只读包清单生产入口：`Sezika.RemoteTool bundle --directory <已冻结本机目录> --remote-directory <明确POSIX目录> --entry <文件名> --runtime <native-aot|self-contained-dotnet> --output <新JSON>`。30秒、最多512文件/512MiB/深度8；逐文件完整读取两遍核 bytes/SHA，生成的 manifest 必须在冻结目录之外且拒绝覆盖。该命令只准备本机清单，不连接、上传或执行远端程序。

私钥认证按 SSH.NET 的真实 `IPrivateKeySource` 算法顺序优先标准 RSA-SHA2-512/256，再保留原有兼容算法；服务器仍由独立 SSH 库执行实际签名校验与精确临时公钥匹配。SFTP buffer 固定64KiB，以容纳完整 native image 读回；协议 fixture 的请求/并发/墙钟预算不增加。

这是单独的 C#/.NET 10 **JIT** 工具。固定 SSH.NET 2026.0.0（MIT），官方源码身份为 `sshnet/SSH.NET` 的 `7b2fd3dbf2c86a80a7b06cea020aa5f821c9902e`；库与运行时版本不冒充 Native AOT 验收。

连接配置必须明确 host、port、username、SHA256 主机键指纹、POSIX 根、NativeAOT worker 路径及完整字节 SHA、允许的 payload 路径/SHA，以及 worker/payload 完整目录 manifest 与 manifest SHA。第一次接触也拒绝未知键。认证只读明确的 Credential Manager target 或 10 秒/24 MiB 的标准输入 typed envelope；不读 SSH agent、默认身份文件或环境密码，不把密码/私钥写入配置、argv 或回执。私钥输入 byte buffer 与 payload stdin 回收时清零；SSH.NET 与 .NET 的字符串/密码内部副本不声称全部内存已擦除。

SFTP 每级原父目录通过有界 read-dir 的原始类型核验，避免 canonical-path 先解析 symlink 的问题。拒绝链接祖先、FIFO、socket、device、范围逃逸及已有最终目标。目录最多 4,096 项，深度 64；文件最多 64 GiB。部分文件与专属状态绑定 host/port/key/path/bytes/SHA/mtime，完整本机下载与远端上传各做完整新句柄 SHA 读回，成功才按 no-overwrite rename 完成。失败保留部分对象，成功只清理准确匹配的状态文件。

SSH exec 的唯一文本由代码构造：`<固定worker> worker --request <portable路径> --sha256 <可信SHA>`。命令中没有用户提交的 shell 内容。控制器用 SFTP 独立完整 SHA 读回固定的 `Sezika.RemoteWorker` NativeAOT image，再交付 typed request。worker 必须在实际 NativeAOT runtime 中运行，在执行前后独立核自己与 payload 的精确全目录文件集（最多512文件/512MiB/深度8、逐文件完整 bytes/SHA）。自包含 JIT payload 必须含 entry DLL、deps、runtimeconfig、CoreLib、coreclr 和 hostpolicy。代码、运行时和依赖不能借用未绑定的文件；78KB apphost SHA 不再独立承担代码身份。

通过这些检查后才用 `ProcessExecutor` 的原生 broker/watchdog 执行结构化 argv；执行证据写 frozen code/runtime 目录之外。STDIN payload 仅走加密内存通道，不落盘。完整 `worker-result.json` 同时绑定 request SHA、两份 bundle SHA、原 execution receipt SHA 和实际进程树结果；控制器由新 SFTP 句柄完整读回，再核各身份、成功状态、输入/输出与子树回收。SSH exit 不替代这些检查。NativeAOT worker 的正式能力仍以实际 AOT publish/run 回执为准。

连接只允许直连一次与本机 HTTP proxy `127.0.0.1:7890` 一次；错误主机键不走第二路尝试。文件写入和 worker 派发没有自动重复。取消只向本 request 的精确 CANCEL 文件发信号；连接失败或取消后尚无实际执行/回收回执时保持 UNKNOWN，不能用连接关闭证明远端进程已退出。

离线 `checks` 是小型合同/键指纹检查，明确不作为真实 SSH/SFTP 协议证据。真实回环集成使用独立的任务内 SSH server/key/config/PID 身份和实际 SFTP 字节，再测试断线、恢复、拒绝未知 key、取消及 worker native 回收。真实服务器、模型训练和外部部署本轮均 **NOT_RUN**。

T2 完整代码/runtime 清单与 T3 的数据、PTX/ABI 及其他资产清单分别绑定；前者不替代后者。旧 SSH/Bash/Paramiko 脚本不由本工具执行。
