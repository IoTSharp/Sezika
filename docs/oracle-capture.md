# Sezika 实际推理对照导出

`tools/Sezika.OracleCapture` 将固定 Laya oracle 已展开的 `input.state` / `input.question` 转为 Sezika 类型化请求，使用本地模型和实际 C# CPU scalar、SIMD 或 CUDA Driver 后端生成对照文件。它不生成语言质量标签，也不把模型预测当作业务执行授权。

参考文件的反序列化类型刻意没有 token、marker、logits、概率或预测字段。工具从原始输入调用 `PromptSequenceBuilder`，再通过 `ModernBertDecisionEngine` 实际推理；记录包装器核对后端真正收到的 token、marker、type ID 与 builder 一致。导出的概率来自 C# `DecisionMath.Softmax`，并逐项核对类型化返回的候选映射。Boolean 的语义候选顺序固定为 `false → true`，自定义显示标签仅影响模型输入描述。

## 有界运行

执行遵守仓库 `AGENTS.md` 与当前任务授权。使用已核验的 C# `Sezika.Tooling process run` 和类型化 `ExecutionRequest` 运行构建及导出，登记 PID、出生、完整命令行和父链，并核输入/输出 EOF、退出码及进程树回收。先运行一条，通过后再扩大到最多 46 条；每次进程最多 1800 秒。Windows 使用 PowerShell 7，调用参数保持结构化。

以下是请求结构示例。所有占位符替换为本次已核验的绝对路径，输出目录及回执必须新建。构建固定单个 MSBuild 节点并关闭构建服务器；程序、DLL、源码、模型与合同的 bytes/SHA 由上层任务独立固定。`ExecutionRequest` 自身不提供程序 SHA 准入，不能把宿主 SHA 当作应用身份。

```json
{
  "Executable": "<dotnet绝对路径>",
  "ArgumentList": ["build", "tools/Sezika.OracleCapture/Sezika.OracleCapture.csproj", "-c", "Release", "--disable-build-servers", "-m:1", "-p:UseSharedCompilation=false"],
  "WorkingDirectory": "<冻结源码根目录>",
  "OutputDirectory": "<新构建执行目录>",
  "TimeoutSeconds": 180,
  "CleanupSeconds": 10,
  "MaximumProcesses": 64,
  "MaximumOutputBytes": 4194304,
  "Environment": {"DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER": "1", "MSBUILDDISABLENODEREUSE": "1", "UseSharedCompilation": "false"}
}
```

```json
{
  "Executable": "<已核验OracleCapture apphost绝对路径>",
  "ArgumentList": ["--reference", "<冻结参考capture.json>", "--model", "<固定模型目录>", "--cases", "<固定cases.json>", "--contract", "<与参考版本一致的合同.json>", "--output", "<新capture目录>", "--backend", "simd", "--length-policy", "laya_compatible", "--max-cases", "1", "--timeout-seconds", "180"],
  "WorkingDirectory": "<本次任务根目录>",
  "OutputDirectory": "<新推理执行目录>",
  "TimeoutSeconds": 200,
  "CleanupSeconds": 10,
  "MaximumProcesses": 16,
  "MaximumOutputBytes": 4194304
}
```

```text
Sezika.Tooling process run --request <执行请求.json> --output <新外层回执.json> --timeout-seconds <含回收预留的期限>
```

Linux 必须声明已核验的 C# `BrokerExecutable`；嵌套执行另按准确路径/SHA 固定 `NestedBrokers`。WSL 同时保留 Windows 外层与 Linux 内层回执，独立检查两者，完整调用合同见[当前工作流](tooling-current-workflow.md)。

新独立 `Sezika.ReferenceTool` 的 Laya 路径使用 `port-derived.v2`：`upstream_executed=false`，原始上游源码只作取证；原 `contract.v1.json` 与上游来源门槛保留。新参考、实际前向、数值比较和评分分别执行。`oracle batches` 协调器固定程序/源码、五项模型资产与输入清单，恢复只接受完整 hash 与执行身份闭合的已完成批次；当前验收状态按 ROADMAP 与独立证据表述。

`--output` 必须是不存在的新目录，且不能位于模型目录内。最终文件为该目录下的 `capture.json`。内部截止时间和 Ctrl+C 可取消运行；已完成行仍保存，未处理的 ID 写入 `unprocessed_case_ids`。外层进程超时应略大于内部截止时间，为资源回收和写入证据留出时间，二者均不得超过授权上限。

| 参数 | 范围与默认值 |
| --- | --- |
| `--backend` | `scalar`、`simd`、`cuda`；默认 `simd`。CUDA 失败不会回退 CPU。 |
| `--length-policy` | `strict`、`laya_compatible`；本对照工具默认 `laya_compatible`。Sezika 产品请求仍默认 `strict`。 |
| `--max-cases` | 1–46；默认 1。未指定 ID 时选取参考文件中的前 N 行，并明确保存全部所选与未覆盖 ID。 |
| `--case-ids` | 逗号分隔的明确 ID；数量必须不超过 `--max-cases`，不存在或重复的 ID 会使运行失败。 |
| `--timeout-seconds` | 1–1800；默认 180。每个类型化请求另受最多 5 分钟的运行时预算限制。 |
| `--require-aot` | 可选无值开关；在读参考、加载模型和创建输出目录前拒绝托管宿主或启用了动态代码的进程。必须直接启动本次发布的原生可执行文件。 |

AOT 验收使用 `dotnet publish -c Release -r <win-x64或linux-x64> --self-contained true -p:PublishAot=true` 的原生发布物，并传入 `--require-aot`。`implementation` 保存 RID、两项动态代码能力、`managed_host_detected`、是否要求 AOT 和实际进程文件 SHA-256。`PublishAot` 的中间托管 DLL 会继承禁用动态代码的 runtimeconfig，因此两项能力均为 false 本身不能证明原生执行；门槛同时检查托管宿主的 `TRUSTED_PLATFORM_ASSEMBLIES`，并在独立证据中记录发布命令、二进制格式和依赖。

输入契约可在独立发布的 `Sezika.Tests` 原生程序中使用 `--prompt-contract-aot` 验收。该入口先检查原生执行门槛，再运行原有输入、JSON、strict 拒绝与取消检查；它不替代真实 scalar/SIMD/CUDA 推理 capture 和冻结容差比较。

## 身份、失败与支持范围

输入与合同文件按实际字节计算 SHA-256，模型/分词器由运行时固定版本加载器校验。参考 capture 必须携带相同固定资产、上游源码版本、输入/合同哈希、1024 总长、256 prefix 及 temperature=1 策略。输出的 `provenance` 保留比较身份；`implementation` 单独记录实际后端、.NET/操作系统/架构、进程信息和本次执行二进制哈希，避免误称 C# 实现运行了上游源码。

Choice 仍仅支持 2–32 个候选，Score 仍仅支持 2–10 个等级。输入集合中的 1/33 个 Choice 候选和 1/11 个 Score 等级会得到结构化 `invalid_question` 失败以及 `contract_differences`，不会被跳过、改写或伪装为推理成功。Boolean 默认 criteria 和显示 labels 通过公共类型合同处理；非法 key、重复或空显示标签保留校验失败。

长度兼容模式保留实际截断诊断。严格模式拒绝任何 instruction、option 或 state token 丢失，在失败行保留诊断，并明确记录长度策略差异。失败行的 logits、概率和预测均为 null；adapter 与实际引擎输入不一致等工具自身错误会中止运行，使 capture 标为不完整。

`measurement_status=completed` 只表示所选每一行都完成了推理或明确拒绝，不表示数值相等。完整 46 行比较可能因明确的 API 范围差异失败；支持范围内的数值验收必须明确选取相同参考/实际 case ID，并报告对原始 46 行的覆盖率。不得把四项范围拒绝从结果里静默删除后宣称完整通过。真实对齐结果由 `Sezika.OracleCompare` 按预先冻结的容差产生。

比较器支持可选第 7 参数 CSV case ID。它先校验两份完整文件的身份和全部行，再比较明确选择的子集，同时保存完整文件哈希、原始行数和排除 ID。因此支持范围比较直接使用原始 capture，不需要另造去掉失败行的参考文件。`passed=true` 仅表示所选 case 比较通过；`full_manifest_passed` 与相对于原始清单的覆盖率另行给出。

本文描述工具合同。构建、实测数值、Native AOT 与性能的验收状态分别以本轮产生的日志和比较报告为准。

2026-09-26 的 `win-x64` / Ubuntu WSL2 `linux-x64` 实际发布命令、原生长输入结果、托管宿主负向检查及清理记录见[独立 AOT 回归报告](evidence/input-aot-2026-09-26.md)。
