# T6 Validation 最终准备证据（2026-10-14）

本轮复核冻结候选的历史操作与 CLI 检查，并补齐 Windows NativeAOT batch 目录的实际进程路径。Windows/Linux 操作检查各 13 项，JIT 与 NativeAOT 目标工具的 CLI 检查各 4 项通过。新增 tiny1 / full2 仅验证 synthetic asset preflight 的两个明确拒绝与目录回执读回；不能据此宣称真实模型、数值对齐、语言质量、校准、GPU 或部署验收。canonical 共享入口由主集成者负责，候选通过不替代最终源码树构建。

**范围与原始证据位置**

审查开始于 `2026-10-14T03:24:26Z`，原定截止 `03:54:26Z`。PowerShell 为 7.6.6。主智能体将原始回执读取上限由 24 提升至 28，仅覆盖准备格式失败与更正后的 tiny/full 回执；源码读取不超过 10 个文件，调用不超过 40 次。未编译、联网、使用 GPU、写 Git、改共享入口或覆盖候选、历史输入及旧失败。此文和 Validation README 是本轮仅有的 canonical 修改。

- 冻结候选根：`C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\validation-candidate-v2`。
- 新请求与回执根：`C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\validation-final-plan-v1`。
- 旧真实 preflight 请求与失败：`D:\source\Sezika\.artifacts\tooling-migration-20261013-01\validation-native-real-2-request.json` 及同名输出目录，原字节保留。
- 本轮复核索引：新根下 `existing-suite-audit.json`；实际 batch 读回：`batch-directory-readback.json`；入口与输入身份：`freeze.json`；本轮文档冻结：`documentation-freeze.json`。

**E1：操作与真实 CLI 回执**

以下每组同时核对 `checks.txt`、`test-process.result.json` 和对应真实 ProcessExecutor 回执。真实执行回执全部为 `Status=PASS`、`ExecutionStatus=SUCCEEDED`、exit 0、input/output complete、未超过输出上限、cleanup PASS、remaining 0；测试驱动 PID 与原始进程身份证据一致。Linux 表内为真正 Linux inner 回执，本轮未另读其 Windows WSL outer 回执。

| 检查 | 完成数 | 驱动 PID | 真实执行回执（相对候选根） | SHA256 |
| --- | ---: | ---: | --- | --- |
| Windows 操作 | 13 | 61592 | `win-validation-13-result.json` | `6924c541ebbea7b66af4b29123a18d90561404730e0c00f0b7ca01696dfdbc02` |
| Linux 操作 | 13 | 57554 | `linux-validation-13-inner-result.json` | `1f9f206c248fb3153cbe558ccbbb42ee25d6f17a587f5671c4d0fc6267836167` |
| Windows JIT CLI | 4 | 4092 | `win-validation-cli-4-result.json` | `80436f34a8a34adb8f896a5284fea0c1c15e166f5ab78b0f0fa1f0d59ed6c731` |
| Linux JIT CLI | 4 | 57663 | `linux-validation-cli-4-inner-result.json` | `570be99e1979f7b8e62ffe081a6f60739bb56cb8fbb82f949ab5bd2456cb3280` |
| Windows NativeAOT 目标 CLI | 4 | 25040 | `aot-win-validation-cli-4-result.json` | `735d44b17409108e0b3d3ff30dbfb9123b17f0798bb5c1a873e4abfcd02c3947` |
| Linux NativeAOT 目标 CLI | 4 | 58160 | `aot-linux-validation-cli-4-inner-result.json` | `9d21caf4b9aa246dd6fcffdd9b18c2923be766a4a8c353a6a90856150d5c5c3d` |

AOT 目标的 publish 回执也独立读回：Windows `aot-win-publish-result.json` SHA `a1ab2d58aabcfdbec5668ba3efe50b4a316bb5e9013e93576941a97ea6ed3f8a`，Linux `aot-linux-publish-inner-result.json` SHA `3a166261f86a845caa4ca98fa5636eb1bdc0397d2cde8a6f3f93ef3080c57cfb`。两者均以 `PublishAot=true` 执行并 exit 0、EOF 完成、cleanup PASS / remaining 0。本轮没有重读编译 stdout，因而不另宣称诊断数为 0。

所有 `checks.txt` 的 `native_aot=False` 指测试驱动。四项 CLI 中真正运行的目标 apphost 由 ProcessExecutor 子进程完整 argv 绑定到 `aot-win/Sezika.Tooling.exe` 或 `aot-linux/Sezika.Tooling`；不能把测试驱动改写成 AOT。13 项中的 batch 操作检查通过注入 synthetic executor 验证合同、复制预算、targeted stderr、失败分母等，不能单独证明真实 batch 调度。

**F1：状态、绑定与单次写入**

证据为 E1 和冻结候选 `MigrationCommands.RunValidation`、`ValidationCliChecks`。六种命令先绑定 typed `Output`，再打开业务输入或运行操作。profile / summarize / data-admission / near-ties 的 `Output` 等于绝对 CLI `--output`；language-audit / batch 等于 CLI `--output` 加 `.artifacts`。

文件操作的回执由操作写一次，CLI 读回完整 typed result 后直接使用。目录操作先写 `manifest.json` 或 `report.json`，CLI 读回核对后创建唯一外层报告。该分工修复文件回执重复 CreateNew，也避免仅凭泛化对象处理将 blocked 或 pending-human 误报为 exit 0。

| 明确 typed 状态 | CLI exit |
| --- | ---: |
| `PASS_HISTORICAL_PROFILE_CONSISTENCY_ONLY`、`PASS_HISTORICAL_S5_REPORT_CONSISTENCY_ONLY` | 0 |
| `original_fixture_audit_not_independent_quality_acceptance`、`PASS_BOUNDED_NATIVE_VALIDATION`、`COMPLETE_HISTORICAL_MARGIN_OBSERVATION_ONLY` | 0 |
| `PARTIAL_OR_FAILED_VALIDATION_PRESERVE_RECEIPTS`、`PARTIAL_HISTORICAL_MARGIN_OBSERVATION`、`blocked` | 1 |
| `checks_passed_pending_human_acceptance` | 3 |
| 未知 typed report 或未知状态 | 2 |

四项真实 CLI 检查分别覆盖 Output mismatch 在业务 IO 前拒绝、blocked 保留原报告且 exit 1、明确 synthetic 的 pending-human 且 exit 3、历史 profile 仅写一次且 exit 0。synthetic 数据中的 `Approved` / `Sealed` 不构成人工批准。`RequireAdmission=true` 被拒绝时保留原 blocked 回执。未知状态返回 2 为源码已审阅分支，本轮没有实际注入未知状态执行。

**E2：Windows NativeAOT 真实 batch 目录 tiny1 / full2**

新请求重新固定当前 dotnet、原有 IndependentModelTool assembly 和原始 5 字节 synthetic 资产，未重写旧请求中已漂移的 dotnet SHA。

| 身份 | 字节 | SHA256 |
| --- | ---: | --- |
| 候选 `aot-win/Sezika.Tooling.exe` | 13139456 | `e4af96293525d21b4d5d0c6e3984c5f8694000809cf1af3d1d68dc64fdbdef8f` |
| `C:\Program Files\dotnet\dotnet.exe` | 167208 | `abce6623a6729059cf39bb0f728c4a7804f625895099c88c17e6e704661e09eb` |
| `D:\source\Sezika\tools\Sezika.IndependentModelTool\bin\Release\net10.0\Sezika.IndependentModelTool.dll` | 544768 | `629da306816c20a2af83695334ea3e1fe80bfb477f24d34da61b030c2dcbc3c4` |

tiny1 使用 `tiny1-request-v2.json`，包含缺包一个 case；full2 使用 `full2-request.json`，包含缺包和无效 manifest 两个 case。均设置 `AssetPreflightOnly=true`、最多 1 MiB 复制、case timeout 10 秒、CLI timeout 45 秒，外层执行 50 秒并留出 5 秒清理。没有编译、重命名 ProcessFixture、解释器或脚本入口。

两个 case 的固定预期退出码均为 1，stderr 分别要求包含 `independent_model_not_installed` 和 `independent_manifest_invalid`。第二项取自旧原始 stderr：`independent-model-tool: independent_manifest_invalid: Manifest is missing required fields.`，其原始 92 字节 SHA 为 `097031e5627e9a030cb543a02f188fb34f6488d36251be1daa130019dcb5fb9f`；不再使用宽泛 `independent_`。

| 本次实际执行 | CLI Root PID | validator PID | 结果 |
| --- | ---: | --- | --- |
| tiny1 | 65704 | 27076 | CLI exit 0；planned/completed/passed=1/1/1，failed/unprocessed=0/0 |
| full2 | 73776 | 15284、64900 | CLI exit 0；planned/completed/passed=2/2/2，failed/unprocessed=0/0 |

外层回执分别为 `tiny1-v2-execution-result.json`、`full2-execution-result.json`，均 PASS / SUCCEEDED / exit 0 / 完整 EOF / cleanup PASS / remaining 0。batch 报告的每个 case 保留实际子进程 exit 1 和 cleanup PASS；`SyntheticTransport=false`、`NewInference=false`。实际子进程使用固定 dotnet + managed assembly，不能称为 NativeAOT 模型推理。

full2 外层 `full2-report.json` 与内部 `full2-report.json.artifacts/report.json` 均为 1017 字节，独立原字节 SHA 均为 `58375ec309326b2c71860b23b4752a0833193fa95432bb753bc20629d07e3144`。tiny1 外层报告 SHA 为 `0070990efc5fb5b138cb78b64fae7649d617d28d20c80562475f6824f663a15d`；内部报告存在，由实际 CLI 做 typed readback，本轮未另读其原字节。

随后对本轮记录的 9 个准确 Windows 原始身份做有界 OS 读回，全部原出生进程已消失。包括准备失败的 native CLI PID 5288、tiny/full 的 CLI 与 validator，以及各自 controller launcher。只读 PID / 原始出生时间 / argv / parent chain，未停止任何进程。该结果为本地 Windows OS 读回，不冒称另外执行过 NativeAOT audit 命令。完整身份证据见 `batch-directory-readback.json`。

**F2：预算、targeted rejection 与原始失败**

`ValidationBatch` 的 `MaximumCopiedBytes` 在整个 case 循环外累计；第二个 case 不会重置预算。所有非零预期退出码都在预检阶段要求非空 stderr；运行后还必须匹配 exact exit、完整输入输出、cleanup PASS / remaining 0、允许的预期非零错误以及明确拒绝文本。E1 的两个新增检查验证跨 case 的实际文件复制边界，以及未声明 targeted stderr / 不相关 stderr 的拒绝。

准备失败完整保留：第一次将旧纯文本 stderr 当作 JSON 解析失败，发生在任何子进程执行前；外层 request v1 将空 byte[] 写为 JSON 数组，outer helper 在反序列化时拒绝，未启动 batch CLI；tiny request v1 的单元素 Cases 被 PowerShell 展开成对象，native CLI PID 5288 实际 exit 1 / EOF 完成 / cleanup PASS / remaining 0，未启动 validator。更正使用新版本请求和新鲜输出，原始失败与 generic FAIL 报告未覆盖。这些是本轮请求准备修正，没有变更 Validation 源码。

**P1：本次调用路径与复核方法**

路径为：现有 ProcessExecutor controller → 冻结 Windows NativeAOT Tooling `validation batch` → typed Output 绑定 → dotnet 与 managed assembly / 输入 SHA 预检 → 每 case 真实 ProcessExecutor → targeted stderr 与独立报告读回 → 内部目录 `report.json` → CLI typed readback → 唯一外层报告。E1 支持 F1；E2 支持 F2 及这条真实目录调用路径。

本次执行参数记录在新根的 `tiny1-execution-request-v3.json`、`full2-execution-request-v2.json`，由固定 `current-tooling-v17/build-artifacts/bin/Sezika.Tooling/release/Sezika.Tooling.exe process run --request ... --output ... --timeout-seconds 60` 启动。原目录已完成，不能在原路径重跑 CreateNew 操作；复现时新建任务目录、保留 JSON 数组类型，更新 typed `Output`、CLI report 和执行目录的精确绑定，并重新核对 exe / assembly / 输入 SHA。空 byte[] 使用 base64 或省略空值。

**仍未闭环**

- Linux 真实 batch 目录流程为 `NOT_RUN`；本轮没有把 Windows 目录结果移用到 Linux。
- 正常模式的真实 validator 成功 positive control、后续完整负例矩阵及取消/超时目录路径为 `NOT_RUN`。本次显式 AssetPreflightOnly 仅适用独立模型负向 synthetic asset。
- 真实模型前向、反向训练、独立数值对齐、语言质量、校准、性能与部署为 `NOT_RUN`。
- 最终 canonical 共享入口接线、最终源码树构建/回归及源到 binary 的最终绑定由主集成者单独留证。本轮候选检查和文档准备不替代这些门槛。
- SSH 独立审计产物保持原字节，其 pending 不在本轮追改；主智能体的 SSH 原生文件与进程补充证据独立管理。
