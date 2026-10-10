# Windows 模型发布凭据

Hugging Face 发布令牌按用户 2026-10-09 的明确要求，保存于当前 Windows 用户的 **凭据管理器 → Windows 凭据 → 普通凭据**。

- 目标名：`Codex:HuggingFace:IoTSharp:Sezika`。
- 原生接口：`CredWriteW` / `CredReadW`，Generic credential，`CRED_PERSIST_LOCAL_MACHINE`。
- 保存后读回比较通过；令牌只在进程内用于构造 `HfApi(token=...)`，没有执行把新令牌写入 Hugging Face 明文缓存的登录操作。
- 新令牌未放入命令行参数、持久环境变量、源码、发布包或日志。保存使用不回显的终端输入；原生读写缓冲区在释放前清零。Python 的不可变字符串仅释放引用，不声称已证明全部内存字节擦除。

凭据目标名可以记录和复用，凭据值不能写入 Markdown、Git、模型卡或交接。模型上传与公开验收仍须分别核身份、组织、固定清单和真实平台回执。HF 凭据不用于模力方舟；后者有独立访问令牌和登录流程。

本轮 ignored 发布目录中的 `windows_hf_credential.py` 是原生读取桥接，`hf_publish.py --windows-credential` 从上述目标读取。原生 dummy 写入/读取/删除探针、实际保存读回、HF 身份验证及独立静态审查回执保留在该任务目录；审查原 PARTIAL 和后续修正 PASS 均保留。

2026-10-10 用户提供的独立模力方舟令牌也已保存于同一 Windows 原生安全存储，目标为 `Codex:MoArk:IoTSharp:Sezika`；不回显终端输入、CredWrite/Readback 比较通过。首次输入超过60秒安全失败，没有保存；重新输入后的实际保存成功，历史回执保留。

官方 Windows `gai v0.0.2-alpha.2` 的 dummy 探针进一步确认，它把令牌保存在系统凭据管理器中。此模型专用目标分别为：

- `gitee-ai.IoTSharp.Sezika-mmBERT-8192-experimental:gitee-ai-token`：模力方舟令牌。
- `gitee-ai.IoTSharp.Sezika-mmBERT-8192-experimental:gitee-token`：已核身份的 Gitee 令牌。

本轮通过原生接口在进程内读取既有凭据并写入这两个目标，不把真实值传给 `gai config` 参数。按实测，gai目标为 UTF8 blob，主发布桥接目标为 UTF16-LE；均为 Generic、Persist=2，写入和读回比较分别验收。`.git/config` 只包含适配器路径和非秘密选项。原生缓冲区清零；Python字符串全内存擦除仍未证明。独立安全审查通过；平台上传接口HTTP500和模型发布状态另见[实际回执](evidence/moark-model-publishing-2026-10-10.md)。

2026-10-10 用户随后明确改用 **maikebing 个人归属**并提供新模力方舟令牌。新目标为 `Codex:MoArk:maikebing:Sezika`，沿用上述 Windows 原生安全存储，不回显输入，写入和读回比较通过。个人模型专用 gai 目标为 `gitee-ai.maikebing.Sezika-mmBERT-8192-experimental:gitee-ai-token` 与同前缀 `:gitee-token`，分别读取新 MoArk 凭据和既有已核 Gitee 凭据；值均未放入命令行或 Git 配置。

个人模型的官方上传初始化实际返回 HTTP200。网页 `/api/base/*` 使用该 Bearer 令牌返回401，不代表其官方模型上传权限无效。存储临时 AK/SK/session token 仅在上传进程内使用，不落盘；日志记录请求身份、状态及公开文件校验值。个人模型的完成状态以独立发布回执为准，旧组织失败记录保留。
