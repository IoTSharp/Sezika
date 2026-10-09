# 租用 GPU 固定实验入口

2026-10-09。用户授权上传资料及在新租用 Linux x64 GPU 服务器训练一轮，已提供实际 SSH 连接和内存认证。Ubuntu24.04/RTX5090/driver610.43.02上包/合同/tiny准入、唯一固定训练、独立验证及原32回归已完成；完整本地模型于北京时间21:17:08下载并核SHA，本机独立证据核验10/10 PASS。见[完整证据](evidence/rental-training-2026-10-09.md)。旧服务器的训练完成和驱动故障保持为历史。

用户给定租用创建时间为北京时间2026-10-09 20:22，四小时租期到2026-10-10 00:22；本轮训练及完整本地模型下载目标为2026-10-09 23:22，留下最后一小时余量。绝对UTC时间和下载预留写入任务根 `deadline.json`。head/report/checkpoint保存后优先运行 `Download-Model.ps1`，核远端和本机SHA，再将已有冻结encoder/tokenizer组成独立 `trained-model` 本地目录。时间比较保留DateTime的UTC kind，不能将ConvertFrom-Json已转换的日期再按本地文本Parse。

完整外部资产包保存于 `.artifacts/rental-training-20261009-01a12099/`，权重单独放在 `package/encoder.safetensors`，不嵌入程序集或 NuGet，不提交大权重到 Git。准备脚本复用已冻结 Linux x64 .NET 10.0.12 自包含工具、完整215题及全256派生账本、修题/教师原件、原修正32题、固定模型 manifest、模型卡/来源锁、数据来源和许可说明、PTX/ABI、运行监护和 SHA 清单。模型、tokenizer、数据、教师许可边界保留原声明；正式发布审签保持 pending。

本机 PowerShell 7：

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -File 'D:\source\Sezika\tools\rental-training\Prepare-Package.ps1'
& 'D:\source\Sezika\tools\rental-training\Invoke-RentalTraining.ps1' -HostName '<host>' -Port 22 -User '<user>' -IdentityFile '<absolute-private-key-path>'
# 也可传整条纯 ssh 命令（仅解析参数，绝不执行原字符串）：
& 'D:\source\Sezika\tools\rental-training\Invoke-RentalTraining.ps1' -SshCommand 'ssh -p 22 user@host'
# 密码只在内存/子进程环境中传给 OpenSSH askpass，不写入脚本、连接文件或日志：
$credential = Get-Credential -UserName '<user>'
& 'D:\source\Sezika\tools\rental-training\Invoke-RentalTraining.ps1' -HostName '<host>' -User '<user>' -Password $credential.Password
```

私钥只在本机使用，不复制到包或服务器。默认使用 agent/key；新连接的主机公钥首次以 OpenSSH `accept-new` 保存于任务专属 known_hosts，后续变化拒绝。如提供 `-HostKeyFingerprint 'SHA256:...'`，连接前以有界 keyscan 核验。日志保留首次信任方式，不把首次取得的公钥当独立验证。

默认远端目录是用户 home 下 `sezika-experiments/sezika-rental-20261009-01a12099`，可用 `-RemoteBaseDirectory /absolute/path` 指定有足够空间的盘。`-GpuUuid GPU-...` 可指定设备，否则选择预检中无现有 compute 使用者且 free≥5120MiB 的 GPU；不停止其他进程或安装驱动。C# 仍使用 CUDA Driver；无需现场构建 kernels，CUDA Toolkit 镜像标签不能代替实际 tiny 检查。Linux libc/动态依赖在运行前核验；实际兼容性未运行时为 NOT_RUN。

入口按预检→上传→包/文件 SHA 校验和安全解包→数据合同小检查→tiny GPU→唯一训练→优先下载并核完整本地模型→独立新进程加载/231次重新编码→原修正32回归→结果下载和 SHA 核对执行。SGD≤300s，训练/独立验证 CLI 各≤1500s、每阶段≤384forwards，GPU并发1/owned≤3GiB、host≤4GiB；回归≤32forwards/300s。固定零初始化 head，encoder冻结，不按 dev 调参，不自动加训、扩量、LoRA、encoder反传或调用教师。Score 无新监督。

每一步失败即停止后续 GPU 工作，保留原回执；连接中断后可重跑入口，已核验的上传与已成功阶段只复核/跳过。训练阶段目录一经创建，不允许再次训练；不完整/失败回执需要另行处理，不能删目录重置。新报告绑定实际 GPU/driver/输入和输出身份，不能复用旧 report SHA 冒充新训练结果。

创建本机任务根的 `cancel.request` 可取消正在执行的本机步骤；入口同时向当前远端 runner 的精确 cancel 文件发信号。直接断开 SSH 不是 GPU 回收证据，Linux runner 的独立期限与精确进程清理继续生效。长任务登记 PID/start/argv/父链，结果明确列出 payload exit、runner exit、最终 live/unknown 和资源清理；短命中间命令的观测局限保留。失败日志和交付物保留，仅清理核实过归属的任务临时对象。

结果中教师一致性、CE、换序选中 ID 和正式质量分开。人审、许可发布、质量、校准、封存、新 head AOT/性能仍 pending；本轮不 push、发布、部署或上传模型平台。
