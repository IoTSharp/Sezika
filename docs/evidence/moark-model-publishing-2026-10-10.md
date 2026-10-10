# 模力方舟 IoTSharp 实验模型发布回执

2026-10-10，北京时间。用户明确授权上传到模力方舟 IoTSharp 组织并提供独立令牌。**公开模型库已经创建，权重上传仍被平台接口阻断，不能称镜像发布完成。** Hugging Face 的完整18文件验收结果保持，见[既有发布证据](model-publishing-2026-10-09.md)。

## 身份、凭据与固定包

专用 Chrome 实际登录后，`GET /api/base/IoTSharp/profile` 返回200。官方新建页面选择 IoTSharp 和公开权限，`POST /api/base/IoTSharp/models` 返回200；公开查询确认模型ID `28816`、底层仓库ID `50735916`、`public=true`、默认分支 `master`。模型库链接为 <https://moark.com/IoTSharp/Sezika-mmBERT-8192-experimental>，当前仍为平台初始文件。

用户令牌已通过不回显输入保存到当前用户 Windows Credential Manager：`Codex:MoArk:IoTSharp:Sezika`，原生写入和读回比较通过。实际值没有写入源码、命令行、明文配置或发布包。HF和MoArk分别使用独立凭据。现有 Gitee 凭据核官方身份为 `maikebing`；详细存储方式见[凭据说明](../model-credential-storage.md)。

镜像准备严格复用冻结包：18文件、**1,245,737,297 bytes**；逐项源文件和复制文件 SHA 校验通过。`SHA256SUMS` SHA 为 `53d51e5904f9a8fc9940536812462339a18d71a42319b832ad7f4d576cbf39c8`；发布 manifest SHA 为 `18aa253b28ffc20f189b0c450fe35432df90488785e7045d0d3b8d41b3998c13`。HF revision仍为 `a46028dfebd539bd04a1c567fbb24c26a649a50a`，未重传或修改。

独立模型 Git 工作目录在 ignored 任务根的 `moark-current/model-repository/`。平台初始 `.gitattributes` 有四个 `merge:lfs` / `diff:lfs` 语法错误，原文件保留，镜像提交窄改为等号，并给 tokenizer 指定 LFS；该平台文件单列，未改变18文件包。encoder及tokenizer的 LFS OID 分别为 `6168c84d2498cdacdcafee16b71a69b5c1636ad569bfabaf2855f5ce7dabd528`、`197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356`。

## 实际上传结果

| 阶段 | 实际结果 | 边界 |
| --- | --- | --- |
| 匿名 Git 查询及克隆 | 成功，初始提交 `6a64335eeea35e0c4058a88ce507cc04cd0c402d` | 当前公开文件只有 `.gitattributes`、`README.md`、`README.en.md`。 |
| Git dry-run | exit0，Everything up-to-date | 不能当作完整模型写入或大文件验收。 |
| 准备模型提交 | 本地提交 `53ca528178af60aa413b08e2c4ce40b83070ce5e` | 未推送成功。源包的原始空白/CRLF保持；对原始上游文档的首次 diff-check 失败保留，随后只检查本次修改的平台属性文件。 |
| 普通 Git LFS 上传 | 被拒绝：`LFS only supported repository in paid or trial enterprise` | 未确认任何新对象完成，Git ref未更新。 |
| 官方 gai 适配器 | 初始化返回 `Internal Server Error` | 两个 API base 和直连/代理调用均保留实际回执，不能宣称平台全面故障。 |
| 独立请求核验 | `POST /api/v1/hub/lfs/missing-lfs` 返回 **HTTP500**；两枚实际凭据、组织名、库名、OID及大小均一致 | 单tokenizer输入和大小写诊断仍500；同形匿名请求401。没有把认证路由响应当作文件上传成功。 |
| 单对象协议探针 | 初始error之后 gai输出complete，并说already uploaded | 初始化失败后客户端的complete没有服务端对象证明，**不计上传成功**，未据此跳过 LFS hook 或推送悬空pointer。 |
| 最后远端复核 | `master`仍为初始 `6a64335...`，无本轮模型提交 | 新模型18文件公开验收为 **NOT_RUN**。 |

官方上传文档推荐 gai：<https://moark.com/docs/products/models/upload>。本次从官方 `moark/cli` release 获取 Windows amd64 **v0.0.2-alpha.2**，安装在任务独占的 `moark-current/gai.exe`；官方 MD5 `aba4f65f0068057ed8c2e3112ad2fe6f` 匹配，SHA256 `a6bdf492a706705631be4b61caa93ba10f63c1df89410e1a16f71162e0353877`，实际 version/help 已核。不执行在线安装脚本、不改全局 PATH。

安全 dummy 探针证实该官方 Windows 版本使用系统凭据管理器，而不是把令牌写入 `.git/config`。按实测编码，以原生 CredWrite/Readback 将两枚凭据分别接入 `gitee-ai.IoTSharp.Sezika-mmBERT-8192-experimental:gitee-token` 和同前缀的 `:gitee-ai-token`，UTF8 blob、Generic、Persist=2。`.git/config` 只保存适配器路径、API地址和非秘密选项；未执行文档中携带真实令牌参数的 config 命令。独立静态审查通过，不宣称 Python 不可变字符串已全内存擦除。

## 接续与收口

本轮 ignored 任务根为 `.artifacts/model-publishing-20261009-01a12099/`，`moark-current/` 保留冻结提交、Windows凭据接入回执、官方工具及真实失败。普通 LFS企业权限限制与 gai missing-lfs HTTP500是两个实际问题。接续时先检查这两个入口和远端 ref，成功取得真实对象回执后才推送准备提交，并独立核18路径/大小/SHA及实际大文件内容。不得把已经创建的模型库当作可下载完整镜像。

初次凭据输入超时、浏览器许可证定位超时、短进程 CleanupFailed、配置入口找不到 command path、helper语法错误及上游原字节 whitespace 检查失败均保留。一次二进制字符串检查超过预期时间后进行了精确进程核查；核查时已进入有界子阶段，没有批杀进程，后续退出状态单列。没有重训、教师调用、新GPU操作、源码push或重传HF；监督 heartbeat仍PAUSED。模型质量、校准、新head AOT/性能与第三方权利补证保持各自pending。

当前进程补核覆盖28份本轮监护回执、156条身份：153已退出、3条PID已被后续不同创建身份复用，本任务匹配存活0。复用PID的子进程保留，未按名称或历史PID终止；首次补核因发现这些子进程而失败，后续分类复核保持原失败。直接凭据输入与二进制检查的三个额外PID及直系子进程也已确认不存在，历史登记限制保留。专用Chrome及本任务HTTP转发服务均已关闭。

最终 `moark-current/publication-blocked.json` 明确记录 `BLOCKED_platform_LFS_initialization_HTTP500`、远端初始提交和18文件验收NOT_RUN。35个准确文档/JSON/模型Git配置文件，共400,005 bytes的凭据值泄漏检查通过。独立七文件文档审查未发现事实错误，特别确认初始化error之后的complete及Git误显100%都没有被计作完成；该审查没有读取真实令牌。全部原始回执仍在ignored任务根，未整体写入Git。
