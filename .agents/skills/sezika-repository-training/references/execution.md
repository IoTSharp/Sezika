# 命令与执行顺序

先读methods.md。所有Windows外部命令放在 `tools/rental-training/Invoke-BoundedProcess.ps1` 下；PowerShell7、明确秒数/取消/进度/任务PID身份。新建目录，失败目录和历史回执不覆盖。

## 当前全量入口与仓库交付

先在已有 private 数据仓库固定准确旧 ref，将完整已转换/准入 corpus 原407文件（353分片）保存到 `derived/<run-id>/corpus/`。保留 manifest、bindings、长度扫描、排除账本、policy、notices及全部分片的原 bytes/SHA，不重转换 raw，不规范化换行。只 stage 本任务目录及审核过的精确 LFS/`-text`属性，正常 commit/push，再以独立认证 fresh checkout 和独立 LFS 读回全部新文件 byte/SHA，记录准确 PublishedDatasetCommit。源 DatasetCommit仍为 `d19515802d31310b4050226b51ab85c60f130d42`；新发布身份另存交付绑定，不能改写原 provenance。

服务器取得新授权实例/有效截止后，skip-smudge克隆准确 PublishedDatasetCommit，只对 `derived/<run-id>/corpus/` 的明确路径执行有界标准 Git LFS pull，核完整清单与实际 SHA，再将 `CorpusRoot` 指向该入口。使用 `remote/pull-full-corpus.sh pull <fresh-root> <实际发布40hex提交> <trusted-413-files.tsv> <seconds>`；trusted TSV随可信工具小输入交付，SHA固定，数据仍从仓库读取。旁车 `training-inputs/lengths.json` 是训练器必需的CorpusLengthReceipt，与 `corpus/length-scan-complete.json` 分别保存，不能混称或漏交付。原 `clone-inputs.sh` 全仓拉取及 raw `prepare` 不作为此默认路径；不要为复用旧 helper重新转换或抽样。本轮 private 数据发布已授权，模型/public发布未授权。旧租期已结束且服务器已暂停，不能复用旧期限。

离线交付准备命令（由 bounded wrapper 执行；spec绑定原来源身份、已验corpus及新实例实际路径）：

```powershell
& tools/repository-training/Prepare-FullCorpusDelivery.ps1 -SpecificationPath <fixed-spec.json> -OutputDirectory <fresh-delivery> -MaximumFiles 8192 -MaximumBytes 137438953472 -MaximumSeconds 900 -CancelFile <task-cancel>
```

helper输出的 `corpus-run-plan.pending.json` 只有绑定真实新实例/profile后才成为执行计划；离线 `LocalDeliveryReady=true` 不等于 `TrainingReady=true`。计划使用 `CorpusRunPlan`，固定工具 `d925f1c51dfe172e6f5b4ccb97408a5281440bf6`（另核50项工程源码 SHA）、模型 `e2e5bc97d62dfbd4e2bbd070542324bb544e6b32`、`TrainRecords=14592720`、`Epochs=1`、`MaximumTokens=8192`；17来源及development1619168保留原隔离。所有路径/SHA、seed/超参/PTX与计划字节在执行前冻结。

`PublishedDatasetCommit` 是额外的交付/读取绑定；当前准备器与 `CorpusRunPlan` 没有这个独立字段，必须另存经核验的发布清单与readback回执。spec/plan的 `DatasetCommit` 保留原来源d195身份，不能用新发布提交覆盖它或据此冒称准备器已经验证发布。服务器checkout提交与该额外交付绑定一致后，再按原 corpus身份核SHA。

当前 `tools/Sezika.RepositoryTrainer/Program.cs` 的真实 CLI（Linux入口 `Sezika.RepositoryTrainer/Sezika.RepositoryTrainer`）：

```text
profile-corpus --plan <fixed-plan.json> --output <fresh-profile> --gpu-uuid <actual32hex> --timeout-seconds <bounded-seconds> --cancel-file <task-cancel>
train-corpus --plan <same-plan.json> --profile <same-instance-profile>/profile.json --output <fresh-stage> --stage-updates <positive-updates-within-remaining-global> --gpu-uuid <actual32hex> --timeout-seconds <bounded-seconds> --cancel-file <task-cancel>
train-corpus --plan <same-plan.json> --profile <same-instance-profile>/profile.json --resume <complete-checkpoint-directory> --output <fresh-stage> --stage-updates <positive-updates-within-remaining-global> --gpu-uuid <actual32hex> --timeout-seconds <bounded-seconds> --cancel-file <task-cancel>
validate-corpus --plan <same-plan.json> --package <completed-global-package> --package-sha <corpus-package.json-sha> --output <fresh-validation.json> --gpu-uuid <actual32hex> --timeout-seconds <bounded-seconds> --cancel-file <task-cancel>
```

`--stage-updates` 实际范围1..800000000，且不能超过固定全局剩余更新；`--timeout-seconds`实际范围1..86400，外层必须按新租期收窄并预留保存/下载/验收。阶段只推进同一plan及AdamW/global cursor，不能把48步或百万条上限称全量。新GPU重新profile，跨实例显式`--gpu-uuid`且不改原plan；旧d925f1c5工具只准RTX5090/至少30GiB/CUDA Driver>=12080。本轮候选工具另允许精确RTX4090/CC8.9/至少30GiB/Driver>=12080，仍要求UUID与当轮完整8192形状profile，并以gpu-admission.json绑定设备名、CC、driver、总/空闲显存、owned上限及profile SHA；重新冻结源码和工具后才可测试，福海稳定性失败、完整前反向NOT_RUN。禁止绕过门槛或CPU正式训练。

完整checkpoint四文件为 `weights.safetensors`、`moments.safetensors`、`checkpoint.json`、`corpus-cursor.json`，最后cursor是完成标记；缺任一文件或SHA/plan/global cursor不符均不可恢复。全量完成需global14592720、所有来源EOF、`FullCorpusEpochCoverage=true`和最终完整checkpoint；阶段成功保留实际覆盖与未完成状态。`validate-corpus`只接受全局完成包并做独立子集GPU前向，不是全量测试/质量通过。

整轮保守更新保存估算用新实测 `4 × MaximumStepSeconds × 剩余全局更新 + 600 + 120 × 保存次数`，另预算GPU验证和完整保存读回。不足时提前向用户报续租需求，不自行购买、降低全局分母或沿用已过期14:30期限；限时先保存完整断点并报告剩余数据。GPU并发1，进程/临时路径归属、guardian、取消、进度与原FAIL保留规则继续适用。

## 历史2026-10-11执行合同

以下所有“本轮/当前”的实例、17615/48步、三文件checkpoint与发布授权仅为历史记录；当前全量默认及权限以本文件上方为准。`New-TrainingTask.ps1` / `prepare --max-records`（最多百万条）、旧 `profile/train/validate` 及215题合同不能替代当前完整corpus入口。

## 准备入口

```powershell
& tools/repository-training/New-TrainingTask.ps1 -RunId <new-id>
& tools/repository-training/New-ToolBundle.ps1 -TaskDirectory <new-task>
```

工具包是Linux x64自包含.NET10，含RepositoryTrainer、FullTrainingChecks、PTX/ABI、clone/pack脚本、guardian；`files.sha256`覆盖全文件，`engineering-sources.json`分开准确source commit和未提交工程字节SHA。服务器克隆固定源码后使用这个冻结工具包，不能声称远端源码commit已经包含新代码。历史guardian1520秒上限不改，包内只替换单个受审字面量至86400；每阶段仍设具体较小预算，不能默认运行一天。

RepositoryTrainer（Linux入口 `Sezika.RepositoryTrainer/Sezika.RepositoryTrainer`）：

```text
prepare --dataset <checkout> --output <fresh-derived-raw> --dataset-commit <40hex> --max-records 100000 --timeout-seconds 180
lengths --data <derived-raw> --tokenizer <base>/tokenizer/tokenizer.json --output <fresh-lengths-json> --max-tokens 8192 --timeout-seconds 600
admit-lengths --data <derived-raw> --lengths <lengths-json> --output <fresh-admitted> --timeout-seconds 60
attach-notices --dataset <checkout> --data <existing-derived> --timeout-seconds 30
storage-check --output <fresh-tiny> --timeout-seconds 120
```

prepare先 `--pilot true --max-records 2`，再全量。lengths拒绝时返回3，保留FAIL；admit另建派生版本、账本明确排除，不裁切原文；对admitted再次lengths确认全部分母。转换/许可/hash/tokenizer在CPU，不进行encoder计算。数值检查 `Sezika.FullTrainingChecks --checks 4 --output <fresh-json>`，内部120秒/外部150秒，最多1600坐标；storage-check为tiny GPU保存/读回/resume。正式模型训练只在新服务器。

prepare自动附实际来源README和rights；admit保留这两个文件。旧派生版本可一次性attach-notices，要求相同source/revision/SHA且两个目标文件都不存在，不能覆盖旧清单、records、ledger。完整模型包还保留底座实际LICENSE-COMPONENTS、license-provenance、全部指定notices及代码LICENSE/NOTICE；整体许可unknown，不能把encoder MIT或数据Apache声明套给tokenizer与新输出。

run-plan必须绑定服务器当轮实际派生manifest SHA。当前JSONL使用平台换行，Windows准备物与Linux输出的records/ledger SHA可能不同；若发生差异，保留准入失败，按逐行内容转换LF核整文件SHA确认是否仅换行，不能直接套用本机SHA或绕过检查。最终模型与数据发布携带同一实际Linux字节版本，原始数据不改写。

## 新服务器

只读核本轮连接/指纹/新租期/boot ID、GPU UUID/driver/显存/现有compute使用者、CPU/RAM/磁盘。目标CUDA12.8、RTX5090 32GB、CPU25核、RAM48GB、系统盘30GB、数据盘50GB。Toolkit版本不是实际driver；CLI核DriverVersion>=12080、RTX5090/至少30GiB及32hex UUID，再经真实tiny/profile验收。官方兼容性资料为NVIDIA CUDA Blackwell Compatibility Guide，文档不能替代实跑。

所有模型/缓存/工作区/checkpoint放实际数据盘；clone最低12GiB free，完整阶段另预算约2.3GB输入、工具、最多两份checkpoint（约3.68GB/份）、最终权重、归档及传输留量。不能把50GB当全部空闲。工具发现限command-v/明确路径，核bash/git/git-lfs/timeout/realpath/df/sha256sum/tar/flock/setsid及运行依赖；需要安装时限定必要可信工具，不扫描大目录。

精简Ubuntu镜像可能缺少自包含.NET工具仍需的ICU。先执行工具help检查运行依赖；实际报告missing-libicu时，核apt-cache和dpkg，在有界guardian下仅安装官方源所需包（Ubuntu24.04本轮为libicu74）。保留默认Unicode行为，不擅自切换Globalization.Invariant来绕过多语言运行依赖。记录包版本、来源、库的准确Linux路径及原始失败，新建stage重验。

容器显示的nproc及宿主根盘不等于租用配额。本轮nproc=384，但cpu.max为2500000/100000，即25核；memory.max=51539607552bytes。按实际cgroup配置DOTNET_PROCESSOR_COUNT和并发，使用独立/data的真实剩余容量；不要把宿主挂载空间当可用租盘。Windows归档使用已验证的系统路径 `C:\Windows\System32\tar.exe`，避免Git附带tar对Windows驱动器路径的行为差异。

1. 上传工具包、核全部SHA。新stage先guardian `/usr/bin/true`：TSV的completed/payload_exit0、final_check_complete1、cleanup_incomplete0、live/unknown/unchecked0必须成立。
2. guardian下 `clone-inputs.sh <root> <run-id> <source-commit> <data-commit> <model-commit> <seconds>`，新根如 `<数据盘>/sezika-repository-<id>`。固定三个checkout，按当前visibility进行标准Git/LFS pull/fsck后再核实际清单字节/SHA；本轮数据集和maikebing内部预发布模型均已转private，先验证受保护授权helper的真实认证clone/LFS路径并冻结工具包，IoTSharp public模型独立匿名读取。旧公开clone证据不自动证明private路径可用；不执行数据仓库loader。
3. 上述数据命令、checks4、storage-check。原始raw保持不变。填run-plan.template实际Linux路径、admitted manifest SHA、GPU UUID，核分母，冻结计划字节。
4. `profile --plan <json> --output <fresh-profile> --timeout-seconds 1800`：真实底座3更新（同一训练题两次+最长完整训练序列），第二次必须核encoder梯度非零，再核最长frame、forward/backward/update和耗时。如果dev需要更长frame，只追加训练ID的EOS作明确合成shape诊断，不让dev标签进入反向。其权重独立，不继续作正式初始化。
5. `train --plan <same-json> --profile <profile>/profile.json --output <fresh-trained> --timeout-seconds <bounded-seconds>`：同计划/data/order/PTX/GPU，零head起点。CLI以profile最大step×4加该训练阶段保存预算拒绝明显不足的阶段；独立validate及下载/回推在外层另留租期预算，不能重复计入一个不执行validate的train阶段。估算不是已测worst-case保证。不能自动延后期限；若用户明确改变训练步数，新建计划并重新profile，保留旧计划与回执。主调度GPU/SSH并发1，其他compute使用者存在时不启动，不停止别人。

guardian形式：`/bin/bash <bundle>/guardian.sh <fresh-stage> <work-seconds> 8 0 file none -- <payload> <structured-args>`。长阶段SSH派发后按有界backoff读progress/TSV；控制端blocking调用不超过60秒。Bash只用单引号PowerShell here-string，由Invoke-Ssh.ps1通过stdin wrapper发送。单项成功必须含真实payload与完整资源回收，不能只看Windows0。

训练阶段估算使用 `4 × profile.MaximumStepSeconds × 剩余updates + 600 + 120 × (1 + floor(剩余updates / CheckpointEvery))`。仅估算train实际执行的更新和保存；独立验证、归档、下载、回推及匿名读回另占外层预算。2026-10-11初次profile下48步估算3332.114秒，固定17615步无法在两小时租期内准入。这是保守准入估算，不能作为实测整轮耗时或性能声明。新计划profile测得的值才是本次启动依据；若剩余时间不足，先报告所需续租时间并等待新明确截止，不默认降低步数或放宽余量。

## 保存、运输路径、独立验收

checkpoint三文件：weights.safetensors、moments.safetensors、checkpoint.json；后者仅在权重/moments写入及全文件SHA读回后生成。含全部encoder/head FP32、Adam两份moments、updates、plan/data/order/PTX身份。只保留本任务最新两份完整checkpoint；旧文件核runID/SHA/固定三路径后精确清理。

恢复 `train ... --resume <complete-checkpoint>`，同一计划、新output，保持cursor/order/换序位。超参/seed/数据/PTX变化拒绝，不手改checkpoint。取消/失败保留partial和最后完整checkpoint；poisoned状态不导出成功包。

按当轮用户选择确定本机完整下载或服务器直接发布。用户选择服务器直传时：服务器完整包保存/SHA→独立GPU验证→服务器dataset先提交/push/ref读回→模型绑定真实dataset commit后提交/push→独立新checkout/LFS完整byte/SHA读回。读回按visibility执行，本轮private数据集和maikebing private内部预发布模型用授权认证，IoTSharp public模型匿名。本机仅保存小报告、manifest、日志和验收证据；不把它记成完整模型下载。本轮后来明确选此路径，两个本机SCP中断和partial均保留。服务器发布与独立readback的共存空间另预算，不能仅沿用下载的2×tar公式。

服务器Git写入使用本轮已授权的owned路径和正常LFS basic/pre-push；凭据不进入URL、argv、仓库配置或日志，只经受保护stdin/进程内存传递。需要RAM文件时核真实tmpfs、准确任务专属目录与权限，finally按归属清理；public匿名读回新进程不带凭据，private认证读回在独立新checkout使用授权helper并明确authentication=true，结束后清理。Bash或系统工具只负责Git、hash和资产调度，C#模型、训练及GPU算子约束不变。下面归档/SCP仅适用于本机完整保存路径。

服务器直传仍要保存本机小证据包：模型/package/checkpoint身份、训练/profile/validation报告、派生manifest、guardian回执及发布/匿名读回结果。小包同样安全解包并逐文件SHA；清单排除其自身，由归档SHA另行绑定。本轮v1生成清单时shell重定向先创建files.sha256，通配符把空清单自身纳入，实际解包拒绝；保留原FAIL，新v2明确排除清单后通过。保存的验证报告可在本机独立重算分布/CE/argmax、分母和候选ID换序，但它是报告验算，不能升级为本机真实推理或完整模型下载。

保存后先 `pack-result.sh <root> trained <seconds>`，本机 `Download-RepositoryRun.ps1 -RunId <id> -TaskDirectory <task> -RemoteRoot <root>` 下载并由Expand-RepositoryArchive核tar SHA、唯一regular文件/安全路径/总16GiB与1000文件界、完整逐文件SHA；新encoder不能用旧原件补齐。归档存在时先只读核，再分别继续解包，不盲目重打包。

完整checkpoint和两份Adam moments使输出包明显大于encoder输入。先以实际归档bytes和观测传输速率检查单项SCP上限是否足够，再在总租期内调整有界传输时限；不能因为默认600秒过小而漏掉模型或无限重试。下载中断先检查远端归档身份和本地partial，保留失败回执；仅处理本任务明确命名的归档，不清理共享缓存。

下载前同时检查控制端卷，最低为 `2 × 实际归档bytes + 1GiB`（归档与解包共存），后续发布checkout/LFS缓存及匿名读回另留空间。助手先probe实际tar bytes/SHA后准入。`-ArchiveTimeoutSeconds` 单项30..1500秒，默认600；总下载1800秒和实际截止仍有效，单项扩展不延长租期。可用 `-ArtifactDirectory` 指定新的本轮专属目录，其leaf必须严格等于RunId、目标尚不存在、父链无链接。结果 `model-download.json` 留在原任务，产物保存目录另记完整绝对路径。2026-10-11本轮D盘仅约9.1GB，约4.929GB归档加解包已不足，核owned SCP完整身份后停止并保留546,275,328bytes partial/Failed回执，改存C盘的新专属目录；不能将中断或重启当下载通过。

独立新进程：`validate --plan <same-json> --package <trained> --package-sha <package.json-sha> --output <fresh-validation-json> --timeout-seconds <budget>`。从新完整权重加载并重新GPU前向，固定64train+64development与全部Choice左移，逐题保存soft-target CE、概率、预测ID/teacher argmax ID；PID/start与训练独立。当前是实验子集，不是正式质量、校准、全量测试或新权重AOT/性能。

## 回推

当前请求已授权本轮完成后的模型与变更数据push；准备/fixture阶段不写真实仓库。实际远端master准确commit的新checkout，index干净且无无关本地commit；模型路径runs/id，派生数据derived/id。普通Git LFS basic、单并发、正常pre-push；凭据只在安全存储/任务内存，不能进URL/argv/日志。

New-PublicationManifest参数：RepositoryDirectory、Kind(dataset/model)、RunId、TaskDirectory、ValidationReceipt、PublishedDatasetCommit；dataset还需ModelPackageDirectory，将派生manifest SHA绑定已验证模型输入。先dataset生成清单→Publish-RepositoryRun -PreviewOnly→复核具体清单→本轮授权普通commit/push→实际ref读回。无数据变更跳过。

model使用新实际dataset commit生成publication-binding.json，保留训练原source commit、derived SHA，再preview/普通push。每kind独立SHA-bound validation gate。large逐路径追加LFS，small追加 `-text` 保持字节；原.gitattributes前缀原字节保持。只stage owned paths和经前后SHA审阅的属性文件。

保留来源许可证/rights原始字节时，文件可能是CRLF。提交前按单次Git调用使用 `-c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol` 执行 `diff --cached --check`，识别CR为行尾，同时继续拒绝真正尾空格/EOF空行等错误；不改全局配置或规范化原件来让检查通过。2026-10-11首次服务器发布在619bytes的source-rights.json上把CR误报为尾空白，未commit/push；原FAIL保留。修复先以tiny fixture确认CRLF原bytes/SHA可通过且真尾空格仍拒绝，再核任务旧HEAD/origin/index/owned-path/全量SHA，从准确失败阶段恢复，不能盲目重跑整个push。

上游原始许可证/notices也可能自带真正尾空格，不能为了diff检查而改写授权原件。将固定来源notices作为显式、准确路径集合：逐份与训练包原来源byte/SHA比对，相同集合和字节才可通过这个保真门槛；其余本次生成metadata仍做CRLF-aware `diff --cached --check`并拒绝尾空格。不能按整个目录或glob宽泛豁免、关闭全局whitespace检查、把generated metadata放入notices名单。本轮第三恢复在model-staged-check退出2，四份已保真原notices存在既有尾空格；新恢复冻结11份原notices准确集合和完整byte/SHA，同时重新核所有generated metadata。原FAIL保留，修复先以小fixture证明原件可接受且生成文件的真尾空格仍拒绝。

服务器临时Git凭据助手使用 `Install-RemoteGitCredentials.ps1`；正式注入前必须以dummy运行真实 `git credential fill`，覆盖dataset和model/LFS路径、最后字段后EOF、没有结尾换行的EOF，以及host/path/重复字段/大小/读超时拒绝。Git调用helper可在字段后直接关闭stdin，不保证再给空行；助手只在有界输入结束、字段完整且范围匹配后输出凭据，空行和EOF都须正确处理。本轮原helper未接受EOF，已生成dataset提交后push因Username unavailable退出128；保留失败，修复后的11个正负dummy及服务器实际Git探针通过。dummy成功本身不代表真实发布成功。

push失败且已经生成本次commit时，先按当前visibility只读核真实远端ref，复核保存commit的父commit、空index、owned工作树、提交路径集合和完整资产SHA。符合原合同才普通push已有commit，不重commit、不force；若实际远端已经指向该commit，继续独立读回而不重复写。原raw仓库已知的宽泛LFS属性/普通Git blob差异会使全仓worktree diff报错；本次owned范围的检查可明确限定 `.gitattributes` 与 `derived/<id>/` / `runs/<id>/`，同时必须保留原pointer FAIL，并在最终独立checkout复核全部原资产SHA及新产物SHA，不能借限定范围跳过原数据验收。

最后新checkout/LFS完整逐文件byte/SHA读回，`-c core.autocrlf=false`；private用当轮授权认证，public用匿名，并分别记录visibility/authentication。ref成功、LFS登记、完整授权下载、完整公开下载分开记录。用户切换private后匿名401/403为预期权限状态；不要重新公开，不要重复push；保留原失败与切换前完整snapshot，再认证核最新ref并在新checkout完整读回。push失败可能已写远端，先只读核ref/LFS，不自动重复；只读ls-remote可单次本机proxy fallback。

可选组织发布须当轮显式授权。本轮用户最新合同为maikebing private内部预发布、IoTSharp public发布目标，正式名/slug `Sezika-Decision-Base`，产物版本 `v0.1-preview`且质量仍experimental；private dataset不镜像。先读取实际目标仓库URL/ref/public可见性及当前账号写入和LFS权限；旧组织失败只作为历史回执。复制范围绑定个人private模型授权认证完整验证commit的完整base及本run准确路径集合/SHA，不从服务器任务根或private数据checkout宽泛复制。保留目标已有非owned路径；在独立组织checkout追加/提交本次可审查路径，正常LFS basic/pre-push及普通push，核真实ref，再独立匿名fresh checkout全部base/run文件byte/SHA。个人模型认证验收和组织模型匿名验收分别记录，不以个人成功替代组织成功；任务预算包含认证模型读回、组织上传及组织匿名读回，以当轮deadline.json及用户明确的新绝对期限为准。本轮原14:18已由用户明确续租至15:00（UTC07:00），原快照保留，智能体未自行购买。

平台LFS单对象大小也须在push前准入。本轮MoArk拒绝 `moments.safetensors` 的2341.81MB对象，平台提示上限2048MB；已生成但未发布的7b9bd72提交和失败回执保留。只在传输层做有界分块，清单固定准确原路径、全文件bytes/SHA、每块顺序/bytes/SHA及安全相对路径；下载新checkout全部分块后按顺序在新任务路径重组，最终原全文件SHA `2cd5a601d63c658598e5717ce4c05cd26d1197a14212f82f3b47fba4bfb83426` 通过后才接受checkpoint/resume。保留原checkpoint.json和package身份，不能删moments、改receipt或以分块SHA代替完整文件SHA。新传输版本先核真实远端ref，从该确认ref生成正常可审查提交；把超限旧commit保留在独立失败ref/工作区，不能让它仍作为新待推分支祖先造成LFS继续上传超限对象，不force或改写已发布历史。

新发布checkout以skip-smudge克隆时，base encoder/tokenizer可能仍是pointer。推送前必须对完整base和本run分别核真实byte/SHA；本轮transport-personal-stage/PID13213正确拒绝两项pointer，trained新权重原件正常，失败保留。只从已验固定底座checkout核准的同SHA原件补齐base，再新stage复验；不能用旧底座encoder替换本run新weights。每种资产核自己的manifest身份，clone成功或新run SHA通过不能替代底座完整验收。

组织介绍使用自己的模型卡，可参考[项目模板](../../../../docs/templates/model-card.zh.md)与平台Decision Model分类表达结构；部署、上下文配置、实际196前向分母、48步范围、校准/质量/AOT和组件许可分别写实。组织根README展示新介绍时，原base README/SHA256SUMS不能覆盖后仍声称原18路径SHA完全一致：准确保留为BASE_MODEL_README.md/BASE_MODEL_SHA256SUMS，并在镜像清单记录original→physical映射及原SHA；新README/MODEL_CARD/实际新SHA256SUMS单独生成与完整验收。

Test-RepositoryWorkflow仅新专用fixture，-LocalFixture强制该任务内file:// bare目的地；mock验证带fixture标记。测试preview、改字节拒绝、普通commit/push/ref，不能冒称真实MoArk LFS、GPU质量或发布通过。

## 本轮最终继续点

本节保留租期结束时的历史继续点；2026-10-11后续已在本机正常Git/LFS完整读回private模型/数据并实际恢复optimizer。最新门槛见[本机接续证据](../../../../docs/evidence/repository-training-local-mirror-2026-10-11.md)。剩余仓库镜像可走[本机流程](local-mirror.md)，无需GPU或新租期。

2026-10-11最终状态 `PARTIAL_MODEL_PUSHED_READBACK_PENDING`、发布验收 `NOT_ACCEPTED`。个人private模型普通push/master和fresh HEAD为 `e2e5bc97d62dfbd4e2bbd070542324bb544e6b32`，源完整18原文件/33新文件SHA及optimizer逻辑重组通过；fresh认证完整LFS/byte/SHA与重组读回480秒超时，payload124，不能置PASS。dataset实际d19515802已push，切private前原275资产/新8文件完整匿名snapshot保留，当前fresh认证完整读回NOT_RUN。不得为恢复读回重复训练、force或盲目重复push。

组织介绍已通过单次README native PUT发布，实际master `2a410d64a70955364439514cba60207b8b9ee796`，认证和独立匿名官方GET完整7585bytes/SHA一致。状态 `INTRODUCTION_PUBLISHED_MODEL_WEIGHTS_NOT_PUSHED`；权重NOT_PUSHED，完整匿名模型读回NOT_RUN。该文档commit不是model artifact commit。新镜像必须重新核ref并以2a410d64为expected master，不能沿用c23c3c42初始ref。先完成源private模型fresh认证完整读回/完整optimizer重组，再复制准确owned模型资产、正常组织push和独立匿名全文件验收。

Range运输诊断仅两段各1MiB、HTTP206/准确Content-Range、约0.44/0.65MB/s，未落盘Range字节；加速助手仍draft、实机NOT_RUN。本机fixture及read1修复不能升级实测。最终小包382文件SHA和服务器332进程身份/凭据RAM收口通过，保留原FAIL；完整模型未下载本机，正式质量/校准/新权重AOT及性能仍NOT_ACCEPTED。用户已确认本轮租期结束，最后已确认deadline为北京时间2026-10-11 15:00，15:30续租建议已收口。后续取得新实例和有效期限，从已推送的private模型准确commit恢复并完整认证验收、重组optimizer，随后补齐private数据读回及组织镜像；不再连接旧实例或为发布重新训练。
