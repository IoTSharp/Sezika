# T3 controller / bundle / resident 独立候选准备（2026-10-14）

本轮仅生成独立候选，状态为 PREPARED_NOT_BUILT_NOT_RUN。已固定精确HEAD、8份现有未提交T3源码和3份T2b依赖，并准备候选级共享接线与Windows/Linux tiny/full计划。11项bundle、14项controller、8项resident均来自实际源码计数，尚未运行；没有SDK构建、生产resident或GPU执行。

**E1：准确基线与不可变输入**

主集成者给定且本轮核对的HEAD为 `20f769f4c7493acddb41db5638e11b73080a4b76`。新候选根为 `C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\controller-candidate-v1`，源码在其 `source`，最终清单为 `source-manifest.json`。

只读 `git archive` 固定该commit和9个准确范围：Directory.Build.props、global.json、README.md、LICENSE、NOTICE、THIRD_PARTY_NOTICES.md、tools/Sezika.Tooling、tests/Sezika.Tooling.Tests、tools/Sezika.ProcessFixture。没有分支/worktree/index写入。执行通过 native-audit-candidate-v1/aot-win/Sezika.Tooling.exe 的有界process run，git进程RootPid83512、launcher26164；原始回执为git-archive-result.json，PASS / exit0 / 完整输入输出 / cleanupPASS / remaining0。

HEAD归档为808960字节，SHA `1bb6453a65d5b0c3264a9afb83483d249f8e27a378f7dc14b70fc50e1e3ae779`。独立有界解包得到62文件751923字节，256条目/30秒、单文件4MiB/总64MiB上限，拒绝链接、设备及逃逸路径。8份迁移源码和3份Git依赖叠加后，source含73文件887825字节。4份候选共享文件另作明确增量，原HEAD副本完整保留。

| inherited T3源码 | SHA256 |
| --- | --- |
| `Tasks/BundleBuild.cs` | `c9814a63a4e417421eae50c11f997ab34d2707ed398d552aa493581b2fd32271` |
| `Tasks/BundleInputCoverage.cs` | `27008917f44a81dc698b04bf20bd52271c28e093862320e95fa2e2f1d660ad2a` |
| `Tasks/ResidentController.cs` | `f42b0908d29009843db3e1557e6276b4afec2e551c6b989ac254a31c2a99786f` |
| `ControllerReviewChecks.cs` | `0b16acddcb2dd1c81367bfeb3e6d805f0bd217f07a4c4b726be88fed0ab72fd2` |
| `BundleInputChecks.cs` | `8774818d01369dc1a92147d4dfea5cadc39e62f7ab5a177c5362987811547dcd` |
| `ResidentChecks.cs` | `289cbee199c91adcc5cb2835a7ae1082f25048d5057f03dd2a366e0590d6ddb0` |
| `ControllerFixture/Program.cs` | `a9e5240ffcaac34a1337e9c3c19a80509e748e19b6d6a3caa85c51e2590b4e81` |
| `ControllerFixture.csproj` | `f6376f27bbe594f6017bf93762383c82f4c6bcf71a2f6e29b8a7487b095548da` |

上述原canonical源码没有修改。复制前后与固定SHA一致；它们是继承版本，不因以前某次回执成功而自动成为本候选通过。

**F1：Controller14依赖T2b三源**

ControllerReviewChecks直接调用NativeGitHook.VerifyDirectory及NativeLfsFiles的canonical pointer、local store方法。NativeGitHook的完整类另引用NativeGit.ValidateRemote；不能剪类或塞stub使候选表面可编译。主集成者明确允许在原16个逻辑源码范围外只增加NativeGit.cs，最终审阅17个逻辑源码路径。

完整依赖来自冻结git-candidate-v2/source，对照canonical三源当时逐SHA一致：NativeGit.cs `4d9f5a7052ff4a67b0718550c2839dcc960fb0452630e0ef0357e2babf3e2f19`、NativeGitHook.cs `5d535536549d18963d8f52bc528e5c281366e530569d6ade10b1fda6b3efcc40`、NativeLfsFiles.cs `d8e5e7770db6e11b5a5d7d44cd732d0bd462b0868c5b9246b5efcc4439510c41`。

Controller14仅做本地合同/字节检查，不运行Git/LFS网络。但正式共享测试提交必须依赖T2b三源已被接受并进入相同可编译基线，不能抢先提交HEAD缺依赖的测试。

**F2：准确候选共享接线**

candidate-shared-partial.patch相对固定HEAD，只改候选中的MigrationCommands.cs、Tooling.csproj、Tests Program、Tests.csproj。canonical共享入口没有改动；主集成者应语义合并增量，保留现有Validation/retirement/Git等接线，不可整文件覆盖。

- 新命令为bundle inputs、bundle build、task resident，使用BundleBuildJson与ResidentJsonContext。
- Save显式读取BundleInputReceipt、BundleReceipt、ResidentReceipt状态，PARTIAL resident返回1；不改变已有合同的真实训练证据等级。
- Tooling增加ControllerFixture友元；Tests以ReferenceOutputAssembly=false引用单独C#fixture项目。
- 新suite为bundle-inputs、controller-review、resident。resident测试驱动180秒、外层执行210秒、cleanup10秒；生产resident预算和方法没有修改。

生产CLI只接RunAsync；内部RunSimulationAsync保持测试入口。未添加脚本、解释器、远端命令或新的模型执行器。

**E2：验收数量、fixture和环境**

源码实际计数为11/14/8，Windows/Linux各suite均准备tiny1与完整请求，共12份，全部NOT_RUN。bundle11使用明确synthetic工程/ABI字节，验证完整输入、变更、路径、复制和取消；它不执行SDK或PTX。controller14验证synthetic checkpoint/schedule、STOP预检、冻结源码和本地LFS字节。resident8通过单独C#CPUfixture检验真实进程、prepared合同、四文件模拟保存/恢复、错误输出和STOP/CANCEL，不能称真实模型、optimizer或GPU。

resident显式环境为SEZIKA_CONTROLLER_FIXTURE与SEZIKA_PROCESS_BROKER，必须分别绑定同一候选的真实fixture/Tooling apphost、DLL及runtime字节。请求里的程序路径只是未来布局，尚未构建或核身份，不能沿用旧binary充数。Linux内部进程的出生/boot/argv/父链/EOF/cleanup须独立于WSL外层验证。原始失败及其分母保留。

**P1：Task-only SDK bundle计划**

本轮磁盘复核C free93000843264字节、D free48487800832字节，计划输出只在C候选根内。执行前再核至少8GiB可用及实际完整publish/暂存空间。准备开始时Windows SDK恢复授权待答；其后主集成者已获授权并在私有目录恢复，完成状态仍待其独立回执。Ubuntu构建槽由executor独占；本轮未构建、安装或扩大工具发现。

范围只含Tooling和ControllerFixture，不含RepositoryTrainer、模型、CUDA/PTX或GPU验收。task-only-bundle-inputs-request.json对这份稳定快照的完整工程输入进行准入，尚未运行。其后由主集成者从不可变快照作真正native SDK publish，使用source外的--artifacts-path、单编译器与新输出，核完整apphost+DLL+deps/runtimeconfig和许可bytes/SHA，再分别冻结包、打包及独立解包读回。

自动BundleBuild.BuildAsync另要求SourceRoot里git rev-parse HEAD前后等于SourceCommit。归档快照没有自己的.git；本轮不伪造Git身份或helper。主集成者须在最终源码静止窗口另绑定真实GitSourceRoot，或先独立审阅archive-bound输入设计。直接SDK publish/freeze与自动bundle build整链是不同门槛，当前都NOT_RUN。

现阶段不从正在改动的canonical目录生成新的正式engineering manifest，也不将task-only输入覆盖说成完整训练SDK包。完整源/包/PTX与实际GPU各有独立门槛。

**仍未闭环**

构建与11/14/8检查；真实SDK发布及完整runtime/package读回；自动bundle build的Git源身份；最终T2b/T3共享提交依赖与canonical接线；适用Windows/Linux/AOT门槛；生产resident、GPU、数值、训练、语言质量、校准、性能和部署。全部保持NOT_RUN或待主集成者核验。

最后对归档执行器登记身份作有界Windows OS读回，见archive-process-readback.json；没有发送信号、按名称停进程或清除任何旧目录。这个读回不冒称另外执行了NativeAOT audit。保留的归档、source、shared-before和计划均为交付物，不作临时垃圾删除。

最新策略：主集成者转达用户要求不锁.NET SDK，canonical global.json仅保留sdk.allowPrerelease=false、移除version/rollForward，net10.0与CSharp14不变。10.0.401是本次私有安装身份，不是工程版本锁。本轮不新增读取canonical global.json，也不回写已冻结v1/source与source-manifest；Root在新策略和静止源码下创建新candidate后才进行真实构建。v1所有构建/测试/SDKbundle继续NOT_RUN，见sdk-policy-build-hold.json。

## 追加：SourceRepositoryRoot 最小接线扩展（2026-10-14 05:05 UTC）

本节记录独立于旧controller-candidate-v1的后续源码修正。旧v1源码、73文件manifest、旧global.json及所有NOT_RUN不回写。当前canonical的BundleBuildRequest末尾追加可选SourceRepositoryRoot=null；Root后续必须用包含该扩展且符合“不锁SDK”策略的新候选进行真实验收。

缺省仍以SourceRoot作git HEAD目录。显式SourceRepositoryRoot在任何输出创建或原生启动前要求绝对、已存在且无链接祖先。该目录只作为source-before/source-after两个git rev-parse HEAD的WorkingDirectory；完整工程清单验证、CopyToFreshBuildRoot、dotnet publish的build-inputs工作目录及源/副本前后SHA核验没有换到仓库目录。

原始类型化请求及engineering-overlay.json保留该字段和完整EngineeringSources。SourceCommit仅表示所选真实仓库的基线，不自动证明任意SourceRoot是该commit纯净树或overlay已提交；真实快照的git archive原件与明确overlay由Root独立绑定。本扩展不创建.git、worktree、替代Git helper或脚本，也不迁移其他CUDA dirty。

原BundleInputChecks的11项完整保留，新增两项有针对性的生产方法回归，共13项：

1. 旧JSON省略新字段仍得到null；相对、缺失及实际目录symlink仓库精确命中预检拒绝，并在任何构建输出/原生调用前停止。symlink由fixture实际创建，只在确认仍是本任务目标后移除链接，目标保留；权限不足直接失败，不跳过。
2. 另一个独立目录保有完整原始工程字节时，已被修改的SourceRoot仍命中Missing or changed engineering input，输出目录未创建，错误快照不会被仓库文件覆盖；同一typed serializer保留两个不同根字段。

两项检查使用既有测试apphost仅作未运行的executable-admission输入，绝不创建或调用假Git程序。目录和源码均为explicit synthetic fixtures，不能当真实Git/SDK成功。新13项构建及执行均为NOT_RUN；真实Git HEAD前后绑定、SDK发布、完整包/解包/runtime读回由Root取得构建槽后另跑。

源码前件及冻结、精确patch在 C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\bundle-source-repository-extension-v1。仅修改BundleBuild.cs、BundleInputChecks.cs、Tasks/README.md和本文；共享入口、其他源码、旧候选与Git均未写入。

## 追加：v2 静态候选与最终核心等待（2026-10-14）

Root 后续授权仅准备新 `controller-candidate-v2`，不编译、不运行 SDK、不写 Git/index、不回写旧 v1。该候选从 v1 的 73 项清单（SHA `0091fd6e22ef22ea3069d89d17bf4214e7df297f3209a414e7cc3b5c3f852a5e`）逐项复制并核 SHA，只覆盖上述冻结扩展的三个候选源码/说明文件及当前不锁 SDK 的 global.json；未复制 foreign GPU/Kernel/Trainer dirty。v1 archive 基线仍为 `20f769f4c7493acddb41db5638e11b73080a4b76`，Root 本轮确认真实仓库 HEAD 为 `2b3ff839279b01608761d28b2bc8f378c9f7d89e`；二者通过 archive 与明确 overlays 分别留证，不混称同一纯净树。

共享 CLI 的 Commands 路由及 MigrationCommands 已通过 `BundleBuildJson.Default.BundleBuildRequest` 类型化读取进入 BuildAsync，因此本扩展静态不需追加 canonical 共享入口或 csproj 修改。候选保留 v1 四处共享接线的原精确 patch。bundle13/controller14/resident8 的两平台 tiny1/full 共 12 个执行请求已准备，显式绑定 broker、取消文件及 Windows 私有 DOTNET_ROOT。当前静态 11 项接线审查通过，构建、类型化 JSON/CLI 运行及所有回归均为 NOT_RUN。

真实 SDK bundle 预请求 SourceRoot 为 v2/source，SourceRepositoryRoot 为 canonical 目录、仅查询两次 HEAD；只发布 Tooling 与 CPU ControllerFixture。当前完整 57 项 admitted engineering inputs 已用独立有界文件系统枚举逐项复核 bytes/SHA，完整源码清单仍为 73 项。生产 bundle inputs、本次真实 Git HEAD/SDK 发布、完整包/解包/runtime 读回保持 NOT_RUN。Windows 私有 dotnet SHA 已只读复核为 `21a46f1e5235cf4e844b9de5429f0e198b9c97a41f0503a66442f1d639ca3ee6`，这不是项目 SDK 锁。外层请求明确设置私有 DOTNET_ROOT/DOTNET_ROOT_X64；静态核环境字典经过继承、明确覆盖和 Windows CreateProcess 环境块传递，动态子进程环境未运行。

Root 随后仅准从 native-exec-race-candidate-v10/source 导入 ExecutionContracts、LinuxExecutionSession、LinuxWatchdog、ProcessExecutor 与 ExecutionChecks 五源。其 manifest SHA `0e5d2ed7abbae9851cb9032c69cd3c9351620bcfba72831fd74b6e120bfc6586` 及五文件实际 SHA 均已核，新增独立导入和 pending 清单；其他 Windows 核心仍旧 v1 baseline，不冒称最终状态。Root 随后报告 v10 完整 22 项门槛 FAIL（watchdog 在 parent reaped root 后读 /proc/stat 遇 ESRCH），selected8 PASS 不替代 full22；本任务未独立读取该失败原始回执。按 Root 指示停止复制中间 core，v2 状态为 HOLD_V10_FULL22_FAIL_AWAIT_FINAL_CORE，未生成最终 freeze，等待最终验收核心与唯一构建槽。所有准备、来源、精确差异和 NOT_RUN 边界在新候选目录留存；旧失败与旧候选原件保留。

## 追加：final-v1 Windows 构建、纯检查与 CPU resident 模拟（2026-10-14）

在保留旧v2/source与v10 pending请求原件的前提下，新建final-v1：73文件完整冻结，准确导入已验v12五源码及已提交WindowsSession（SHA起始0db2），候选driver仅补execution90，原bundle13/controller14/resident8接线保持。source-manifest SHA为`bf6d0a65f6ba0c45d3ed4ed04320624e65f8f3354e8eeb42cc64ee5785f30285`。唯一一次Windows build实际完成Tooling、ControllerFixture、Tests三项目，31.5958196秒、零警告/错误；没有额外SDK调用，结束即释放槽。

独立读取三个实际PE/PDB，57个手写来源checksum与冻结源一致、GUID/stamp绑定；352个实际PDB文档中295个生成文档只作为metadata计数，本次未导出或重hash生成源字节。完整runtime28项，manifest SHA为`686e2a2b7f46766962c9023685aabdea4c92a74bcf2b6dac6a424c4172702883`。Core DLL在三个输出目录的真实副本SHA均为`a9d03737eabc3b6ed60fc9e832be11860f42f769eb5a0fcfd42899023bd92316`；各轮前后73源+28runtime共101条pins均不变。

Windows bundle tiny1/full13与controller tiny1/full14实际全PASS，均仅driver进程。后续Root单独授权本机runtime窗口并静态核无Git调用后，resident tiny1/full8实际全PASS_CPU_CONTROLLER_SIMULATION_ONLY（0.462169秒/6.3021178秒）。driver内180秒、外层请求210秒；只使用已编译CPU ControllerFixture apphost模拟查询和有界游标保存，四文件是明确合成ASCII身份数据。GPU、真实训练/恢复、optimizer、质量、校准、发布与本候选NativeAOT均未验收，不能据模拟标签冒称。

pure严格审计5回执/11身份，resident两组严格审计24回执/47身份，全ABSENT、live/unknown为0；另10个native launcher按PID/出生终核全ABSENT。24个resident目录为2outer加full内22scope，tiny内部query由tiny outer完整不可变登记及driver cleanup覆盖，未另外作为独立strict输入。原CANCEL内层一份FAIL/CANCELLED原样保留，absence没有改写原执行结论。没有Git写、SDK复跑、真实GPU/模型动作或旧证据覆盖。

final-execution-summary SHA为`d21c0aec943d84b5f8cae19e9903c475fc0639be287762caa63fac5049d75015`；resident full原生结果SHA为`be8dcd78bfecc275a15a460e147502aa92bc2159add950fb03ab62f2f0a42283`。正式T3保持NOT_DONE，依赖T2b真实Git8×2及后续真实bundle/解包/runtime验收。真实SDKbundle仍NOT_RUN，旧DiskFull失败保持；计划暂用Root确认的3b411d75b2ced16900f82a57a615847910c2f6f7，T2b最终提交后须用新请求重绑SourceCommit和完整EngineeringSources，不能拿当前计划或旧失败路径当实际成功。

## 追加：T3 真实 SDK bundle、新 HEAD 与 Linux FDD（2026-10-14 09:11 UTC）

本轮状态为 PASS_T3_CODE_WINDOWS_SDK_BUNDLE_LINUX_FDD_WITH_PRESERVED_INITIAL_FAILURE。复用final-v1冻结73源码与28项Windows运行文件及既有bundle1/13、controller1/14、resident1/8 CPU模拟结果；前后126项source/旧runtime/新Linux runtime pins不变。SourceCommit使用T2b提交63827efd3431aea37a49dc0215f4c22531e337bb，真实Git source-before/source-after均一致；SourceRoot仍为73项archive+明确overlay快照，不能说成该commit纯净树，SourceRepositoryRoot只用于两次HEAD查询。完整57项EngineeringSources独立枚举和bytes/SHA重核，无foreignCUDA。

唯一真实bundle尝试成功：Tooling与ControllerFixture各一次串行win-x64 self-contained publish，关闭build servers、单编译器，source外新build-inputs；391项冻结文件166722109字节，ManifestSHA为9d1b49ee202645e3646ba59f58b35429b0f5cb89344c960c032d8a04c5f9d98c。published与frozen全量逐SHA读回通过；TAR393文件167605248字节，SHA为d0e7f01d6b326ad066698b0baddbe2c57e92f0d6f0034c8c374c2dcf6d6be7d0。真实独立解包后392项manifest条目逐SHA通过，完整包不止apphost；旧DiskFull路径及失败原件未覆盖。此包仅Tooling/CPUfixture，不是完整模型/GPU训练SDK或消费者/服务器验收。

Linux随后三次串行FDD publish全部内外PASS/exit0/EOF/cleanupPASS，完成即释放SDK槽。25项完整runtime独立冻结，manifestSHA为dbceb2742b4431eed3a63943d72634997e894cfd5873c1c51196e98faf3686b5。初始bundle tiny1通过；长/mnt/c输出full13在6项后触发原45秒driver取消，nativeFAIL/134、WSLFAIL/1和cleanupPASS完整保留。Root仅准多一次业务后，二进制/源码/45秒driver均不改，新唯一/tmp短路径full13实际13项0.717秒PASS。因果仍为UNKNOWN_IO路径假设，没有生产修复。

同一短路径后续controller tiny1/full14分别0.045/0.128秒PASS，resident tiny1/full8分别0.859/18.967秒PASS_CPU_CONTROLLER_SIMULATION_ONLY。总业务7次包含原失败，非全部7次成功。全量937文件/334目录/534907字节的/tmp证据已复制冻结并双SHA核对，当前文件均在任务根保留。五批严格当前资源审计覆盖Windows17scope/77记录及Linux33scope/115记录，全部ABSENT、live/unknown/reused0，各平台去重PID另见summary，不相加冒充独立PID数。原长路径失败、预期CANCEL内层失败与旧证据不改写。

19个本轮Windows native launcher按原始出生、精确argv及父链独立读回，全部退出或身份重用判定不影响新进程，未发送终止信号。Windows UNC精确临时目录删除曾DirectoryNotEmpty，随后既有C#native runner监督/usr/bin/rm只删除明确本任务/tmp根，内外PASS/EOF/cleanupPASS并验证目录不存在。该清理自身native控制收据在最后五审计之后产生，独立当前native audit为NOT_RUN，不能混入此前192记录分母；其Windows launcher已终核。13个本任务startup gate已清理，未清空共享TEMP、缓存或其他tmp。

本轮实际1次bundle尝试（内部2 SDKpublish）加3 Linuxpublish，业务上限由Root6增至7，资源审计由4增至5，每批最多12logdirs，总35calls及09:15窗口未扩。T3 NativeAOT并入最终T8保持NOT_RUN；真实GPU、生产resident、optimizer/训练、质量、校准、性能、现场/租机及模型数据发布均NOT_RUN。无canonical源码/共享入口/Git写入，唯一仓库写为本文追加。最终证据位于controller-candidate-v2/final-v1/t3-real-sdk-head63827-v1，精确SHA、分母和边界见final-t3-summary.json及cleanup-resource-summary.json。