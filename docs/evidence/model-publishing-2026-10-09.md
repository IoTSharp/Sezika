# 实验模型发布准备与认证回执

2026-10-09 至 2026-10-10，Asia/Shanghai。用户已授权将本轮实验模型上传 Hugging Face 与模力方舟，并指定模力方舟 **IoTSharp** 组织。2026-10-10北京时间00:52已核Hugging Face完整上传与18文件匿名验收PASS；模力方舟仍待独立认证，尚未创建或上传模型库。

## 冻结公开包

本机公开目录：`D:\source\Sezika\.artifacts\model-publishing-20261009-01a12099\package-staging\package`。18 文件，**1,245,737,297 bytes**。保留原 encoder、tokenizer、model.json、训练 head/checkpoint 的字节；新增双语模型卡、精选实验摘要、C# head 加载身份、分项许可通知、发布 manifest 和 SHA256SUMS。

| 项目 | SHA256 |
| --- | --- |
| encoder | `6168c84d2498cdacdcafee16b71a69b5c1636ad569bfabaf2855f5ce7dabd528` |
| tokenizer | `197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356` |
| head | `4205457556736ab07799b462d42c377710e765cb14edb9aae508c1558c5e9d64` |
| checkpoint | `4709e847a135265f71c092bb18c66cf291de87d90970cd8932add6fbcb7f4264f` |
| publication-manifest.json | `18aa253b28ffc20f189b0c450fe35432df90488785e7045d0d3b8d41b3998c13` |
| SHA256SUMS | `53d51e5904f9a8fc9940536812462339a18d71a42319b832ad7f4d576cbf39c8` |

准备工具 [`Prepare-Package.ps1`](../../tools/model-publishing/Prepare-Package.ps1) 先通过两文件 copy/hash/path/额外文件拒绝探针，再准备并核验全部资产。加载字段修订仅改安全元数据；旧公开文件先核 SHA，5 份旧元数据备份留在 ignored 目录，不上传。主调度另核全部文件大小/SHA、允许集合及敏感资料排除，`hf-local-check.json` 为 PASS。

原逐题训练/验证报告、token_ids、题文、教师原始响应、连接资料及进程原回执未进入公开包。完整原模型和证据仍保留。完整包 `license: unknown`；encoder MIT 声明、Gemma tokenizer 条款、教师权利未知分别披露，见[发布材料审查](../model-publishing-review-2026-10-09.md)。正式质量、校准、封存、新 head AOT/性能继续 pending。

模型卡提供真实 `IndependentMarkerHeadAssetStore.Load(...).Head` API 及所需 identity/feature/head SHA。当前实验 CLI 仍需要完整训练报告，公开摘要不能替代；示例仅核签名，没有重跑推理。实验教师一致 train118/162、development27/53、换序7/16，selected ID 改变6/16，Score新监督0。

## 平台状态

| 平台 | 固定目标 | 实际回执 |
| --- | --- | --- |
| Hugging Face | `IoTSharp/Sezika-mmBERT-8192-experimental` | 上传及18文件匿名验收 **PASS**，公开 revision `a46028dfebd539bd04a1c567fbb24c26a649a50a`；[模型下载](https://huggingface.co/IoTSharp/Sezika-mmBERT-8192-experimental)。新令牌已安全保存并核maikebing/IoTSharp身份，模型卡HTTPS许可链接复验通过。 |
| 模力方舟 | `IoTSharp/Sezika-mmBERT-8192-experimental` | 用户指定 [IoTSharp 组织控制台](https://moark.com/IoTSharp/dashboard/models)。独占浏览器显示“请先登录”，实际组织 GET 返回 **401**；现有 Gitee 凭据的 Gitee identity 成功，同凭据的 MoArk 组织 GET 仍401。 |

HF 新令牌按用户明确要求保存于 Windows 凭据管理器 `Codex:HuggingFace:IoTSharp:Sezika`，实际读回比较及账户认证通过；不重复记录其值，没有将新令牌写入 Git、发布包、命令行、持久环境变量或 HF 明文登录缓存。见[凭据保存说明](../model-credential-storage.md)。此前 OAuth 的403为历史，新令牌已证明可创建组织仓库；只有模力方舟仍缺独立认证。未创建个人仓库、未猜写入 API或push源码。

HF 初始 revision `d114243af8934186653635cb00d7ff30d10c4922` 仅含 `.gitattributes`。首次提交被模型卡相对 `license_link` 拒绝，未上传模型；将该字段改为HTTPS并受控备份4份元数据，源模型/摘要/许可原文保持，公开包增加74 bytes，18文件重新核SHA。服务器模型卡复验PASS。之后Xet通道日志实际出现TLS握手EOF/连接重置，任务PID75144按精确登记身份取消并保留回执；切换SDK实际支持的HTTP/LFS路径（仅子进程 `HF_HUB_DISABLE_XET=1`），再次核同一空仓库revision后上传固定包，不重建仓库或盲目重复commit。

2026-10-09 23:53，北京时间：串行HTTP/LFS进程PID74916达到1800秒硬上限，监护结果 `TimedOut`，登记进程及子进程最终live0/unknown0；observer截止warning保留。独立公开读取仍为上述空revision，不能以磁盘IO计数称已上传。SDK1.23.0源码复核确认单文件multipart逐part执行、默认客户端上传timeout为空；具体卡住的请求没有足够日志定位。

2026-10-10 恢复：独立LFS batch检查确认encoder需77个16,000,000-byte分片、最后11,772,549 bytes，tokenizer对象已存在。新ignored预上传工具显式HTTP阶段超时、3worker、每part最多2尝试、协作预算1000秒和监护硬上限1200秒，令牌及签名URL仅保留在内存；全部分片须取得ETag并核编号/总bytes后才请求complete，再以独立batch无actions核对象存在。16-byte离线切分探针通过；短命进程guardian `CleanupFailed` 历史保留，后核PID不存在不补造成功。小head9619bytes实际multipart→complete→独立batch通过；前两次小文件batch选择basic导致缺header，失败回执保留，修正batch合同后重试通过。encoder当前上传在途，尚无完整commit或匿名验收。

00:31，北京时间：root根据早期吞吐预估提前取消首轮encoder预上传；当时54/77分片已取得ETag，864,000,000 bytes，约698秒。该取消判断不够准确，已向用户说明；`Cancelled` 原回执和未complete状态保留，不称权重对象已完成。唯一恢复进程改为1700秒协作预算/1800秒硬界，旧候选分片逐一重新核本机bytes/SHA，先补缺片；候选ETag只有服务端complete接受后才能称同session有效。若400/404拒绝，则在当前签名session内仅重传候选分片，再核全77编号/bytes及complete、独立batch。候选字节单列，不能混作当前session实际确认上传字节。

恢复进程的首次候选complete实际返回400，不能复用旧54分片；它清除候选状态，保留本session新增23分片并重传54分片。400/404本身不保证session有效，后续PUT或complete若失败必须保持FAIL；本轮后续PUT正在取得当前session的真实ETag。该恢复脚本的独立静态审查通过，公开模型发布仍以最终commit和匿名验收为准。

2026-10-10北京时间00:51已核恢复进程PID9320完成当前session全77分片，实际确认 **1,227,772,549 bytes**，全部候选均已被真实PUT回执替换；complete成功、独立batch确认对象存在，1056.244秒，guardian Succeeded/exit0/live0/unknown0。随后PID84492通过Windows凭据读取令牌，再核原空仓库与完整公开包后提交，revision **`a46028dfebd539bd04a1c567fbb24c26a649a50a`**。

00:52已核匿名 `HfApi(token=False)` 固定revision验证18路径、总 **1,245,737,297 bytes**、逐文件大小/SHA全部PASS。encoder/tokenizer核服务端LFS SHA并实际范围读取、比对本机前缀，确认可取真实对象而非pointer；其余16文件完整下载重算SHA。大文件没有重复完整下载，不能称全量二次网络SHA。结果 `hf-publication-complete.json` 为 `PASS_public_upload_all_files_verified`，31.481秒；guardian Succeeded/exit0/live0/unknown0。中英README已更新为真实下载链接，模力方舟仍明确未发布。匿名下载通过不构成正式质量、完整CLI部署或新head AOT/性能验收。

2026-10-10已取得模力方舟独立令牌和IoTSharp组织登录，并创建同名公开模型库。冻结18文件镜像提交已准备；普通Git LFS受企业权限限制，官方gai missing-lfs接口返回HTTP500，远端仍初始提交，权重上传未完成。模型库链接已按真实状态写中英首页，详见[本次模力方舟回执](moark-model-publishing-2026-10-10.md)。HF上述流程已经完成。失败保留现场，先检查接口与远端状态，不盲目重复写请求。

## 证据与收口

ignored 发布任务根 `.artifacts/model-publishing-20261009-01a12099/` 保存准备、加载元数据修订、本地核验、HF创建失败、认证复查、MoArk身份测试及进程日志。原日志不整体复制到 Git。发布脚本限定固定18路径、精确总字节和两个发布清单的实际SHA；独立只读审查提出的冻结绑定及LFS验证方法问题已窄改。

HF 创建尝试 PID67172，payload exit1/HTTP403，guardian Failed，最终 live0/unknown0；只读调查 PID28956/34424 均 exit0/Succeeded。首次 tiny SHA 常量错误失败、修正后 payload0/短命进程 CleanupFailed/unknown1 原回执保持；补查 PID26808/78456 及直系子进程均不存在，未补造历史成功。完整本地核验 PID81840 及冻结绑定修正后 PID81312 均 exit0/Succeeded。独立发布脚本静态审查PASS，不代表真实平台验证通过。

最终 root 发布进程补审覆盖13回执、36登记/回执身份，当前36全部absent、live0、未匹配0。首次逐项查询达到45秒界而停止，随后仅将同一范围查询分批执行，3.589秒完成；原失败保留。MoArk浏览器调查另核7回执/86身份：85absent、1PID已由无关进程复用并保留，本任务owned live0/unknown0，所有命名session关闭。精确范围分别见 `root-process-closure.json`、`moark-discovery/discovery-report.md`，未按名称杀进程。

仓库训练技能已独立提交，见[技能验收](rental-training-skill-2026-10-09.md)。本轮没有重训、教师调用或新 GPU 阶段；原监督 heartbeat 保持 PAUSED。

凭据文档、桥接及保存回执的第二次6文件静态审查通过，没有读取真实凭据或联网；审查累计约267秒，超过委派180秒上限，已停止后续读取并如实保留这一时间限制。上传恢复工具独立窄审无P1资源/完整性问题；1000秒仅为协作deadline，在途HTTP由外层1200秒监护硬界及任务归属回收保证。

最终root补核当前发布目录33份监护回执、57个登记身份，owned live0/unknown0，1.945秒，`final-process-closure.json` PASS；全部历史失败回执保留，范围仅登记的发布进程，没有全机扫描或按名称终止。该补核进程随后亦精确确认已退出。未清共享TEMP、模型资产或无关工作区改动。
