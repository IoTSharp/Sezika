# 独立 C# 数值参考工具

本工具是固定 Transformers v4.57.1 ModernBERT eager数学的独立 C#移植，没有生产 Sezika项目引用。它从原 PyTorch ZIP静态读取数据，不执行 pickle、Python、脚本、网络或生产模型代码。三份 Transformers 数学来源的 Apache声明和准确SHA见 [NOTICE](NOTICE)；Laya adapter、tokenizer资产及267项golden各自保留来源与许可记录。这是一条新参考路径，`upstream_executed=false`，不能冒充原 PyTorch上游执行。

## 当前入口

```text
Sezika.ReferenceTool capture --request <v2-request.json> --output <new-receipt.json> --timeout-seconds <1..1800> [--cancel-file <absolute-path>]
Sezika.ReferenceTool self-test --maximum-cases <1..8> --output <new-json> --timeout-seconds <1..120> [--cancel-file <absolute-path>]
Sezika.ReferenceTool laya --request <laya-v2-request.json> --output <new-receipt.json> --timeout-seconds <1..1800>
Sezika.ReferenceTool tokenizer --request <tokenizer-request.json> --output <new-receipt.json> --timeout-seconds 60
Sezika.ReferenceTool build-proof --request <genuine-build-request.json> --output <new-summary.json> --timeout-seconds 30
Sezika.ReferenceTool proof-checks --request <pinned-proof-request.json> --output <new-report.json> --timeout-seconds 30 --maximum-cases <1..8>
Sezika.ReferenceTool laya-input-checks --request <partial-check-request.json> --output <new-report.json> --timeout-seconds 60
```

通过 Tooling固定 C# apphost/DLL的 `process run` 调用；根统一构建和验收。`capture`只接受原 jhu-clsp/mmBERT-base revision `c5955035435e2bf121cde7f3c8863ef52ff35d82` 的1个固定46-token case及markers25/35，CPU仅用于小规模数值参考。内部数学API允许最多1024个token供同一参考项目的独立Laya adapter使用，不表示mmBERT CLI已扩大范围。正式训练和批量特征计算保持GPU要求。

`ExportRequest`与`ReferenceContract`定义见 [Contracts.cs](Contracts.cs)，JSON使用snake_case，验收receipt的顶层`Status`保持准确显式字段。新合同schema为`sezika.independent-oracle-contract.port-derived.v2`、状态`frozen_before_port_measurement`；原身份字段`source_weights_sha256/source_weights_bytes`保持原名。请求固定原 weights/config/tokenizer/actual metadata/新v2合同、3份上游原始源码、完整C#程序来源、实际native/host image与JIT DLL的 bytes/SHA，以及现行executor的 `ProcessEvidenceDirectory`。请求和CLI期限/取消路径须一致，output directory和receipt须新建；外层期限预留清理。当前 v10 `ProgramSources` 为完整24项源码／项目，实际证明另含219项生成来源和5项runtime文件；新增编译输入仍须刷新完整清单。

原始源码只作为只读取证，保存为`.source.txt`；不作为可执行文件。官方仓库是 `huggingface/transformers`，明确tag为 `v4.57.1`。本轮原字节位于 C盘任务证据目录 `Sezika-tooling-artifacts-20261014/reference-mmbert-20261014-032132`，捕获仍独立核验其bytes/SHA。旧v1合同和历史原件不修改。

## 数学与输出

严格核原config和134个`model.*` tensor的名字、shape、连续stride、storage offset及LE F32字节。embedding只物化选中行，但对整个原embedding tensor计算SHA。模型采用768 hidden、1152 intermediate、22层、12头；首层跳过attention norm；每3层global attention，其余半窗64；half-split RoPE theta160000；attention/MLP分别残差相加，MLP为GELU(input)*gate。dropout关闭，温度/决策head不在本入口范围。

数值路径如实记录：FP32 tensors与vector dot reduction、FP64 population layernorm统计后转换FP32、erf形式GELU的A&S7.1.26近似，以及实际vector宽度/runtime/OS/architecture/argv。它不宣称与PyTorch逐bit相同；新v2参考必须独立比较，不能观察差异后放宽旧冻结容差。

`sezika.independent-reference.v2`输出含25个`[46,768]` checkpoint、`[2,768]` marker、I32 tokens/markers、原始request、134个原tensor的来源与逐tensorSHA，以及输入/源码/程序/合同/已登记进程identity。只有全部完成才写`capture.json`；途中已刷出的原始文件和编号partial保留。完成receipt为`CAPTURE_COMPLETE_COMPARISON_NOT_RUN`，不使用模型质量或数值对齐PASS。原始权重只有完整读入/执行及134项分母闭合后才标verified。

输出总上限20MiB、partial最多28份、JSON1MiB/深度32/200000tokens、PyTorch pickle4MiB/300tensors/有限静态opcode、ZIP4096entries、源hash2GiB/2049blocks。请求总期限1..1800秒、观察resident512MiB..6GiB，Ctrl+C和准确cancel file支持取消。resident是检查点采样上限，不能当作任意瞬间硬内存隔离。工具不启动子进程；完整外层进程回收仍由Tooling独立回执验收。

## 独立检查与验收

8个self-test使用手工固定的解析结果，覆盖矩阵方向、population norm、half-split rotation、erf GELU、稳定softmax、global/local attention及非有限拒绝；不调用生产数学、不加载权重。先1项tiny，再完整8项，输出明确`fixture_only=true`、`original_weights_verified=false`、`represents_parity=false`。

2026-10-14 的冻结 v10 已实际完成 JIT 构建（0警告／0错误）、生产 `build-proof`、genuine proof1／proof8（1／1、8／8），包括真实 PE 的单字节 CodeView GUID 负例和真实非构建 probe 拒绝。原始 v9 compiler pin 当前不一致的正控 FAIL 原件保留；v10 没有重写旧证明。

v10 实际从原始 mmBERT 权重导出25个checkpoint、验证134个原tensor，再由 `compare-port` 完成26／26比较、884736元素、零失败；`accepted_port_derived_reference=true`，`accepted_independent_reference=false`，`upstream_executed=false`。这是固定小规模 CPU 数值参考，不是训练、质量、上游 Python 或 Native AOT 验收。

Laya 接口已接线：`laya-input-checks` 在 v10 实际完成 tiny1＋1、full 输入46／46及历史logits decoder42／42；`tokenizer` 完成 tiny1和完整267／267 golden crosscheck。局部报告 `fixture_only=true`、`original_weights_verified=false`；它们不生成新的神经logits。真实 Laya 原权重仍缺失，完整 encoder／head／scorer／act_head forward 和原权重 port 数值正控保持 NOT_RUN。`laya` 的实现入口不代表该完整模型已验收。

最终 `Sezika.OracleCompare` 消费者已独立编译并执行4个早期metadata拒绝；没有真实Laya正控的七类深层来源／runtime／process动态负例仍 NOT_RUN。`Sezika.OracleCapture` 主项目及其CUDA依赖的最终编译为 UNKNOWN／本轮未执行，不能由 OracleCompare 编译或共享helper编译替代。

本轮原生回执保留PID／birth／argv／父链、输入输出和cleanup证据。2026-10-14 11:59 的 Windows 系统SDK关键依赖缺失观察保留为当时的 NOT_READY；此后主集成者按用户授权，在任务独立目录完成微软官方SDK下载、SHA512、解包及实际版本核验，回执为 `windows-sdk-recovery-v1/installation-verification.json`。项目不固定SDK版本，`global.json`仅排除预览版，构建记录实际选用版本；原v10构建输入、证明和数值结果均不回写。准确源码清单、原件SHA和各项范围见[最终证据](../../docs/evidence/tooling-reference-final-2026-10-14.md)。
