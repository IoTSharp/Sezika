# Windows 模型发布凭据

Hugging Face 发布令牌按用户 2026-10-09 的明确要求，保存于当前 Windows 用户的 **凭据管理器 → Windows 凭据 → 普通凭据**。

- 目标名：`Codex:HuggingFace:IoTSharp:Sezika`。
- 原生接口：`CredWriteW` / `CredReadW`，Generic credential，`CRED_PERSIST_LOCAL_MACHINE`。
- 保存后读回比较通过；令牌只在进程内用于构造 `HfApi(token=...)`，没有执行把新令牌写入 Hugging Face 明文缓存的登录操作。
- 新令牌未放入命令行参数、持久环境变量、源码、发布包或日志。保存使用不回显的终端输入；原生读写缓冲区在释放前清零。Python 的不可变字符串仅释放引用，不声称已证明全部内存字节擦除。

凭据目标名可以记录和复用，凭据值不能写入 Markdown、Git、模型卡或交接。模型上传与公开验收仍须分别核身份、组织、固定清单和真实平台回执。HF 凭据不用于模力方舟；后者有独立访问令牌和登录流程。

本轮 ignored 发布目录中的 `windows_hf_credential.py` 是原生读取桥接，`hf_publish.py --windows-credential` 从上述目标读取。原生 dummy 写入/读取/删除探针、实际保存读回、HF 身份验证及独立静态审查回执保留在该任务目录；审查原 PARTIAL 和后续修正 PASS 均保留。
