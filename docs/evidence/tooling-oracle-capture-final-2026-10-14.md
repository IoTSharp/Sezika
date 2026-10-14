# OracleCapture Windows 编译绑定与 CLI 拒绝检查，2026-10-14

当前追加读回：Root 在后续独立范围核对冻结180份源码与六份AOT发布的16个完整运行文件，全部匹配；另核保留的ext4原件703文件/15,846,651字节及归档SHA。两份最终Linux审计原回执中的Launcher由准确PID/start ticks/boot/argv取证，均已在同一可读boot的`/proc`中不存在。独立原件在本机`root-t8-final-readback-v1/`。此前T8片结束于11:15:38 UTC、超过11:14边界的原分区结论及未在原期限内完成的NOT_RUN不改写；本次补查是后续证据。MP32原UNKNOWN、缺三条累计出生注册的严格审计FAIL保持，未接受该次发布输出。

本轮结果是冻结源码与 Windows managed JIT 产物的绑定、四项实际 CLI 拒绝检查，以及独立进程审计。真实 Laya 模型前向、正控、数值比较、深层 port-proof 故障注入、Linux 构建与 OracleCapture Native AOT 发布均为 NOT_RUN。负例通过不能代替这些验收。

## 候选与本轮范围

候选根目录：`C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\oracle-capture-final-candidate-v2`。

本轮独占证据目录：`C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\oracle-capture-final-acceptance-v1`。没有修改候选、canonical 源码、共享入口、Git 或既有 CUDA dirty 文件；仅新增本记录及独占证据文件。未启动 SDK、GPU、租机、训练、教师采集或发布。

Root 提供的候选已真实构建。本轮读取并核实原始 `win-build-request.json`、`win-build-result.json` 与 stdout：PASS、exit 0、输入/输出完成、cleanup PASS、remaining 0、Errors 空，7 个登记身份；编译 stdout 为 0 警告、0 错误。构建使用固定私有 `dotnet-10.0.401-win-x64\dotnet.exe` 及同目录 DOTNET_ROOT，目标 net10.0、Release、禁止共享编译和 MSBuild server。本轮只校验其字节身份，不重新启动该 SDK。

候选 `final-source-manifest.json` 实际 135 文件，SHA256 `3a82431ce38654ad657edc71192fea2e7512e74c0506857e0093d9aa8dd5d7ca`。明确 overlay 共七项：OracleCapture 的 Contract、PortReferenceProof、Program、README、csproj 五项，加 `tools/Sezika.ReferenceTool/Provenance/CompiledProgramProof.cs` 和无 SDK version/rollForward 的 global.json。这七项的候选快照与 canonical 当前字节均匹配冻结清单，运行后再次复核相同。

OracleCapture csproj 明确以 Compile Link 引入 `../Sezika.ReferenceTool/Provenance/CompiledProgramProof.cs`，并引用 Sezika 与 Sezika.Cuda 项目。不能把 helper 的存在写成其深层验证已在本轮全部执行。

## PE、Portable PDB 与 runtime 绑定

生产输出目录为候选下的 `build-artifacts\bin\Sezika.OracleCapture\release`，实际九个文件：OracleCapture 的 exe/dll/pdb/deps.json/runtimeconfig.json，Sezika 与 Sezika.Cuda 的 dll/pdb。`runtime-before.json` 对九个输出、接受的 AOT ProcessExecutor 和固定 SDK host 共 11 文件记录路径、字节数及 SHA256。

接受的执行器固定为 `C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\windows-lifecycle-candidate-v1\aot-tools\publish\Sezika.Tooling.exe`，SHA256 `341e075512d56598d0630f6706772155bba31388b7d1e7186a7b6f38bc778a61`。SDK host SHA256 为 `21a46f1e5235cf4e844b9de5429f0e198b9c97a41f0503a66442f1d639ca3ee6`。

通过 PowerShell 7 已加载的系统 MetadataReader/PEReader 只读检查三组 DLL/PDB，没有加载或执行被检查程序集。各 PE 的唯一 Portable CodeView GUID/stamp、age 1 与相应 PDB 内容身份一致。

| 程序集 | MVID | 匹配冻结清单的手写源码文档 | 单独记录校验和的生成文档 |
| --- | --- | --- | --- |
| Sezika.OracleCapture | 6f481211-22cd-4e46-be55-b06cae530a11 | 7 | 63 |
| Sezika | 91a8ef94-8d9d-4829-b563-d230273555e8 | 53 | 81 |
| Sezika.Cuda | 20836bc9-83fc-46fb-abdf-2b978931d630 | 30 | 4 |

共 90 个手写源码文档的 SHA256 匹配冻结 135 文件清单；另 148 个生成文档只保存 PDB 中的路径与 SHA256，没有单独导出这些生成文件，不声称已独立验证其源字节。完整逐文档记录见 `source-pdb-runtime-before.json`。Core/CUDA 的源码比较针对冻结候选，未取 canonical 中其他人的 dirty 版本。

该产物包含 managed CLR metadata。本轮的 `--require-aot` 拒绝结果也明确证明当前目标由 managed host 运行；不能以 apphost EXE 的 PE 外形或 SDK 编译成功声称 OracleCapture 已通过 Native AOT。

## 四个实际负例

每项均由接受的 AOT `Sezika.Tooling process run` 启动冻结 OracleCapture apphost：内层 Oracle 超时 10 秒，外层执行 timeout 15 秒、cleanup 10 秒、max processes 4、stdin 0、输出上限 65536 字节，另有数量、取消文件及 suite 墙钟上限。每次显式使用固定私有 DOTNET_ROOT。

| 名称 | 精确目标拒绝 | 实际退出与范围 |
| --- | --- | --- |
| managed-require-aot | `capture_failed: InvalidOperationException: --require-aot requires a Native AOT executable with dynamic code disabled.` | target exit 2；在读取 reference/model 前拒绝 |
| invalid-backend | `Backend must be scalar, simd or cuda; quantization is outside the frozen FP32 contract.` | target exit 2；Options.Parse 阶段拒绝 |
| port-upstream-claim | `capture_failed: InvalidDataException: Port reference proof: port-derived reference cannot claim upstream execution` | target exit 2；原始坏 reference 的元数据拒绝 |
| port-fixture-claim | 同上 | target exit 2；原始 fixture claim 的元数据拒绝 |

后两项 stderr 的第一行均为 `Read bounded local reference inputs and frozen identities; reference numerical outputs are not deserialized.`；两行完整内容逐字匹配。前两项没有这行输入读取提示。四项 stdout 都为空，没有 `Loading verified local model`，capture 输出目录均未创建，模型参数均指向本轮不存在的受控路径，未伪造模型、权重或正控。

后两项复用 `laya-consumer-final-v2-checks` 原始请求已指向的坏文件：

- `reference-final-v9-20261014-1126\laya-negative1-invalid-upstream-claim.json`，配 canonical `tests\fixtures\laya-oracle\contract.port-derived.v2.candidate2.json`。
- `reference-final-v9-20261014-1126\laya-negative2-invalid-reference.json`，配同历史目录 `laya-negative2-contract-fixture.json`。
- cases 均为 canonical `tests\fixtures\laya-oracle\cases.v1.json`。

这些现有输入的完整绝对路径、字节数和 SHA256 见 `negative-inputs-before.json`，运行后 5/5 再次匹配。没有修改或重新生成这些坏文件。它们只用于提前元数据拒绝，不能证明真实 Laya port 正控或深层 source/runtime/process 证明链已通过。

`negative-results.json` 为 `PASS_4_EXPECTED_CLI_REJECTIONS_ONLY`。四份原始 native execution 回执仍是 FAIL/FAILED、target exit 2，外层 process-run CLI exit 1，Errors 精确保留 `Process exit code 2`；没有把原始失败改成 PASS。每项登记一个完整 native 出生/argv/父链身份，退出状态 EXITED/EXITED_RETAINED_HANDLE、EOF 完成、cleanup PASS、remaining 0、无未解决观察错误。

## 独立审计、前后复核与进程归属

`native-audit.json` 由生产 AOT audit 实际读取构建和四个负例的精确 execution 目录，共五份 native 回执、11 条身份记录，live 0、unknown 0，状态为 `PASS_current_recorded_task_identities_absent_or_reused`。原始状态分母为一份 PASS、四份 FAIL，完整保留。审计自身也通过 native executor 包裹，回执为 exit 0、EOF 完成、cleanup PASS、remaining 0。

`before-after.json` 记录输出/工具 11/11、overlay/helper/global 7/7、原始负例输入 5/5 前后哈希一致。`owned-cleanup.json` 使用原生 result.Launcher 的实际 PID、创建时间、argv、父链进行当前回读，本轮五个 launcher 的相同出生均不再运行；没有按名称终止进程或接触其他任务。

这些证据完成的是 SOURCE_BUILD_BINDING 与 CLI_REJECTION_ONLY。四个预期拒绝不能代替真实模型推理、真实上游运行、数值对齐、语言质量、校准、性能或 Native AOT 验收。

## 最小后续检查，均未运行

`remaining-checks-plan.json` 与 `planned-windows-aot-request.json` 仅准备计划，不自动启动 SDK。

1. 主代理明确分配构建槽后，复核冻结 135 文件，使用已固定私有 SDK、MSVC 和独占临时/产物目录进行 Windows AOT publish；独立绑定 native PE、runtime 和源身份，再验证 `--require-aot` 能到达预期的前模型元数据拒绝。当前 NOT_RUN。
2. Linux 先确认主代理接受的进程所有权协议、Ubuntu 中准确稳定 .NET 10 SDK 路径与 source snapshot/mount 绑定，再执行有界 build 和相同负例。Linux SDK 路径未猜测，当前 NOT_RUN。
3. 真正前向需要已核实来源、许可、版本及 SHA256 的原始 Laya 权重、tokenizer、配置与独立参考。先一条真实 case，再单独扩大；capture 完成与数值比较分别验收。当前 true forward、positive control、deep port-proof positive、46-case 和 numerical parity 全部 NOT_RUN。

## 后续追加：真实原权重单条正控与资源预算（2026-10-14）

上文保留较早候选的原始检查结果。本节记录后续已获Root授权并实际完成的Windows managed JIT单条正控，不回写上文的旧135文件、四个早期CLI否定或原下载超时FAIL。真实五个原始资产已完整下载并独立核实；原ModelTool生成170个tensor描述的model.json/source_lock.json，再由独立C# port和实际typed SIMD分别前向。显式choice-en-short比较通过，只有1个answered pair，完整46条输入清单保留，覆盖1/46、缺45条，full_manifest_passed=false、historical_upstream_gate_accepted=false、upstream_executed=false。

本节不是上游Python执行、全46条验收、语言质量、校准、性能或Native AOT证明。Linux与OracleCapture/OracleCompare Native AOT此时仍NOT_RUN。原始模型、tokenizer、source-lock、合同和各旧candidate均保持原字节。

### Evidence：可复核原件

证据根A为 `C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\laya-original-local-positive-v1\cpu-positive-run-v2`，新候选根B为同级 `oracle-capture-resident-budget-candidate-v3`。下表的A/B均指这两个准确根目录；文件不在仓库内，离线复核需要保留这些原件。所有输出均为真实运行产生，未手写tensor清单或填充参考数值。

| ID | 文件 | SHA256 |
| --- | --- | --- |
| E-01 | A/acceptance-summary.json | e18f5172c93cdd484cb2707535912afe1c3d3d84a57f17c59251640569c30788 |
| E-02 | A/final-freeze.json | 8468ddc64d3fdf8b6aa94e54441468a48f06bd4dff5b43fcdd4a77e6d702cfa3 |
| E-03 | B/source-runtime-binding.json | a0e1321b0625124a4895ec52ff9dfb3a5bf2f60b8afca466ec36c68a67b05813 |
| E-04 | A/manifest/generated-output-pins.json | 73923b3d51e5f6c784d2b1ca1a6135290b9fa8dd303875df2cbb91c551d8a752 |
| E-05 | A/reference/receipt.json | 3f616a910d79b2b22297615255303a76a45e042e1cdd23f1f8e4c4ad8c2db4d4 |
| E-06 | A/actual-simd/native-result.json | ae6f27972db6bb11bb38d824e75aef64a1b8eb8bc62e29ae1463495a8ab68f4c |
| E-07 | A/comparison/report.json | 8864fa20fa74b18c7840335fc197e45f5deb7267261ca1f090c6fc6d37dd556b |
| E-08 | A/cli-budget-negatives/summary.json | 4afef6bd5374687b8097ed5516320dc7210eff6ce6fec0c2292ce6c9e14f506a |
| E-09 | A/deep-seven/summary.json | 929e56de77b95aa8ca6d409886e9bcced4a404fc0a6baf41d1dac6b84d4e0dbd |
| E-10 | A/final-hash-readback.json | d1c056e76c0bb641f0d8ce3d00b2fd37db4a52a0ba350950cf31c1ffbc1b3bfd |
| E-11 | A/final-native-audits/summary.json | fc8f3595fd7b380019b78f26a3d22371302a32c91ba7f8ee91626ec71c1b90fc |
| E-12 | A/final-owned-cleanup.json | d79a1d02a27a7c60dc988137ca066c7653a7175713fc02f877d5d55a69081853 |

例如使用PowerShell7的Get-FileHash读取A/acceptance-summary.json，所得SHA应为E-01；再用ConvertFrom-Json -DateKind String读取E-07的answered_pairs、requested_case_ids、missing_case_ids和full_manifest_passed，能独立核实1/46的实际范围。所有旧执行请求的输出已存在，不得原样重跑覆盖；重新执行需新请求、新输出路径和Root安排的资源槽。

### Finding：真实来源、编译与资源合同

E-03绑定新候选135文件，source-manifest.json的SHA为 `0a63e9b47fc3582e1f2412e9afd92d84106305a4a23fe9fb3d2fd6022fec502c`。它由旧候选131文件加本次必要的Options、Program、Contract、README四个overlay构成。旧候选的135文件不修改。新fresh build使用私有SDK10.0.401一次、单核、禁止build server与共享编译，耗时47.478秒，0警告/0错误、exit0、EOF完成、cleanup PASS、remaining0，7个登记身份；随后释放SDK槽，没有第二次SDK调用。

三对真实PE/Portable-PDB的CodeView GUID/stamp/age与文档checksum绑定完整：

| 程序集 | MVID | 手写文档 | 生成文档仅checksum |
| --- | --- | --- | --- |
| OracleCapture | f950b524-a7a6-4766-a2b3-b623f3402537 | 7 | 64 |
| Sezika | f00f693e-92d4-43dd-809c-333dfc8ce84f | 53 | 81 |
| Sezika.Cuda | 48b08d92-5e9a-4154-8984-e2f8243af7dc | 30 | 4 |

合计90个手写文档、149个生成文档checksum与9个实际runtime文件。生成文档没有导出成独立源码证据；managed apphost不能当作Native AOT。OracleCompare使用已独立绑定4个手写源码、5个runtime和7个编译输入的原消费者，MVID为8375b77e-4591-461f-a260-b30baf5517ee；没有以共享helper存在代替其真实DLL/PDB绑定。

新增可选参数 `--maximum-resident-bytes` 使用long，范围1048576..4294967296、缺省保留4GiB。实际显式传2147483648，Options值进入既有DecisionResourceBudget.MaxResidentBytes，并写入capture.implementation.maximum_resident_bytes。它是既有typed admission预算，不是操作系统硬内存隔离，也不限制模型加载前内存。README准确保留这一限制。

### Finding：原五资产、170项清单与单条数值正控

原模型为convaiinnovations/laya-multilingual，revision052592a15d198d9ad47da779604259b10b47b7aa。643835514字节权重的SHA为9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204；原五资产共678201614字节。ModelTool原有3个手写源码及12个runtime/host pin在执行前后均一致，原编译器来源UNKNOWN不回填。

原ModelTool的manifest实际耗时3.196秒，从真实SafeTensors头及每个tensor范围产生170项描述；verify耗时1.808秒，检查原weights/tokenizer完整SHA与头。E-04保存真实生成文件字节身份；independent-header-and-lock-readback.json另只读原头，逐项核170个名称、dtype、shape和offset及生成source_lock的五项SHA。该读回没有声称再次独立计算全部170个tensor范围hash，范围hash由已绑原ModelTool产生。

E-05的独立C# port reference使用原24项ProgramSources、219项真实导出的generated sources、5项runtime及未改写的原build-summary，实际64.815秒，planned/completed/answered=1/1/1、failed=0，观察peak546512896字节。参考capture的SHA为20008d7807f015af36fb2489fcb6c03eb92e06c66f1468a6b6a8c3adbe9cd482，明确upstream_executed=false、represents_parity=false。

E-06的typed SIMD实际捕获31.671秒，choice-en-short为answered，implementation.maximum_resident_bytes=2147483648，完整manifest分母46、missing45。actual capture SHA为0cc8c73ad8bb157e77466b62594d29d4ca7da62b9b9e40d36ef2dd4799057290；参考数值未进入adapter。两项真实执行均使用DOTNET_PROCESSOR_COUNT=1、准确私有runtime和独立原生注册/回收。

E-07由已绑OracleCompare使用显式第7参数choice-en-short产生，6.674秒、1个answered pair、issues为空、passed=true。最大logit误差1.5404052717826744e-6，最大概率误差9.000484801902076e-10；冻结容差没有放宽。报告原样保留46条清单SHA、两个完整capture SHA、missing45、input_coverage=1/46、full_manifest_passed=false和historical_upstream_gate_accepted=false。

### Finding：六项参数拒绝与七类深门

E-08的六项实际CLI负控分别为重复、缺值、非整数、Int64溢出、低于1MiB与高于4GiB。各请求采用最少10个合法必填argv后追加目标错误，未被args上限提前挡住；逐字匹配对应parser拒绝，target exit2、外层exit1、capture目录未创建。

E-09只在真实单条比较正控通过后执行。七项都从独占副本一致变异metadata，每个副本明确negative_fixture_only，原数值、权重、源码、runtime、registered和native回执原件均不改；不能将这些图副本当真实producer证明。

| 门 | 实际命中的拒绝位置 |
| --- | --- |
| G1 完整producer源码清单 | build source denominator differs |
| G2 compiled源码SHA | Provenance source/artifact SHA differs |
| G3 完整runtime清单 | runtime provenance denominator differs |
| G4 实际native build回执 | Exact successful build execution/request/compiler/root/cleanup binding absent |
| G5 独立登记scope | independent process birth/argv/parent/scope differs |
| G6 实际request选择分母 | complete selected reference row denominator differs |
| G7 producer回执与capture | actual producer receipt/capture binding differs |

七项均命中事先指定的目标错误，未把更早拒绝算作深门PASS。十三份原始native结果全部保留FAIL/FAILED、exit2及原Errors；测试汇总的PASS只表示预期拒绝得到验证。

### Path：完整原件复核与资源闭环

E-10在执行后重哈希新source135、runtime9、ModelTool源码/输入/host18、原producer证明261、Compare12，共435条pin记录（非435个不同文件），全部不变；另对原五资产各用同一只读句柄核length/SHA/EOF，并核两份原capture完整SHA不变。

E-11由两次实际原生audit读取精确真实execution目录，覆盖19份native回执、25条身份记录；不是25个不同OS进程。原状态为6份PASS和13份预期FAIL，MatchingLive=0、Unknown=0。审计自身同样由原生executor包裹，EOF/input完成、cleanup PASS、remaining0。negative-fixture图副本明确排除在真实进程审计集合之外。

E-12另对21个登记launcher做精确PID缺失或出生时间复用回读，live/unknown0，无终止操作；只保留本任务build临时目录作为编译证据，没有清空共享TEMP或缓存。原生执行含两次审计的最晚StartedUtc+Elapsed为2026-10-14T09:12:22.1007904Z，早于CPU共享09:14:30截止；09:15:21为最终JSON摘要/封存写入时刻，不是CPU阶段延长。后续文档和静态计划另有范围，不重跑已通过单条或七深门。

从本节Evidence到结论的链条为：真实五资产及170清单（E-04）→独立参考和实际捕获（E-05、06）→明确1/46的冻结容差比较（E-07）→针对性拒绝（E-08、09）→源码/包字节及当前资源闭环（E-03、10、11、12），最终由E-01/E-02封存。Linux/AOT需要新的平台原件和实际验收；Windows producer内的绝对路径和PE/PDB来源证明不能改写成Linux正控。
## 2026-10-14 T8 最终限定验收追加

Windows 与 Linux 的 Capture、Compare、Numeric 共六个 Native AOT 发布均有成功回执；各平台三份原生主程序、五组中间 PE/Portable-PDB、101 份手写源文档和八份发布文件分别绑定。Windows 原生 Compare 复用原始真实捕获完成正控与七项深层负控；Capture 仅完成模型前 AOT guard，不声称 Windows 原生模型前向。Windows 原生 Numeric 完成 legacy/port tiny1 与真实26项比较，26项无差异，仍为 port-derived、非独立上游。

Linux 在保留的 ext4 真路径上建立真实编译证明（24份手写源、221份生成源，其中218份实际 PDB SHA1 与3份 SHA256）。原生生产 gate 正控及八项针对性拒绝通过。真实 Laya 独立 C# port 参考与 `--require-aot` SIMD 实际捕获各完成1条 answered；原生 Compare 正控与七项深层拒绝通过。最大 logit 误差约1.52625e-5，最大概率误差约9.00042e-10，按冻结容差通过。完整46条仍只覆盖1/46、缺45条，`full_manifest_passed=false`、`upstream_executed=false`；Linux Numeric 仅 tiny1 fixture，真实26项 NOT_RUN。语言质量、校准、GPU 与性能不在此验收内。

最终资源审计按分区保留：38份 Windows 与23份 Linux完整回执共401条身份记录，当前匹配存活0、未知0；401是记录数，不是独立进程数。原 sdk04 Windows MP32 的32条最终身份中3条缺出生注册，原 UNKNOWN 与首次严格审计 FAIL 均保留，该目录为 NON_AUDITABLE_AS_COMPLETE_NATIVE_CHAIN，不能合并写为全部历史通过。原生成 SHA1 拒绝、/mnt/c 30秒超时、ZIP/tar staging FAIL 和超出12目录的审计请求 FAIL 原件均保留。ext4 同代码成功不构成超时因果归因。

最终分区、控制进程只读回查、保留 ext4 的新归档及逐文件 SHA 位于 `oracle-numeric-t8-candidate-v4/resource-close-1114`。原绝对路径 `/home/maikebing/sezika-t8-proof-v3b-20261014` 必须保留；归档不是路径重写或运行证明替代。最终状态以该目录的新冻结摘要及原始回执为准。
