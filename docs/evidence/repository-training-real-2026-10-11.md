# 自有仓库训练实机证据（2026-10-11）

当前结果以[本机接续证据](repository-training-local-mirror-2026-10-11.md)、[结构化回执](repository-training-local-mirror-2026-10-11.json)及[已授权历史清理证据](repository-training-history-cleanup-2026-10-11.md)为准。private模型18底座+33训练产物、private数据275原始+8派生文件已从新认证clone完整bytes/SHA验收，optimizer已实际恢复。IoTSharp 当前提交 `9c522c4e227c52c86f493a90595a894a41e1935a` 的55个owned+1个保留文件、6,174,855,372bytes已匿名全SHA验收；一次精确lease历史改写后，fresh匿名全历史确认旧 `dfef170bc9e26dff7e4317fc08686d0abd013438` 详细报告提交和路径不再可达，当前模型文件差异0。平台旧对象/缓存物理删除仍NOT_VERIFIED。无需重租GPU或重复训练。

以下是服务器租期结束时的历史快照，状态为 **PARTIAL_MODEL_PUSHED_READBACK_PENDING**、发布验收 **NOT_ACCEPTED**。48步完整encoder/head训练和196次独立GPU前向已完成；当时maikebing private模型已正常Git/LFS推送，但独立完整认证读回480秒超时。数据已推送，当时private的新认证完整读回未执行。IoTSharp public **Sezika-Decision-Base v0.1-preview** 当时只有介绍，模型权重尚未发布。本机当时仅保存小型报告/证据，完整模型未下载。正式质量、概率校准、新权重AOT/性能至今仍未验收。旧[离线工程证据](repository-training-engineering-2026-10-11.md)及下列失败/预算快照保留历史范围，不覆盖后续本机证据。

本轮任务为 `encoder-full-20261011-121827`，本机目录 `D:\source\Sezika\.artifacts\repository-training-encoder-full-20261011-121827`；远端独立工作根 `/data/sezika-repository-encoder-full-20261011-121827`。用户已确认本轮租期结束，**最后已确认截止北京时间2026-10-11 15:00（UTC07:00）**；此前用户确认的续租及原截止14:18（UTC06:18）、训练阶段13:48和1800秒预留保留为预算历史。智能体未自行购买或续费，本轮远端工作已收口；租期变动不改变已经完成的48步范围。

| 固定输入 | Commit |
| --- | --- |
| 源码 | `9ee9eda7a45d41fa0f3a283e9377094ff6885760` |
| 数据 | `b8ea13b81aaaca722baf6a3c0f12e2217fc4353e` |
| 模型 | `06ebe82325e58c0ae723fb9014a38cdb2f87b22a` |

实际Ubuntu24.04.1、glibc2.39、RTX5090、driver610.43.02；GPU UUID `989DB6F63273D1DDB2B956CCED4F30A4`。容器nproc=384为宿主视图，cgroup实际25核、48GiB内存；/data初始可用49,820,409,856bytes。GPU主调度并发1，使用实际UUID和DOTNET_PROCESSOR_COUNT=25。全部模型、算子、训练调度为C#，GPU运行时采用静态CUDA Driver和构建期ILGPU PTX。

完整输入验收275数据资产、1,076,726,933bytes全部SHA通过；模型17条SHA清单及原18文件已验收。数据LFS pointer检查实际FAIL：clinc150/data_full.json和banking77/dataset_infos.json是原普通Git blob，被原宽泛属性标成pointer；另行objects检查、原Git blob身份及全量SHA通过，原件/属性未改。原FAIL不能改称标准fsck通过，训练来源不消费这两个源。

当前只适配kaivoss/system-one-270m-data固定revision原始train，25,002行最终21,913accepted：train17,615、development4,298；隔离3,089、duplicate0、最长7740token。其余17来源未适配；`FormalTrainingAdmitted=false`、`ExperimentalOnly=true`。teacher soft targets用于实验目标，不是实测正确率；未消费test或development标签反向。

| 实际Linux派生身份 | SHA256 |
| --- | --- |
| admitted manifest | `ccf7193de9efed49e9c6f511ac54907de13354a50761a8f808ad4f8462559bdf` |
| records | `9d62559b6e668dc14dd79e3819f6afbc984e7ab87366c983a3c66080527c18e6` |
| ledger | `b649c02f61a18c76858f096e5ed74d083ea955dd53abbc88917f661afb59aa76` |
| strict-length report | `c534980eafe46157f566ea867e7f67abd32c49a95547311a69c16716d4f53047` |

第一次合同准入沿用Windows准备SHA而失败，长度21,913/21,913已经通过但训练未启动。随后逐行转LF核整文件SHA与Linux版本一致，证明仅换行不同；冻结新的实际Linux计划及profile。旧失败与Windows准备物均保留。

实机tiny梯度4项覆盖Choice/Boolean/Score及AdamW，1,008坐标，最大绝对误差0.00013484732818777904；checkpoint7项、恢复后的1,008状态坐标逐位相同，最大差异0。首次启动因missing ICU而SIGABRT；仅安装官方Ubuntu源libicu74 74.2-1ubuntu3.1，实际库 `/usr/lib/x86_64-linux-gnu/libicu*.so.74.2`，保留默认Unicode，新stage复验通过。

真实mmBERT profile从底座和零head独立执行3次GPU反向/AdamW：800token同训练题两次，7740token最长训练题一次；第二次要求encoder第一层QKV梯度非零。profile最大单步13.6047607秒、总36.6357193秒、owned峰值12,017,759,752bytes（约11.2GiB）；未使用合成最大长度probe，状态`PASS_real_base_profile_only`、`QualityAccepted=false`。profile不保存供正式训练延续的权重。guardian实际completed/exit0，完整终检与清理通过，live/unknown/unchecked均0；CUDA资源已释放，结束观测仅2MiB。

固定17615步一轮无法按保守阶段预算在本租期准入。训练阶段公式为最大实测单步×4×剩余updates+600秒+每checkpoint保存留量；48步基于初次profile估算3332.114秒。它是准入估算，不是实测整轮耗时。用户明确批准48步全参数实验，必须另建计划并重新profile；只覆盖训练顺序前48条（约0.27%），不能称全量训练、收敛或质量改善。

新48步计划SHA `db54a65509a9188337b8e560ad77374d55829c82da42e33c6c95e4b3f27c31e1`，重新profile3次PASS：最长7740token，最大单步13.617305秒，总36.278922秒，owned峰值12,017,759,752bytes，guardian完整回收。冻结v7工具包清单SHA `939eb6cd520aba258283ea652524d9bf795acaf806a140139e87267a1f727b94`，归档SHA `0f4b87dcf4e70e9760238332787e9b8c5c61278ab10b6c78ca3f9adf52f12309`，旧v6/计划/profile均保留。固定48步训练于12:49:54在新stage启动，阶段3461秒，早于13:48截止约25秒；启动成功不等于训练完成。

训练实际完成48/48更新，从底座+零head独立初始化，状态`PASS_experimental_GPU_updates_saved_only`。训练PID4667，开始UTC04:49:54.9802404；平均观察loss1.2756282823781173，报告测点78.6151095秒（在末尾保存收口之前），guardian全过程84.260秒，owned峰值5,823,711,896bytes。新完整权重SHA `f683bc38e40627abd03659a1cc07f9123e0776635aa1bf38724a7d19c5185c20`，checkpoint receipt SHA `9f46e65f29d611c4c962481088a104730181dc819cea66c99b6e8be9a616db5d`，训练报告SHA `44c5b1a3d2960bef0f7fe83b8b341e0c4336f78222f740d6a0ea3cfb2589f122`。guardian completed/exit0，完整终检/清理通过，live/unknown/unchecked0，GPU回到2MiB/0%。loss跨不同训练题观察，不能据此宣称收敛或语言质量改善；48条中的序列长度与最长序列profile不同，也不能把两个显存值当优化对比。

完整结果归档约4.929GB，控制端D盘仅约9.1GB，无法同时保存归档、解包、发布checkout和匿名读回。第一次下载约2分钟后，主调度核SCP52316的PID、创建时间、完整argv和父链，仅停止本任务SCP并由有界runner回收子树；原Failed-1及546,275,328bytes partial保留，停止记录为本轮任务 `download-relocation-stop.json`。第二次下载改存 `C:\Users\mysti\Documents\Codex\Sezika-training-artifacts\encoder-full-20261011-121827`，C盘约67.5GB可用；新助手根据实际tarbytes准入 `2×archive+1GiB`，单项上限1500秒，总1800秒和14:18截止保留。

用户随后明确要求从服务器直接上传仓库，覆盖此前先下载到本机的路径。第二次SCP44732核PID/创建时间/完整argv/父链后停止，262,537,216bytes partial及 `download-direct-server-steering-stop.json` 保留。服务器独立GPU验证由supervisor PID5507派发，绑定实际package SHA `36adeeb7531e6212229e8df318bfd8dfd930703140b3be75576e0a873f50c20e`，完成结果见下文；后续按服务器正常Git/LFS推送dataset/model并独立新checkout完整读回（当前private数据认证、public模型匿名），本机仅小报告/日志/验收证据。此变更不构成本机完整下载通过，最终不得称完整模型已本机保存。

独立GPU验证实际完成，状态`PASS_new_package_GPU_experimental_validation_only`，实际模型字节核验通过。PID5512、UTC04:58:18.9315969，独立于训练PID4667；128原题（64train+64development）及其中68Choice左移，共196次重新GPU前向。owned峰值5,937,282,408bytes；guardian全过程54.320秒，completed/exit0、完整终检/清理及live/unknown/unchecked0通过，GPU恢复2MiB/0%。日志中的256为最多原题加换序次数上限，实际分母196不扩写为256。

| 固定实验原题 | 分母 | Mean soft-target CE | 与teacher argmax一致 |
| --- | ---: | ---: | ---: |
| train | 64 | 1.3304532719776034 | 36/64 |
| development | 64 | 1.059903010725975 | 39/64 |

这些数值是teacher分布目标的实验CE及argmax一致性，不是人类标注准确率、概率校准、正式语言质量或新权重AOT/性能证据。缺少训练前同子集对照，也不能据此声明48步提高了质量。

本机小证据保存到本轮任务 `local-evidence-v2`，归档SHA `bb7f1a7a1aa283152f2f007cf7d78343c4a4d6d014a72dc938bde8a5af266f44`，34文件/98,257bytes，33条内容清单加清单自身，安全解包/SHA通过。原v1的shell输出重定向先创建files.sha256后被通配符纳入，记录空清单自身SHA，实际解包拒绝；原tar/目录/FAIL保持，`small-evidence-archive-failure.json`记录原因与新v2。小包包含报告和字节身份，不含完整权重。

本机独立读取保存报告，分布、CE、argmax、原题/换序分母及候选ID左移验算通过：validation SHA `de575b7487b9d001b9cbafb9c9ce000ac071965fcb7ab53c94ef14b7000876b0`，最大CE重算绝对误差5.745255466926835e-6，68个Choice换序题selectedID稳定67、改变1。这表明换序检查已完成，本子集未达到预测完全不变。报告验算receipt SHA `3013806466be88fbfd6e30035ee9c3b1fbf05e24dd39ceeff976f9985e197206`；它是小规模CPU报告审核，不是新的模型前向或正式质量验收。服务器RAM凭据范围/传递/清理的3个dummy实机case通过且目录删净，dummy通过不记成发布成功；首次publisher的真实凭据使用和清理见下段。

首次服务器直传publisher实际执行到 `dataset-staged-check`，`git diff --cached --check` 退出2，调用数14，未commit或push。guardian资源回收通过，真实凭据RAM清理exit0；原FAIL保留。根因仅source-rights.json第1..10行保留CRLF，Git默认blank-at-eol把CR当尾空白。文件原619bytes及SHA `19825d7f1eecc673ae9ce380c7c29187e92ac24079c21c444780d17878847bbd`保持不变。单次Git调用设置 `core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol` 后实际staged检查通过，tiny fixture同时确认真尾空格仍被拒绝。

首个恢复stage由supervisor7974执行26次调用，staged检查通过并生成dataset本地提交 `d19515802d31310b4050226b51ab85c60f130d42`，父commit `08f1fdad70add4e0dd442ec3c796242df596c669`；随后 `dataset-push` 退出128，Username unavailable，实际远端当时仍是旧父commit。guardian完整回收及RAM凭据清理exit0。真实Git dummy复现helper输入直接EOF而非结尾空行，原helper错误拒绝；新版 `Install-RemoteGitCredentials.ps1` SHA `29099a1670fc5539b387718ea2c0c508356af40682fe59c069fdcfe3a9bad6b5`，11个正负dummy（真实Git dataset/model-LFS、两类EOF及范围/超时拒绝）和服务器真实Git dummy探针通过。没有因这次失败重新生成commit。

第二恢复stage由supervisor8894执行34次调用，在 `dataset-recovery2-worktree` 退出1，尚未push；全仓diff只包含已知两个原raw普通Git blob受宽泛LFS属性误显的差异。核本次 `.gitattributes` 与 `derived/<run-id>/` 的owned工作树diff通过，原pointer FAIL保留。该stage完整终检、资源回收及RAM凭据清理通过。第三恢复 `publication-recovery3-48-stage` 由supervisor9402执行，复核保存commit/parent、空index、owned完整SHA、提交路径集合和真实ref后普通push既有d195；实际45-push退出0，LFS两对象约62MB，46-remote-after准确d195。

dataset匿名新checkout、完整LFS实际下载及全部byte/SHA读回已通过，`public-readback-dataset.json`绑定真实d195：原275资产/1,076,726,933bytes保持，新增8文件/64,322,231bytes一致，完整hash阶段0.9803044116秒（不含下载时间）。这验收本轮原数据和派生数据真实公开字节，原pointer属性FAIL仍独立保留。

模型完整包prepare/owned完整SHA通过，但第三恢复在call57的model-staged-check退出2，仍未生成model提交。四份immutable notices自带原始尾空格：两个Gemma HTML分别1421条diagnostics、MIT2条、mmBERT README20条；实际逐份与trained原byte/SHA相同，排除准确notices集合后generated metadata检查通过。原失败保留，不能改写这些授权原件或全局关闭whitespace检查。第四恢复supervisor10971、`publication-recovery4-48-stage`，脚本SHA `45873aa4530a4db141ac419f6612c1b0f734216e06ab9d4e7d5a1c621cda1efe`，冻结11份来源notices准确集合/完整byte/SHA。约5GB实际对象与已观察带宽下，model push和完整LFS读回单项均900秒，payload2200/guardian2220秒，14:18截止保持。

用户随后将dataset主动改为private，model保持public。第四恢复的匿名dataset ref核对因此在training-proof阶段退出65，calls58，未model commit/push；真实凭据清理exit0，失败保留。匿名401/403属于私有仓库预期状态，不能据此重新公开用户数据或重复dataset push。private之后call59认证dataset ref确认真实d195，并审核切换前保存的完整snapshot；这不等于新的认证checkout完整读回。第五恢复按private dataset授权ref与public model普通push/匿名完整读回继续，任务收口还须新fresh认证dataset checkout/LFS全部byte/SHA验收并明确authentication=true。

第五恢复保存回执已显示model本地commit `7b9bd72ac952e427b4a2580c50d81c51585d3c57`生成，但model-push阶段失败：status1/calls68/elapsed8秒、真实凭据清理exit0。根因为 `moments.safetensors` 2341.81MB超过MoArk单对象上限2048MB。该本地commit未发布，失败和完整原件保留；新传输版本仅将moments分块，完整清单/分块与重组全文件SHA是独立门槛，原完整moments SHA `2cd5a601d63c658598e5717ce4c05cd26d1197a14212f82f3b47fba4bfb83426`、checkpoint和package身份不变。先核真实ref，保留旧失败commit到独立ref/工作区，再从确认远端ref生成可普通push的新传输版本，避免超限对象仍在待推祖先中；不能盲重commit、force或删除moments。

分块恢复tiny实际payload退出0，7项包括正确重组和6项错误SHA/顺序/重复/全文件SHA/大小/越界拒绝。原Windows guardian因根进程过快退出未获CIM身份，记录CleanupFailed/unknown1且Processes空，保留原FAIL。后续只读精确核PID75396（UTC05:59:47.9643498）、launcher9112（13:59:45.437872+08）与75396直接子进程均无当前记录；没有把历史unknown升级成已观测成功。核准确task/transport-tiny-fixture-v1路径、父链、23成员无链接后只删除该临时fixture，原stdout/identity/FAIL回执保持。后续audit SHA `57d6bd256de49c5f0a84b2828e576e814816e589021721f793ae85be8bc73f73`。

真实分块首stage `transport-personal-stage`、guardian13213在pre-push完整base SHA门槛拒绝仍为LFS pointer的原encoder/tokenizer；trained新完整权重正常，失败保留。主调度从已验root/model固定底座补齐相同原件，核新stage身份和完整SHA后再发布；这只是补齐根底座，不替换本run的新weights，原package/checkpoint身份保持。用户已明确续租至15:00，新stage按当前UTC07:00截止预算，原14:18快照保留。

用户最新发布合同为maikebing private内部预发布、IoTSharp public发布目标，正式名/slug `Sezika-Decision-Base`、版本 `v0.1-preview`。平台目标状态独立核实：个人model28817/repo50738359为private，认证metadata200、匿名metadata403/Git401；组织model28824/repo50759976已改名为正式slug且public，匿名metadata/Git200，初始master `c23c3c428beaece8a116c4d75ae51df2c97206d2`。旧组织实验slug404，创建第一次错误地比较numeric visibility1与string public导致的FAIL保留，独立调和确认只创建一次并改名一次。平台metadata、改名及初始ref不能代替模型字节push/完整读回，当前PREPARING_NOT_PUSHED。镜像绑定个人private模型授权认证完整验证commit，只复制完整base及本run，不复制private数据/derived、缓存、凭据或服务器任务根。组织正常push及独立匿名完整byte/SHA验收分别记，与个人模型认证验收分开；历史组织权限限制不能替代本轮实测。当前截止15:00，原14:18仅为历史快照。

组织模型介绍参考用户指定的模力方舟Decision Model分类页面，自己的完整中文模型卡及193字符短介绍保存于本轮任务 `iotsharp-model-card.md`，标题 `Sezika-Decision-Base v0.1-preview`，SHA `e4688c01565356caff45eee79344d43bb7ba9c044f0f5d41336538c96d1e0ba0`；[可复用模板](../templates/model-card.zh.md)保留实测/许可/消费边界。初版7c545原字节和旧出处保存为initial文件。官方页面直接web抓取InternalError，单次本机代理获取95,486bytes HTML应用壳（SHA `3960f70100c94ee6988c6bf216cdc4a31698fe9e5093d373063d2ff707ccb624`）；官方检索索引提供决策分类/选择/评分/布尔和上下文信息，仅借表达结构，模型数字来自自己的保存报告，不借平台Serverless能力、价格或其他模型性能。出处及工具回执存 `iotsharp-model-card-sources.json`。平台description和新根README使用自己的介绍，原base README/SHA256SUMS精确保留为BASE_MODEL_README.md/BASE_MODEL_SHA256SUMS；原18文件的original→physical路径映射及原SHA、新README/MODEL_CARD/总SHA清单分别验收。

## 最终发布与收口快照

个人private模型实际普通Git/LFS push及远端master为 `e2e5bc97d62dfbd4e2bbd070542324bb544e6b32`，独立fresh checkout HEAD同值；新weights与三个optimizer传输分块共4个LFS对象、约3.7GB上传完成。源pre-push完整验收18原文件/1,245,737,297bytes及33新文件/4,929,157,012bytes，状态 `PASS_complete_model_transport_original_optimizer_SHA`、`logical_reassembly_verified=true`。这验收源包，不能替代远端fresh认证全文件读回；后者在 `private-independent-readback` 阶段480秒超时，payload124、calls10、总791秒，完整byte/SHA和optimizer重组验收未完成。guardian终检完整且live/unknown/unchecked均0，`credentials_absent=true`、`configured_helper_absent=true`。旧超限失败提交 `7b9bd72ac952e427b4a2580c50d81c51585d3c57` 与运输恢复提交 `78024624da886175ae564923c639517fee86b0c4` 保留。实际发布硬化helper SHA为 `3f19b443f48c3d2df6e3c6d18ec1bd504f0db9c7477290dcec2d06ee8213c82f`；当前源码凭据安装脚本SHA为 `aff1a97c5ed02734d6338a2737f4231b0d2dda5e27c7c303ef0cee73e4aff0d0`，两者身份不同。

private数据实际commit仍为 `d19515802d31310b4050226b51ab85c60f130d42`。普通push及切private前完整匿名snapshot通过：原275资产/1,076,726,933bytes、新8文件/64,322,231bytes。当前private认证ref已确认，fresh认证完整读回 **NOT_RUN**；历史匿名snapshot不升级为当前认证验收。

组织仅介绍发布已通过，状态 **INTRODUCTION_PUBLISHED_MODEL_WEIGHTS_NOT_PUSHED**。单次native PUT README返回HTTP200，`put_count=1`、model/dataset weight bytes写入0，实际master `2a410d64a70955364439514cba60207b8b9ee796`。认证和独立匿名官方GET均HTTP200，完整README7585bytes/SHA `26d3ea93ca51e44da33d46ebdeeb8ab4456f55891b4e7e1182eaff81784d82d2` 一致；原7397bytes模型卡作为逐byte后缀保留。README明确介绍已公开、权重尚未发布、下载示例验收后启用；个人源模型仍private，目标model28824/repo50759976、title及public角色未变。该commit仅文档，不是model artifact commit；组织完整权重 **NOT_PUSHED**、完整匿名模型读回 **NOT_RUN**。后续镜像必须重新核ref并以 `2a410d64…` 为expected master，原 `c23c3c42…` 仅历史初始值。

最终小证据包 `publication-evidence-v3` 已本机安全解包和全部文件SHA验收：382文件/3,399,257bytes；tar4,085,760bytes、SHA `c83caeab91b58d05c744532457e50ba64c1fb8137a3cc747e8887dc4a94666d4`。manifest记录380原报告/3,297,173bytes，另两份metadata合为382；manifest为 `PARTIAL_pending`，个人完整读回、组织完整读回和private数据完整读回三份证明缺失。归档器的 `NOT_DETERMINED_by_archiver` 不替代本文件发布验收状态。服务器最终runtime UTC06:56:37核359注册行/332独立身份，332已退出或PID复用，无匹配存活/unknown，凭据RAM已清理；27份历史stage结果及首次审计项目数准入FAIL保留，精确小目录1024项目/40秒复验PASS。

LFS Range实际运输诊断取得两段各1,048,576bytes，HTTP206及准确Content-Range，host `s3.gitee.com`，2.397/1.610秒，约0.44/0.65MB/s；未保存Range字节到磁盘，仅运输诊断，不是完整模型读回。加速助手仍为draft、真实执行 **NOT_RUN**；本机修复和fixture不能写成实测支持。用户已确认本轮租期**已结束**，最后已确认截止为北京时间 **2026-10-11 15:00**；15:30续租建议已收口。本轮服务器工作结束，发布验收保持NOT_ACCEPTED，旧服务器磁盘产物的后续存续未复查；后续在新实例从已推送的private模型准确commit完整恢复并验收，接续组织发布，无需重复训练。

| 本轮门槛 | 当前状态 |
| --- | --- |
| 固定输入实际byte/SHA | PASS；数据pointer FAIL独立保留 |
| 5090 tiny全图数值/checkpoint恢复 | PASS_tiny_only |
| 真实mmBERT三步profile | PASS_real_base_profile_only |
| 新48步计划/profile | PASS_same_plan_real_base_profile |
| 48步固定训练与完整保存 | PASS_experimental_GPU_updates_saved_only；48/48 |
| 完整模型本机下载 | NOT_COMPLETED_user_selected_server_direct；两次partial保留 |
| 独立新进程128原题及68Choice换序验证 | PASS_new_package_GPU_experimental_validation_only；196次 |
| 本机小证据安全保存与报告独立验算 | PASS_small_evidence_and_saved_report_audit_only |
| 服务器RAM凭据范围/清理 | 原dummy3项、EOF修复11项和服务器真实Git dummy PASS；三次失败后真凭据清理exit0 |
| 真实dataset commit/push | PASS_normal_Git_LFS_push_ref；`d19515802d31310b4050226b51ab85c60f130d42` |
| dataset切换private前匿名完整snapshot | PASS历史；原275资产及本次8文件完整LFS/byte/SHA一致 |
| 当前private dataset认证ref/独立完整读回 | ref真实d195已确认；fresh认证完整读回NOT_RUN |
| maikebing private模型commit/push及认证完整读回 | 普通push/ref e2e5bc97通过；fresh完整认证读回TIMEOUT_480_seconds；发布未验收 |
| IoTSharp public Sezika-Decision-Base介绍 | PASS_README_only；commit 2a410d64；认证及独立匿名7585bytes/SHA一致 |
| IoTSharp模型权重及匿名完整读回 | NOT_PUSHED / NOT_RUN；private dataset不镜像；介绍commit不能替代权重 |
| 最终小证据及服务器进程/凭据收口 | PASS_small_evidence_SHA_and_current_process_closure_only；历史FAIL保留 |
| 完整发布验收 | NOT_ACCEPTED；PARTIAL_MODEL_PUSHED_READBACK_PENDING |
| 正式质量、概率校准、新权重AOT/性能 | NOT_ACCEPTED |

精确指标、回执路径与SHA见[机器可读证据](repository-training-real-2026-10-11.json)。本轮训练完成后的模型和实际派生数据回推已获用户授权；源码push未授权。并行智能体只处理各自文件与准备工作，GPU训练由唯一主调度执行。
