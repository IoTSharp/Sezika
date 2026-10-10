# 模力方舟 maikebing 个人模型发布回执

2026-10-11，北京时间。用户明确改用 maikebing 个人令牌创建并尝试上传。**公开库已创建、完整 encoder 已上传到 BOS 存储；平台注册、Git 模型提交与完整匿名下载验收尚未完成。** [模型库](https://moark.com/maikebing/Sezika-mmBERT-8192-experimental)当前不能当作完整模型下载地址。Hugging Face 的既有18文件完整发布保持，见[前次证据](model-publishing-2026-10-09.md)。机器可读汇总见[JSON](moark-maikebing-model-publishing-2026-10-11.json)。

## 真实身份和完整文件

2026-10-10浏览器创建；独立公开API确认模型ID28817、Gitee仓库ID50738359、公开权限和默认master。个人checkout18文件共1,245,737,297 bytes逐项SHA通过，本地模型提交06ebe82325e58c0ae723fb9014a38cdb2f87b22a。最新匿名远端查询仍为初始f33889013d3c2d60bb25edf5e725681d370f2fd2；准备提交不是远端完成证据。

可用于手动逐文件上传的完整目录：`D:\source\Sezika\.artifacts\model-publishing-20261009-01a12099\moark-maikebing-20261010\model-repository`。仅选择18份发布文件，不上传`.git`或任务日志；18文件清单见目录内`SHA256SUMS`（17项加清单本身）。encoder为1,227,772,549 bytes，tokenizer为17,525,329 bytes，另含C#运行时所需manifest/head、组件许可和模型卡。没有把大权重提交到本项目Git。

旧`package-staging/package/README.md`在本轮开始前发生漂移：当前SHA e0e42904b751fdf970590fed7ce871565af86d6897f86198dcd7fa1c5ebed28f，冻结预期385f40f051d9a62a134309e589fe0e2bdf8ce5ea4aa60ae66775bda6c1824064。当前版本完整保留；个人checkout使用旧MoArk准备目录内SHA匹配的原模型卡。旧package目录未修改，不能称它目前全项通过；个人checkout的18文件已通过。

## 上传与登记的实际结果

encoder完整源SHA为6168c84d2498cdacdcafee16b71a69b5c1636ad569bfabaf2855f5ce7dabd528。一次完整BOS上传完成196个6 MiB分片（末片938,629 bytes），上传脚本的`put_part`在计入成功分片前核真实PUT200与返回ETag等于本地MD5；最终成功回执累计196分片，累计1,227,772,549 bytes；另一次未取得HTTP状态的尝试保留。实际合并POST200，bucket/key/eTag核验通过，耗时758.631秒、实际出站201次。随后官方客户端经传输层发出真实BOS HEAD200，Content-Length=1227772549。完整源SHA、分片校验、合并和HEAD不替代平台注册及匿名完整内容SHA。

tokenizer OID为197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356；平台missing-lfs两对象查询仅列encoder缺失，说明tokenizer被平台登记，但尚未做个人库匿名完整下载验收。

原版官方gai HEAD跳过已有对象后，真实callback返回200/JSON true，missing-lfs仍列encoder。观察到callback的`lfs_file_path`为空。初版传输层过严阻止已知200后的相同官方轮询，已修正并保留失败；扩大轮询后仍未完成。加入name、改成LFS对象路径、再由真实Git LFS产生协议，三种后续尝试仍真实产生空路径；分别取消并保留监护回执。前两次字段假设未获实测支持，不将其写为已修好的客户端原因，也不据此宣称平台bug或正常后台延迟。

随后按照已观察官方body结构，以冻结正确路径`encoder.safetensors`、OID及size直接调用同一callback接口，完整源SHA重新检查通过；仅请求一次。该请求ReadTimeout，HTTP结果未知，不重复写入。独立新进程在112.514秒内做8次真实missing-lfs查询，每次HTTP200仍列encoder缺失。当前状态是**登记未验证**，不是成功，也不证明服务端确定没有后续处理。

原官方直连/代理大上传RequestTimeout、两连接上传MalformedJSON及error-complete、退出0却有error、各次取消和登记超时均保留。官方v0.0.2-alpha.2二进制身份与静态字段审查保留；没有找到官方升级或修复说明。旧[IoTSharp组织失败](moark-model-publishing-2026-10-10.md)保持为独立历史，个人存储完成不等于组织镜像完成。

## 凭据、收口与接续

个人MoArk令牌已按用户要求写入当前用户Windows凭据管理器`Codex:MoArk:maikebing:Sezika`并读回比较；官方gai的两枚个人模型专用凭据也通过原生安全存储，见[说明](../model-credential-storage.md)。没有令牌argv、明文Git配置或Git记录；临时BOS凭据仅在上传进程内使用，不宣称Python字符串全内存擦除。

完整BOS上传监护exit0/FinalCheckComplete=true/live0/unknown0，CleanupErrors和ObservationWarnings空。本次空路径轮询、name、对象路径、真实Git LFS和单次正确路径请求以及读回都保留各自真实Status/ExitCode；取消不改写为成功。文档更新短进程监护实际为CleanupFailed/exit0/unknown1，原回执保留。随后独立只读核37份回执、139条身份/138唯一PID，当前匹配存活0、unknown0；晚于已核父进程退出时刻才创建的子进程和PID复用进程保留，不发信号。首次把后创建子进程列为unproven的失败也保留。独立checker自身监护exit0/FinalCheckComplete=true/live0/unknown0，CleanupErrors空。该后核只证明当前身份快照，不补造缺失历史argv或未观察的过去子链。59个准确文档/JSON/配置文本的真实凭据值泄漏检查通过；验收不是Python全内存擦除证明。

接续先只读查两个固定OID及远端master。未知的正确路径callback不自动重发。若平台登记两个对象均完成，再推送固定准备提交，最后匿名验证19 Git路径（18文件加平台.gitattributes）、小文件字节/SHA、两个LFS pointer和两个大对象完整流式SHA。未满足前述条件不绕过LFS hook发布悬空pointer。原始回执与全部失败保留在ignored任务根。

训练技能和固定租用实验早已完成；本轮无重训、教师调用、GPU/旧服务器修改、HF重传或源码push。模型正式质量、概率校准、新head AOT/性能与第三方权利补证保持各自pending。
