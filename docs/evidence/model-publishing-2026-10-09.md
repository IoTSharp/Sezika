# 实验模型发布准备与认证回执

2026-10-09，Asia/Shanghai。用户已授权将本轮实验模型上传 Hugging Face 与模力方舟，并指定模力方舟 **IoTSharp** 组织。公开发布尚未完成；预定仓库地址不能作为已发布链接。

## 冻结公开包

本机公开目录：`D:\source\Sezika\.artifacts\model-publishing-20261009-01a12099\package-staging\package`。18 文件，**1,245,737,223 bytes**。保留原 encoder、tokenizer、model.json、训练 head/checkpoint 的字节；新增双语模型卡、精选实验摘要、C# head 加载身份、分项许可通知、发布 manifest 和 SHA256SUMS。

| 项目 | SHA256 |
| --- | --- |
| encoder | `6168c84d2498cdacdcafee16b71a69b5c1636ad569bfabaf2855f5ce7dabd528` |
| tokenizer | `197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356` |
| head | `4205457556736ab07799b462d42c377710e765cb14edb9aae508c1558c5e9d64` |
| checkpoint | `4709e847a135265f71c092bb18c66cf291de87d90970cd8932add6fbcb7f4264f` |
| publication-manifest.json | `f3f3debd55fda91d42f9b70e0a6db4f8ab46bc0e83b84668e555aac8b081996b` |
| SHA256SUMS | `b89dcfbeef2cc61612c0d92913c10f2a90f4bf6377c1079a54ef10eb1cc86059` |

准备工具 [`Prepare-Package.ps1`](../../tools/model-publishing/Prepare-Package.ps1) 先通过两文件 copy/hash/path/额外文件拒绝探针，再准备并核验全部资产。加载字段修订仅改安全元数据；旧公开文件先核 SHA，5 份旧元数据备份留在 ignored 目录，不上传。主调度另核全部文件大小/SHA、允许集合及敏感资料排除，`hf-local-check.json` 为 PASS。

原逐题训练/验证报告、token_ids、题文、教师原始响应、连接资料及进程原回执未进入公开包。完整原模型和证据仍保留。完整包 `license: unknown`；encoder MIT 声明、Gemma tokenizer 条款、教师权利未知分别披露，见[发布材料审查](../model-publishing-review-2026-10-09.md)。正式质量、校准、封存、新 head AOT/性能继续 pending。

模型卡提供真实 `IndependentMarkerHeadAssetStore.Load(...).Head` API 及所需 identity/feature/head SHA。当前实验 CLI 仍需要完整训练报告，公开摘要不能替代；示例仅核签名，没有重跑推理。实验教师一致 train118/162、development27/53、换序7/16，selected ID 改变6/16，Score新监督0。

## 平台状态

| 平台 | 固定目标 | 实际回执 |
| --- | --- | --- |
| Hugging Face | `IoTSharp/Sezika-mmBERT-8192-experimental` | 已认证 maikebing，可见 IoTSharp；创建请求返回 **403**。随后只读确认目标不存在，未开始上传。缓存只有一个 OAuth 凭据，没有另一份可用的缓存写入凭据。 |
| 模力方舟 | `IoTSharp/Sezika-mmBERT-8192-experimental` | 用户指定 [IoTSharp 组织控制台](https://moark.com/IoTSharp/dashboard/models)。独占浏览器显示“请先登录”，实际组织 GET 返回 **401**；现有 Gitee 凭据的 Gitee identity 成功，同凭据的 MoArk 组织 GET 仍401。 |

已索取必要的 HF 组织创建/写入凭据及模力方舟有效访问令牌或可复用登录会话。这是认证资料缺失，不是再次申请发布许可。未创建个人仓库、未猜写入 API、未公开假链接或 push 源码。

取得认证后先核身份、组织及失败请求后的远端状态，再上传固定公开包；模力方舟按官方 Git/LFS/gai 通道发布。成功后核实际 commit、公开文件集合/大小/SHA，大权重确认真实对象可取而非仅 LFS pointer，随后更新中英 README 链接。部分失败保留现场，先检查远端状态，不盲目重复写请求。

## 证据与收口

ignored 发布任务根 `.artifacts/model-publishing-20261009-01a12099/` 保存准备、加载元数据修订、本地核验、HF创建失败、认证复查、MoArk身份测试及进程日志。原日志不整体复制到 Git。发布脚本限定固定18路径、精确总字节和两个发布清单的实际SHA；独立只读审查提出的冻结绑定及LFS验证方法问题已窄改。

HF 创建尝试 PID67172，payload exit1/HTTP403，guardian Failed，最终 live0/unknown0；只读调查 PID28956/34424 均 exit0/Succeeded。首次 tiny SHA 常量错误失败、修正后 payload0/短命进程 CleanupFailed/unknown1 原回执保持；补查 PID26808/78456 及直系子进程均不存在，未补造历史成功。完整本地核验 PID81840 及冻结绑定修正后 PID81312 均 exit0/Succeeded。独立发布脚本静态审查PASS，不代表真实平台验证通过。

最终 root 发布进程补审覆盖13回执、36登记/回执身份，当前36全部absent、live0、未匹配0。首次逐项查询达到45秒界而停止，随后仅将同一范围查询分批执行，3.589秒完成；原失败保留。MoArk浏览器调查另核7回执/86身份：85absent、1PID已由无关进程复用并保留，本任务owned live0/unknown0，所有命名session关闭。精确范围分别见 `root-process-closure.json`、`moark-discovery/discovery-report.md`，未按名称杀进程。

仓库训练技能已独立提交，见[技能验收](rental-training-skill-2026-10-09.md)。本轮没有重训、教师调用或新 GPU 阶段；原监督 heartbeat 保持 PAUSED。
