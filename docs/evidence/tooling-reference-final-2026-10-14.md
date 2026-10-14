# Reference 最终证据复核（2026-10-14）

截至 2026-10-14 12:13（Asia/Shanghai），同字节源码的新 v10 已完成真实构建、生产 build-proof、genuine1／8（1／1、8／8），以及当前证明链下真实 mmBERT 26／26、884736元素、零失败。v10 Laya 局部输入46／46、历史logits decoder42／42、tokenizer267／267均已实际通过。它们仍不代表上游执行、真实 Laya 全 forward、质量、校准或 Native AOT。冻结 v9 的新正控 FAIL 和 v9 proof8 NOT_RUN 原样保留，不回写为 v10 成功；Windows 系统SDK关键文件缺失的当时观察与后续授权独立恢复分别留证。

本切片未修改 ReferenceTool 编译源码、共享 Program、原始证明、旧失败、golden 或容差。Root 授予唯一构建槽后串行构建 v10 与最终 OracleCompare，已交回该槽；后续仅运行验证和只读核查。没有下载、联网、训练、发布或 Git 写。专属 README 的范围更新不在24项编译来源内。本文采用 Evidence → Finding → Path 组织，属于工程证据记录，不采用安全报告厂商模板。

## 证据位置与固定身份

- v9 冻结根：`C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\reference-full-v9`
- 本次新增证据根：`C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\reference-final-v9-20261014-1126`
- v10 准备根：`C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\reference-full-v10`
- 既有执行器：`current-tooling-v17\build-artifacts\bin\Sezika.Tooling\release\Sezika.Tooling.exe`，SHA256 `8c0a3a0ecf36c7fc2814a62df29e25db298d461079caf16cb6481a9564b0be15`。

| Evidence | 文件及观察 | SHA256 |
|---|---|---|
| E-001 | v9 `build-summary.json`，142607 bytes；历史原件保持不变 | `663ab0531a324340fb227e43c7e9731c99262e74a7f3089f72dcc6a13175fe00` |
| E-002 | 新证据根 `proof1-report.json`；实际正控 0／1，精确拒绝 compiler SHA | `ad92e345566bd8991ba4e80c8c1d7e4356e0246fa8906132edfee483344b6a24` |
| E-003 | 新证据根 `proof1-execution-result.json`；真实 exit1、cleanup PASS、remaining0、输入输出完整 | `c16b25934973b288cd4cf76ddc05d8fc304c7c5ee62caebb035554ef54828001` |
| E-004 | 新证据根 `compiler-identity-current.json`；当前文件身份及已知候选限定核查 | `44dada75d6e3ce8bbefbb6efb4fb06a96bc433093520b482ec3ca4b9aae4da31` |
| E-005 | 工具产物根 `reference-v9-laya-full-report.json`；真实局部输入 46／46、decoder 42／42，均零失败 | `cc991ae5cff87b2484eb19a0a1d01eb2f9220e44db19d6b48ec6edd43262f9de` |
| E-006 | 工具产物根 `reference-v9-laya-tiny-report.json`；先前 tiny1＋1 原件 | `807ac531cc893a87ad23972f370968cff9608b27f4cc1de9999b9348eccfba44` |
| E-007 | 工具产物根 `mmbert-reference-v9-comparison.json`；实际 26／26、884736 元素、零失败 | `93fc48d6e2e13a0356290e49058112de389430428ef5f080d9c0bdfdd14f0a15` |
| E-008 | 新证据根 `mmbert-original-identity-current.json`；当前 config＋原始权重完整同句柄 SHA | `809c7ca680b1ab6c40b2689868b55fa8e3e0ddfd2aae149b473334590b39e9e6` |
| E-009 | 新证据根 `laya-consumer-dynamic-summary.json`；4 个早期 metadata 拒绝及 7 类 NOT_RUN 深层检查 | `13370f49527bbaee49b864ff6efc7d3b0438045ef40d3cd7626711826c617ac8` |
| E-010 | v10 `source-manifest.json`；24 项准确源码／项目清单 | `0bb27e615b25b1f80a0e40fb0efa341f983e4e7651034d1dca3aef6fb9a80a0b` |
| E-011 | v10 `preparation-plan.json`；26 个源及构建输入的 v9／v10 同字节结果和未执行计划 | `1018f9ebb611b849fc5cd146363216623a81db5c59af71b0e8139bb62c9d387c` |
| E-012 | v10 `build-summary.json`；实际生产证明24源码／219生成／5runtime | `aa3394b6c4286b1aced19b069e1a8809a96678c6c00622fdba2abda22e8572fa` |
| E-013 | `reference-v10-final-checks-v1/proof8-report.json`；8／8，真实GUID／PDB门及genuine非build probe | `bb8ba5637fc98d6ba54149455b017584eb36386ee1a2ad8577f79fa39bcdebcb` |
| E-014 | `mmbert-reference-v10-comparison.json`；当前真实26／884736零失败 | `d7ddfe29c3f6a47842c407aef0a0326c34602ff58adf0d4d48ebac126dc4a68f` |
| E-015 | `reference-v10-profiles-v1/laya-full-report.json`；当前局部46／42，完整forward=false | `9b4c0d397ad58238631f22a13bba47a5a60c15c3ce11f2dee909541479c8377b` |
| E-016 | `laya-consumer-final-v2-checks/summary.json`；当前最终consumer4个早期metadata拒绝 | `f6a3b57f606abc0cf3cb91549df4448e2cc2fab1704bbdc5cb4f85cd0c6811fe` |
| E-017 | `reference-v10-profiles-v1/tokenizer-full-report.json`；真实267／267 crosscheck | `2f8278049b44f50a67e55a88c5fb487e1bb37c55801c8bb707da31643f8aeaeb` |
| E-018 | 新证据根 `final-cleanup-receipt.json`；23个原生目录、33项task identities，live0／unknown0 | `4ae4112fc922658b6bdf0a0fd2a572ea8a6119e36fe6d78520a415b0c8c501d4` |
| E-019 | `oracle-capture-final-candidate-v1/preparation-plan.json`；准确Git baseline tree＋5项own overlay，未物化／未编译 | `d42b034112fad2531fda14c78d014ecc93daa3d8a370cb3c0e230cb009a77fe0` |

## F-001：当前 v9 构建正控失败，GUID8 不执行

证据 E-001～E-004。11:26 实际通过既有原生执行器运行 v9 `proof-checks --maximum-cases 1 --timeout-seconds 30`，精确观察为：

```text
Status = FAIL_COMPILED_PROOF_FIXTURE_ONLY
Planned=1, Completed=1, Passed=0, Failed=1, NotRun=0
ExpectedGate = SOURCE_BUILD_ONLY
ObservedGate = Provenance source/artifact SHA differs: C:\Program Files\dotnet\dotnet.exe
ExactCodeViewGuidNegative = NOT_RUN_real_managed_pe_guid_only_copy_negative
```

当前 `C:\Program Files\dotnet\dotnet.exe` 长度 167208，SHA256 为 `abce6623a6729059cf39bb0f728c4a7804f625895099c88c17e6e704661e09eb`；v9 原 pin 同长，SHA256 为 `21a46f1e5235cf4e844b9de5429f0e198b9c97a41f0503a66442f1d639ca3ee6`。当前 FileVersion 为 `10,0,1326,50504 @Commit: 4099f83977f4d29604a9aeb971fb0a7bc1d5f699`，文件 CreationTime／LastWriteTime UTC 均为 `2026-10-04T14:15:32Z`。哈希不一致的原因和发生时刻为 UNKNOWN，不能由该时间戳推定。

限定检查 `reference-full-v1..v9` 的准确 `build-proof-request.json` 路径及工具产物根顶层命名 compiler 文件：存在的 v6～v9 请求全部指向该系统路径与旧 SHA。未在该限定范围恢复旧 compiler 字节；未扫描系统目录，不宣称全机不存在原副本。保留 v9 summary、runtime、数值产物和新 proof1 FAIL 原件。正控未过，v9 proof8 不执行；不弱化验证器、不重写旧 pin。

复现须使用新的 request／report／execution 路径，避免覆盖原件。现有精确请求为新证据根 `proof1-execution-request.json`；其记录的程序和参数均保存在 E-003。读取该原生结果即可独立核对 PID、birth、完整 argv、父链、scope、exit、EOF 和 cleanup；不得把预期失败进程的原生 `Status=FAIL` 改记为成功执行。

## F-002：mmBERT 原权重身份与历史数值结果分别成立

证据 E-007、E-008。既有 v9 comparison 原件记录 `fixture_only=false`、`passed=true`、`accepted_port_derived_reference=true`、`accepted_independent_reference=false`、`upstream_executed=false`；比较分母 26，实际完成 26，元素分母 884736，失败比较／元素均为 0。该结果是独立 C# 数值移植参考的结果，未执行上游 Python。本切片没有重新运行数值比较，也没有因 F-001 推断既有数字错误。

当前另行核对原始模型 `jhu-clsp/mmBERT-base`，revision `c5955035435e2bf121cde7f3c8863ef52ff35d82`：

| 资产 | 准确路径 | bytes | 当前完整 SHA256 |
|---|---|---:|---|
| 原始权重 | `D:\source\Sezika\.artifacts\models\mmbert-independent\source\pytorch_model.bin` | 1231188142 | `8ea64ec1ea4eb8fca0fc14b69a2ae571de6bfbc25fd214bb932dd4aba6a3a04e` |
| config | `D:\source\Sezika\.artifacts\models\mmbert-independent\source\config.json` | 1188 | `47b40fd2e1df8299426dd5f4bb18c28f028cfafcb51b73645f83e596d187eb37` |

先用小 config 验证读取，再完整核权重；最多 2 文件、每文件 2048 个 1MiB block、共享墙钟 35 秒，实际 4.32 秒。使用同一只读句柄计算 SHA 并复核长度／EOF，提供准确取消文件和分块进度。来源、许可证和模型资产仍与工具代码分开管理；本切片没有下载、分发权重或开展训练。

## F-003：Laya 的 46／42 仅为局部 fixture 一致性

证据 E-005、E-006。先前实际 tiny1＋1、full46＋42 产物保留：输入层完成 46／46，含 42 个构造成功期待和 4 个真实无效问题分类；decoder 完成 42／42，均为零失败。decoder 仅把历史 `raw_logits` 作为显式输入，重新计算 softmax、Choice、zero-based 未舍入 Score、false／true Boolean 及 `>=0.5` 阈值；它不生成新的神经网络 logits。

full 报告明确：`fixture_only=true`、`original_weights_verified=false`、`upstream_executed=false`；`represents_encoder_forward`、`represents_neural_head_forward`、`represents_action_head_forward`、`represents_full_laya_forward`、`represents_quality`、`represents_calibration`、`represents_aot` 均为 false。输入构造与解码通过不能表述为 encoder、head、scorer、act_head 或整体 forward 通过。

只检查以下三个既有明确路径，`model.safetensors` 均不存在：

```text
D:\source\Sezika\.artifacts\models\laya-mmbert\model.safetensors
D:\source\Sezika\.artifacts\models\laya-mmbert\source\model.safetensors
C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\laya-port-source-20261014-1942\model.safetensors
```

预期模型为 `convaiinnovations/laya-multilingual` revision `052592a15d198d9ad47da779604259b10b47b7aa`，原始权重 SHA256 `9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204`；来源见 `tools/Sezika.LayaOracle/source-lock.json`。本切片不扩展搜索、不下载替代权重。真实 Laya 原权重 forward、完整 port capture／数值正控、质量、校准和 AOT 均为 NOT_RUN。

## F-004：既有 Laya 消费者早期拒绝已执行，深层门仍 NOT_RUN

证据 E-009。使用既有 `port-consumers-v1\build-artifacts\bin\Sezika.OracleCompare\release\Sezika.OracleCompare.exe`，SHA256 `da0224dd608abfb9292ca2dc4c2d9d04120f0971b27de08ce35de7e5712627a9`，先最小 1 项，再余 3 项。所有夹具均位于新证据根，以历史 JSON 副本制造明确无效 metadata；没有生成或伪造成功执行回执，没有产生新的数值输出。

| 项目 | 实际精确拒绝门（均带 `Comparison unavailable: Port reference proof:` 前缀） | 结果 |
|---|---|---|
| 顶层 `upstream_executed=true` | `port-derived reference cannot claim upstream execution` | PASS_metadata_rejection_only |
| 顶层 `fixture_only=true` | `port-derived reference cannot claim upstream execution` | PASS_metadata_rejection_only |
| raw-logit absolute 容差由 0.0005 改为 0.0006 的夹具 | `frozen numerical tolerance differs` | PASS_metadata_rejection_only |
| 错误 v2 actual schema 的夹具 | `explicit v2 reference/actual contract required` | PASS_metadata_rejection_only |

四项实际 consumer exit2；外层原生结果均真实记录失败退出，cleanup PASS、remaining0，且没有创建 comparison 报告。完整 argv、birth／父链、stdout／stderr 和原生结果分别在 `laya-negative1..4-execution*`；每项 `*-observation.json` 保存 expected／observed。早期 metadata 拒绝的分母独立为 4，不并入 Laya 的 46／42，也不算真正 source／runtime／process 证明链验收。

当前没有真实原权重 Laya port 正控，以下七类动态负例继续 NOT_RUN：完整 runtime 清单、export request 资产／source／cases／contract、最终完整父链／ownership／platform、Linux expected-exec 原生退出凭据、捕获的 build-execution pin、result ArgumentList 与 native argv、同次 JSON 读取或 link／race。此前静态增补记录不能代替这些动态结果。原 reference／cases／v1 contract／candidate2 contract 的准确旧 pin 复核均保持不变。

## F-005：v10 同字节准备与后续实际验收

证据 E-010、E-011。24 项源码／项目与 v9 同字节，且与本轮核对时工作区全部相同；加 `Directory.Build.props` 和 `global.json` 共 26 个不同文件。复制上限 32 文件、墙钟 30 秒，实际约 0.20 秒。没有复制旧 bin／obj／成功 summary，也没有把 v9 数值产物重新绑定为 v10。

v10 `build-request.json` SHA256 `38c1ed6b8c72bcef9f0034812490316b00381f8312e797752470087edc781dec`，沿用原始构建参数、单 MSBuild worker、禁 shared compiler／build server、生成源码输出位于新 v10；最长 160 秒、cleanup10 秒、最多64进程、4MiB输出。准备时 E-011 如实记录 NOT_RUN；Root 后续明确分配唯一槽并核无旧构建后才执行，前后 compiler SHA 均为 `abce6623...`。真实 `build-result.json` SHA256 `b1dd0fc101f79c47479a71d41bbb34a20227b155e759ef26a3b7ffe1b885010e`，0警告／0错误；未写占位成功回执。

实际顺序为真实 v10 build → 生产 build-proof → genuine proof1 → proof8 → 新原权重 mmBERT export／compare-port → 最终 OracleCompare 编译及早期拒绝 → v10 Laya 局部和tokenizer。均使用新请求／输出／native evidence，保留 v9 FAIL。新原生请求采用 Root 指定的 `native-audit-candidate-v1/aot-win/Sezika.Tooling.exe`，12239360 bytes，SHA256 `bb33455efd974fd800f17e2c328a99caa3f986ec74e93399827814c50feff6ed`。

E-012／E-013：proof1实际1／1，proof8实际8／8，零FAIL／NOT_RUN；proof8 22.62秒。只改变真实PE的一个CodeView GUID字节：`6d6e7814-e88f-4050-9cda-448a93b2b9c2` → `6d6e7815-e88f-4050-9cda-448a93b2b9c2`，保留原PDB，精确拒绝 `Pinned PE CodeView GUID/stamp does not bind this Portable PDB.`。两份复制夹具保留，真实成功执行回执未改、检查未启动子进程。

E-014：`mmbert-reference-real-v10-plan` 使用同一原权重、4GiB／180秒固定小规模参考；新run-contract与v9原合同同字节（SHA `e7fcd2be9c364f92a7ab0dae9142ae47acea0afd3e98ce42435ae755c336bcf5`），没有放宽容差。真实新capture SHA `cf47854667bc1bcd9c4388809b1f1c8719bb161dd6974e0d96a69bb133c59bf6`，25checkpoint、134原tensor；导出17.76秒，观测peak114638848 bytes。随后新 `compare-port` 为26／26、884736元素、零失败，port-derived=true、independent-reference=false、upstream=false。没有把v9 capture重新绑定为v10。

E-015：v10新请求先tiny1＋1，再full46＋42，均PASS；原生full6.56秒。`reference-v10-profiles-v1/tokenizer-*-request.json` 复用准确旧tokenizer与267 golden，仅更新v10 source／build／runtime／process pins；先tiny1，再完整267，分别crosschecked1／267。完整tokenizer receipt为 `PASS_independent_tokenizer_267_crosscheck`，capture SHA `6f92703b2b8e36d6de0ed0755846f93642c96b51df94790ad94fb80bb3d1d817`，上游执行仍false。Laya局部与tokenizer不是模型forward。

E-016：`laya-consumer-final-v2` 从准确7项源码／构建输入复制验收候选，实际OracleCompare编译0警告／0错误，5项runtime pin；source manifest SHA `30696f04530729b838bf5b5bcd7beab8d7d7a68e641a0c4119e4b568f3d9ebd8`。4个早期metadata负例在该最终二进制再次精确拒绝。七类深层动态负例及真实Laya正控仍NOT_RUN。**OracleCapture主项目及其CUDA依赖的最终编译为UNKNOWN／本轮未执行**；OracleCompare编译与共享helper编译不能替代它。

## F-006：后续 SDK 缺失影响当前重建就绪性

新证据根 `current-sdk-dependency-readiness.json` 在11:59独立只读核查四个准确路径：dotnet.exe仍167208 bytes、SHA `abce6623...`，与v10 compiler pin一致；SDK10.0.401的 `dotnet.dll`、`MSBuild.dll`、`Roslyn/bincore/csc.exe` 当前均不存在。状态为 `NOT_READY_known_SDK_dependencies_missing`，未重新构建、安装或扫描其他系统目录。v10五个runtime文件完整SHA重核全部相同，包括DLL `c9e67689f5a5d55c5ad0161f8f44745b22f0d94cdf717bbc071e580f44f719af` 和PDB `b5c8df336fa486f915c85fb02bec02320cd81c2ebfc953c7a99987e33b5bd30c`。已完成的真实构建、证明与数值原件不改；它们不等同于当前SDK可立即重建。

以下为准确 24 项编译源码／项目清单，路径相对 `tools/Sezika.ReferenceTool/`；不是正式发布包清单，也不把 source-generation／JIT 构建判为 Native AOT 验收。

| 文件 | bytes | SHA256 |
|---|---:|---|
| Budget.cs | 5431 | `f398202851559f4bddb27d0dec09da29fe04e3b72d1e3ab188dcd847d7eb8f2a` |
| Contracts.cs | 5876 | `72fb0f741edecbafe0d6b3daf37f2ec2c7a52dccd4ff11cafa5d0a501a9e1cc8` |
| Exporter.cs | 20387 | `800215524686c0f410ed427d4a7497c34ccf07115a46bd7a9fcf25ae405449c8` |
| Laya/LayaCases.cs | 5251 | `1808202b6dcbf51db2ed8c5ffddee50a212d0f0cfe12b8c3033269e6b47d4906` |
| Laya/LayaContracts.cs | 5645 | `38fe0a8a107e961a1692d689b409e981477d45ece6dc7c1808dc015ab68c4eb8` |
| Laya/LayaExporter.cs | 12744 | `3c808ed6d0816eb77d7d79a2eca0bfb6b9d1db1a56c55a6f2786d95e80934e8a` |
| Laya/LayaForward.cs | 6312 | `55002b528d7acfcf93f8af9d2ae661c53897bcce6f8cd5bec89ec1386ecd1a57` |
| Laya/LayaInputChecks.cs | 29833 | `e789bfe68b795c6b04fdb3b2969eb031fdbcd0a296bb268853920ba901c509d2` |
| Laya/LayaQuestion.cs | 11629 | `ba8101196b96ec9be2f06e9e00e5ca0e11506164bab87c6a2d66b7f795c7578b` |
| Laya/LayaSequence.cs | 5514 | `20410b747f2c770f44ce891750c6b79d941039e590747e69a01b830a7f708c9d` |
| Laya/LayaWeights.cs | 6191 | `23f5bb843391637287af534ada163d39812e1ad5cae496fd831b1c5661b77dae` |
| Laya/PortProof.cs | 7851 | `5e4ed8e5f201978b3005d473a56cf15b2eff41a683d2ec10d7263582537be75a` |
| ModernBertReference.cs | 10770 | `d26c42e788dee7d958650da28465f8901072c08a018535f9739796166c9e1bdb` |
| OriginalWeights.cs | 6012 | `5beb1d6f82dd2085008db1ba88bd922fb8c759e44882ba72c7c0b078b38066ca` |
| Program.cs | 10383 | `5da0d0121a931e20b42aeb9635fe66f7721929f324954dee68f6ac1c3eb27063` |
| Provenance/BuildProofExporter.cs | 13360 | `cb21cd66df9314cd206ad53055022f92792bef2c7c1ed71255c1b589dc658b09` |
| Provenance/CompiledProgramProof.cs | 37630 | `7c280932919b4c08dde5e2ab67e816f35079f7e3d61ffbc9df7d78a32bdfb035` |
| Provenance/ProofChecks.cs | 28021 | `547ea352e133481648fc1d843b97ddad225a0a0fd99e776a28d64bd6423cc879` |
| RestrictedPickle.cs | 9297 | `eb8a0157e8c3e7472993cd73518044b013ccb239f70be54866b420e4f9c262a2` |
| Sezika.ReferenceTool.csproj | 367 | `1bc91fba5f777cde48d1e5bcc8762527014bd90edf9a0f31ec6beabe6d0acb5e` |
| Tokenization/BpeModel.cs | 7133 | `eedfbd7935eaca923bbecc6a91e6400d729d4aa9c152663d581e251d02cf5096` |
| Tokenization/FrozenTokenizer.cs | 8047 | `7e4101fa137a28b2cf485c8f827e0abc9a231adda438929111826756c12a3199` |
| Tokenization/TokenizerContracts.cs | 1904 | `93c8ad919c148adc92e60366281caa6c10afce2840ae462e8bcaf7919a0f0286` |
| Tokenization/TokenizerExport.cs | 5935 | `c450128729e16447e5874642d9f371367a564ee5fd87b6ae9ab5bb0c5657cb98` |

## F-007：OracleCapture 后续验收准备计划

E-019 只读解析指定 commit `20f769f4c7493acddb41db5638e11b73080a4b76`，限定 `Directory.Build.props`、`global.json`、`src/Sezika`、`src/Sezika.Cuda` 和 `tools/Sezika.OracleCapture`。准确Git tree为133文件、1254630 bytes，记录每个blob及长度；上限700文件／64MiB，后续物化墙钟60秒。准确五项当前own overlay为 `Contract.cs`、`PortReferenceProof.cs`、`Program.cs`、`README.md`、`Sezika.OracleCapture.csproj`；各项当前SHA及共享 `CompiledProgramProof.cs` SHA固定在计划内。未夹入工作区CUDA优化dirty，Git查询均为只读。

截止时交付的是**准确准备计划**：`Materialized=false`、`BuildStatus=NOT_RUN`，没有声称源码快照已物化。后续由Root将准确baseline blobs与五项overlay／共享helper物化为验收复制，禁止作为新开发workspace；再按计划以Ubuntu既有SDK／指定AOT执行器单槽编译，复核source／runtime／原生进程证据。OracleCapture主项目最终编译仍UNKNOWN，真实Capture forward／GPU均NOT_RUN。

## Path 与资源边界

P-001：原 v9 summary（E-001）→ 本次真实最小正控及原生退出（E-002／003）→ 当前 compiler pin 精确核查（E-004）→ 保留 FAIL、停止 v9 proof8 → 同字节 v10 准备（E-010／011）→ 新真实构建／生产证明／genuine1→8（E-012／013）→ 当前原权重数值26／884736（E-014）。后续SDK缺失单列F-006。

P-002：历史 mmBERT 数值比较（E-007）＋当前原始权重身份（E-008）→ 新v10独立执行与数值比较（E-014）；历史Laya局部／metadata拒绝（E-005／006／009）→ 当前v10局部46／42与tokenizer267、最终consumer4早期拒绝（E-015／016）→ 七类深门和真实Laya整体forward保持NOT_RUN，均不提升为上游执行。

本轮使用 PowerShell 7.6.6。原生执行均设置独立新请求、墙钟、输出和进程界；记录启动器及子进程 PID／birth／argv／父链，使用准确身份清理路径，不按进程名终止。数值负例夹具与失败、v10源码／生成来源／runtime均为需保留的证据／交付物，不作为临时垃圾删除。未创建新脚本、后台服务或独占 MCP。18个先前原生目录按工具12目录上限拆为9＋9独立审计，保留最初参数超限FAIL；随后补审tokenizer2目录及只读Git3目录。E-018合计23个原生目录、33项task identities，live0／unknown0；9份原始业务失败保留。进程观察journal含73条shell／启动器记录，按PID＋birth核查无本任务匹配存活；PID23004已被另一node server以不同birth重用，原样保留，没有执行终止。最后检查shell74064正常exit0。所有单项审计、最终回执和journal均保存在新证据根。

主集成者补充：`reference-acceptance-review-v1/audit.{json,md}` 独立核对24项编译来源、219项生成来源、5项runtime、PE/PDB身份、真实GUID单字节篡改、原始6资产与26份F32产物；没有发现独立ReferenceTool编译源码阻塞。该审查只接受所读证据的范围，不替消费者或OracleCapture主项目背书。NOTICE/README随后只补充Laya及tokenizer来源范围，不改24项编译输入或旧证明。

Windows系统SDK缺失的原始F-006观察仍保留。用户随后明确授权微软官方SDK 10.0.401 ZIP安装到本任务独立目录；主集成者完成300,608,304字节ZIP的官方SHA512、5,577项解包、关键程序SHA及实际版本检查，见 `C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\windows-sdk-recovery-v1\installation-verification.json`，系统SDK未改。用户另要求项目不锁SDK，提交 `36ba9b8` 已把 `global.json` 改为仅排除预览SDK；Windows 10.0.401和Ubuntu 10.0.112均实际解析通过。安装版本与原v10工具身份作为证据记录，不构成项目SDK锁，也不回写历史构建输入。
