# ValidationBatch 结构化非零退出：v5 实际验收

2026-10-14，Root 已实际运行新 v5 batch：Teacher 原版完整24项得到22 PASS／2 FAIL，修正版完整24项得到24 PASS；两版都先通过tiny1，完整矩阵均完成24项、未处理0。原版失败准确保留在catalog20／21，没有因其预期冲突而跳过或回写。新结果验证的是synthetic Teacher合同协议与真实OS子进程调度，不是模型推理、外部教师调用、模型许可核实、真实人工批准、训练授权或整个T6结案。

本次worker在2026-10-14 12:44～12:49（Asia/Shanghai）只读10份raw内容／字节和3份v5源码字节，独立计算SHA并交叉核对结果；没有重新构建或执行validator。只新增本文，冻结源码、请求、旧文档、原失败和Git均未修改。下文区分本次直接读回、Root实际执行记录和仍待主集成者绑定的范围。

## Evidence：准确原件与读回范围

以下路径相对统一产物根：

```text
C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014
```

| ID | 本次实际读取的raw | bytes | SHA256 |
|---|---|---:|---|
| E-01 | teacher-contract-v4-execution-v1/progress.json | 2807 | `d2fd318ea82743cb80e8ba3f8f002963f03c2f90d3f97a8d56df27c1496c7932` |
| E-02 | teacher-contract-v4-execution-v1/independent-full-report-and-sentinel-readback.json | 1519 | `04d59ae1b0dd4ddb634b38f466129a1c888bf47da660cd673a755f917b74fc22` |
| E-03 | teacher-contract-v4-execution-v1/native-audit-final-summary.json | 2286 | `777aea28573c0dc9dc081ec31587ba682b7bc91f01a246dbe0ab7133fcc03730` |
| E-04 | validation-candidate-v5/win-build-result.json | 5251 | `e3b95fbcf9953a399d4ef9024024ba6e5bfde313c0dbe97acd6f357d4093f90a` |
| E-05 | validation-candidate-v5/win-build-execution/stdout.bin | 845 | `87fd5c2e806fdfbb766e2b438c96e712fa188a86a6f78367f3ab1b452df37776` |
| E-06 | validation-candidate-v5/operations15/checks.txt | 1147 | `6ecfcaedbe0d62a1d61528de5d3791e9bb2398b5ef38dba7d1bb6a3f99996735` |
| E-07 | validation-candidate-v5/cli-full4/checks.txt | 363 | `1a5d87d8a0b6378d3c7ebf9801795a26842f1fce5db0c3e2cc33441dc4713952` |
| E-08 | validation-candidate-v5/baseline32/checks.txt | 1145 | `a6d7f362efe7689239c6d9a1e946e43f0d668a01de4e465d88ad112dd6c8fdd4` |
| E-09 | teacher-contract-v4-plan/v1-original24/full24-receipt.json | 9025 | `0994b11c05fa1b5ea4a483ddfd5dc95ef70a442d0d66649641b9b3c90cc383be` |
| E-10 | teacher-contract-v4-plan/v2-corrected24/full24-receipt.json | 9362 | `534d7ba8ef8532dad6543016bc4316a7a35fd6fec854b29884aeb8b7e52bd609` |

每份raw先核1MiB以内的文件长度（后五份收紧到128KiB），再从本次读取的同一byte数组计算SHA和解析内容；两批各最多5份、墙钟15秒。源码字节核查最多3份、共享墙钟20秒。目录枚举仅用于定位这些明确目录下的文件，没有展开新证据范围。

本次没有另读四份内部batch报告、四组全部54份native原件、各sentinel文件或tiny测试原件。E-01／E-02／E-03分别是Root保存的执行与独立读回／资源审计汇总；本次对其中两份full24外层报告另读原字节，SHA与两份汇总准确相等。不得把读取汇总表述为本worker重新执行了这些检查。

## F-01：实际新v5构建与Windows回归

E-04记录Root于`2026-10-14T04:34:10.8398301Z`启动真实构建，使用本轮私有工具链路径：

```text
...\toolchain\dotnet-10.0.401-win-x64\dotnet.exe
build tests/Sezika.Tooling.Tests/Sezika.Tooling.Tests.csproj
-c Release --artifacts-path ...\validation-candidate-v5\build-artifacts
--disable-build-servers -m:1 -p:UseSharedCompilation=false
```

真实构建回执为`PASS / SUCCEEDED / ExitCode=0`，cleanup PASS、remaining0、输入输出完整、Errors空；耗时20.617秒。E-05原始stdout同时显示0警告／0错误，生成Tooling及Tooling.Tests DLL。记录10.0.401是本次实测构建路径身份，没有据此重新锁定项目SDK政策。

| Windows检查 | Root已执行结果 | 本次独立读回 |
|---|---|---|
| 操作tiny1／完整15 | 1／1、15／15 PASS | E-06完整记录15个名称及`PASS checks=15`；tiny1原件未另读 |
| CLI tiny1／完整4 | 1／1、4／4 PASS | E-07完整记录4个名称及`PASS checks=4`；tiny1原件未另读 |
| 基础32 | 32／32 PASS | E-08完整记录32个名称及`PASS checks=32` |

三份检查记录均明确`native_aot=False`，属于此次Windows JIT检查驱动的实际结果。本组不构成Native AOT或Linux最终预算验收。操作检查中的注入式batch回归继续属于synthetic transport；Teacher四组实际batch另见F-03。

## F-02：JsonEquals三源在v5中保持准确字节

本次从`validation-candidate-v5/source/`独立读取以下3份源码字节，均与已交付的结构化JSON目标补丁准确相同：

| 源码 | bytes | SHA256 |
|---|---:|---|
| tools/Sezika.Tooling/Validation/ValidationBatch.cs | 14424 | `dbfdba77d8c4349e7684289eaa1c2ddac97bfb18920c2677223b70f344a04320` |
| tools/Sezika.Tooling/Validation/ValidationContracts.cs | 6349 | `1352d6939cd11cc116f8efef991a7d347e4c11ad046e9d0479c54e0f057db783` |
| tests/Sezika.Tooling.Tests/ValidationChecks.cs | 24186 | `98765cc9e1fbf67efaf34d692f69bc774c52d8c35232bc0f94a371adc05d4b34` |

兼容扩展允许非零预期退出由非空targeted stderr，或`MustExist=true`输出的精确JSON状态／字段约束识别。`ValidationOutput`末尾可选`JsonEquals`限制1..32字段，比较准确JSON类型和值；所有已声明stderr与JSON检查仍须通过，既有native exit、cleanup、EOF、输出存在性、字节界和SHA读回门保留。

E-06保留原13项名称／顺序，新增的两项实际通过：一项读取真实写出的status／计数／Boolean输出并接受无stderr的预期exit3；另一项拒绝错误number类型／值、Boolean类型／值，以及同时声明JSON目标时的无关stderr。它们证明操作回归的输出门行为；E-09／E-10再提供实际TeacherTool子进程协议结果。

Root报告v4共享PartialMigration分派的case语法构建FAIL原件保留，随后创建v5修正该来源再构建。上述3份Validation源码没有随该修正改变。本次未追加读取旧v4失败或共享入口源码，没有将v5成功回填到v4。

## F-03：Teacher两版完整24项均实际执行

E-01与E-02四组一致；E-09／E-10独立原字节确认两份完整报告均`Completed=24`、`Unprocessed=0`，`SyntheticTransport=false`、`NewInference=false`。

| 版本／profile | Planned | Completed | PASS | FAIL | controller exit | 报告SHA256 |
|---|---:|---:|---:|---:|---:|---|
| v1-original24 / tiny1 | 1 | 1 | 1 | 0 | 0 | `c5b326e5e9be846438435297ebec4eb8721795235f57a89c345847490449c49c` |
| v1-original24 / full24 | 24 | 24 | 22 | 2 | 1 | `0994b11c05fa1b5ea4a483ddfd5dc95ef70a442d0d66649641b9b3c90cc383be` |
| v2-corrected24 / tiny1 | 1 | 1 | 1 | 0 | 0 | `5f1b8d8486984e40030bd5056a2657aa6c775a783aee611ba57d7bbc4f82d993` |
| v2-corrected24 / full24 | 24 | 24 | 24 | 0 | 0 | `534d7ba8ef8532dad6543016bc4316a7a35fd6fec854b29884aeb8b7e52bd609` |

原版仅第20项`catalog-unverified-blocked`和第21项`catalog-declaration-is-not-evidence`失败。两行原始`ExitCode=null`、`CleanupStatus=READ_PROCESS_RECEIPT`及exit／cleanup／drain不符的Failure继续保留；没有将该占位字段改成PASS。原版完整状态仍为`PARTIAL_OR_FAILED_VALIDATION_PRESERVE_RECEIPTS`，资源存在性由F-05的独立native审计回答。

修正版只把这两份synthetic catalog的`candidate.model`从`qwen35-9b-q4km`改为当前明确合同要求的`qwen3.5-9b-q4-k-m`。原id、status及`independently_verified=false`语义保留，其他22对fixture字节不变；原→新bytes／SHA／理由保存在`teacher-contract-v4-plan/catalog-correction-deltas.json`，本次没有修改该文件。修正版24行中6行预期exit0、11行预期exit3、7行预期exit1，全部协议检查PASS；预期非零退出没有被改写成业务成功。

本次目标Teacher DLL固定为已有文件的字节身份：`D:\source\Sezika\tools\Sezika.TeacherTool\bin\Release\net10.0\Sezika.TeacherTool.dll`，SHA256 `0b51c5ff3888008189219e7b6f35d32e988eea39ebbde10393f9e6c30c851933`。本轮新batch及测试驱动由v5实际编译；没有声称Teacher DLL也在本轮从最新教师源码重建，本文未再次读取该DLL。

真实native调度与fixture内容分别表述：50个validator调用使用synthetic合同输入，未调用`collect`、模型、外部教师或网络。catalog声明verified不能成为独立模型／许可核实；synthetic accepted／human_review_id不能成为真实人工批准，训练许可仍为false。这里的native指真实OS子进程调度，Teacher目标为现有managed DLL，不提升为Native AOT结论。

## F-04：内外报告与原始sentinel读回

E-02记录四组`OriginalAndInternalReportBytes=PASS_exact_same_read_bytes`；两组full24都记录`Sentinel=PASS_frozen_original_sha`。本次实际读取E-09／E-10的外层完整报告，其字节SHA分别与E-01／E-02记录相同。

本次独立从冻结literal `do-not-overwrite`的UTF-8无尾换行字节计算SHA256，得到：

```text
708a546f370dee0ec382f9bf24102af0124ee649e0add925c577539e44e04f54
```

两份完整报告的第19项`output-never-overwritten`均PASS、预期exit1，`OutputSha256`都恰为该值。Root另有E-02实际原件读回结论；本次没有再读sentinel文件或内部报告，不能把这一层交叉核对称为重新执行原件读回。

## F-05：54份原始native回执已分组终核

E-03保存Root对4个controller＋50个validator共54个回执目录的实际审计。每次最多12目录，原始分母如下：

| 组 | Receipts | IdentityRecords | Absent | MatchingLive | Unknown |
|---|---:|---:|---:|---:|---:|
| 1 | 12 | 39 | 39 | 0 | 0 |
| 2 | 12 | 12 | 12 | 0 | 0 |
| 3 | 12 | 39 | 39 | 0 | 0 |
| 4 | 12 | 12 | 12 | 0 | 0 |
| 5 | 6 | 6 | 6 | 0 | 0 |

各组状态均为`PASS_current_recorded_task_identities_absent_or_reused`，本次汇总里的Reused也均0。identity records按每组原始记录保留，包含controller与嵌套validator关系，不当作全局去重进程数量。本次独立读取E-03，没有重新观察这些PID或逐份重审54个回执。

最初27目录一次传入的限额FAIL继续保存在`teacher-contract-v4-execution-v1/native-audit-1.json`；后续实际请求均不超过12。限额失败没有被覆盖为审计PASS。

## 历史失败、待绑定项与Path

Root已记录旧`validation-normal-directory-v1`正控内CliChecks第12项的独立token-budget预期错误，该旧FAIL继续保留；本次不修复、重跑或计入15／4／32通过项。本文没有为节省读取预算删除或重新解释任何旧失败。

本证据绑定v5实际Windows构建及其检查、真实Teacher合同矩阵。最新Linux native core＋tests总预算由executor-v4继续执行；Root报告其SourceCore2与v5相同，最终集成来源／二进制绑定仍由Root单独完成，本次没有读取或验收那条新链。不能据本文宣布整个T6、跨平台最终集成、模型质量、性能、AOT或部署验收完成。

P-01：结构化非零退出补丁的3源SHA相等（F-02）→ v5真实编译与15／4／32检查（E-04～08）→ 原版完整24保留22／2及新版本24／0（E-01、09、10）→ 内外报告／sentinel交叉读回（E-02、F-04）→ 54份回执分组终核（E-03、F-05）。这些结论分别保留来源、分母、执行层和未验范围。

后续复核可按Evidence表的准确路径读取byte数组并计算SHA，再以PowerShell7的`ConvertFrom-Json -DateKind String`核对原始类型和计数。需重新执行时由Root使用新请求／输出路径及准确runner身份；不得覆盖本文所列原件或把新的结果回填旧candidate。

## G. v6 三平台 Validation 最终范围验收（2026-10-14）

v6 在同一份冻结源码（84文件、64个C#，source-manifest SHA256 `1ab391b656e730123bcf9d32787500ea43f66614e3a21ae35642c759b5ae1d94`）上完成 Windows FDD、Linux FDD 与 Windows Native AOT 的实际 Validation 验收。每个平台分别执行 operations 小样1/全15、CLI 小样1/全4；12次运行均通过。此处是 Validation 操作 fixture 与实际 CLI 合同/负控，不代表教师、模型、Git fixture、语言质量或全项目推理验收。

新证据统一位于 `C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\validation-final-candidate-v6`。`cross-aot-acceptance-summary.json` SHA256 为 `98bf7ae346fc73e4d208a3403706c98f68a2d91eed4852da592b64f14bc841b7`；`cross-aot-final-freeze.json` SHA256 为 `2952df6c2410e3d4e5d88d91d11d62f6adf13fe05ff51f45d2fe31798766b351`。此前 Windows FDD 摘要与旧 candidate 的 FAIL/UNKNOWN/NOT_RUN 原件均保留，新结果不回填旧快照。

Linux FDD 与 Windows AOT 的4次串行发布均 exit0、输入/输出完成、cleanup PASS、remaining0；SDK 槽于08:04:09 UTC释放，此后没有新增SDK调用。Windows AOT 实际执行独立的 `win-aot/cli/Sezika.Tooling.exe` 与 `win-aot/tests/Sezika.Tooling.Tests.exe`，两者原生PE的CLR目录RVA/size均为0，运行日志明确 `native_aot=True`；没有把tests目录内的managed引用apphost当作原生CLI。

最终重哈希覆盖84/84源码、21/21跨平台发布文件、8/8 PE/Portable-PDB绑定文件，全部不变。四对PE/PDB分别覆盖每个平台50个Tooling与14个Tests手写来源，共128条手写文档记录；708条生成文档仅记录PDB checksum，不宣称导出源码证明。AOT来源证据是中间PE/Portable-PDB绑定加实际原生PE与运行记录，不声称原生PDB为Portable-PDB。

`final-runtime-audits-v1/summary.json` 的3组最终审计覆盖26份native回执、100条身份记录（不是100个不同OS进程），匹配存活0、身份未知0；18份原PASS与8份预期负控原FAIL完整保留。`cross-aot-owned-cleanup.json` 另对15个已登记Windows启动器做精确PID缺失或出生时间复用读回，无终止操作；一次CIM读回超时与一次聚合序列化失败均记录在该新文件中，不作为成功证据。

辅助 `linux-uid-preflight/native-result.json` 的max4启动链诊断仍为原始UNKNOWN、cleanup UNKNOWN、EOF=false；后续仅已知PID缺失读回，没有产生当前UID成功证明。Linux业务使用Root接受的 `git-candidate-v3/linux-final-v4` 默认UID1000证据，未使用 `--user root` 或更改所有者。Linux Native AOT、该worker的教师/模型/Git fixture仍为NOT_RUN。
## H. 最终共享接线与 Linux Native AOT 补验（2026-10-14）

`tooling-final-combined-candidate-v1` 冻结92文件，manifest SHA256 `7123d23ce6b0993456e892e03a6e667550d88a7ac78581c6f4cab8356b07367c`。基于上述84输入加入退役审查及Oracle/凭据检查，并保留已提交Windows生命周期和Git接线；Validation业务源码9项、RunValidation方法与v6相同。Root独立核92文件及7处精确候选patch。实际四次串行发布全部通过，随后Windows FDD和Linux Native AOT分别运行Validation操作1/15、CLI1/4，全通过；Linux日志明确native_aot=True。

最终摘要 `acceptance-summary.json` SHA256 `5616e8ba4c88c94b22d7c16d56b2be0ca688254e174e394dc79d78900b34ee3b`。92源码和21运行文件再核不变。组合检查的5批最终资源审计分别覆盖Linux16回执73身份与Windows30回执110身份（109个PID，109条ABSENT/1条REUSED），live0/unknown0；这是整个组合范围，不能全算成Validation身份。其他套件的原始预期FAIL及LinuxOracle模拟初次30秒取消失败均保留，其受控ext4复查单列，不影响上述Validation分项结论。

Root按Validation-only的准确共享partial提交：保留HEAD的Git/Windows，加入操作/CLI路由、typed读回和明确退出语义，不夹带T3、T7、Oracle或凭据待提交代码。最终466条替代gate绑定和项目整体完成仍另行验收。