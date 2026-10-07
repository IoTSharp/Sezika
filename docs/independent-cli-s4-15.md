# 显式独立 decision-v1 CLI

本次实现覆盖 S4-15 / S6-02 的显式工程入口：`inspect` 和 `predict` 可选择独立 encoder、自有 head 与 `decision-v1` 请求。默认格式仍为 `legacy-laya`；独立模型通过 S4-08 的质量、校准及部署验收后才可另行迁移默认入口。本页不代表正式 CLI 发布、真实独立模型质量或真实权重的两 RID Native AOT 推理已通过。

2026-10-07 已完成实际验证：managed CLI checks41项、Windows Native AOT tiny fixture42项通过，后者核对动态代码支持/编译均false且TPA为null；产品 CLI 原生发布和help通过，无编译/trim/AOT警告。完整执行与边界见 [监督证据](evidence/supervision-2026-10-07.md)。

## 命令与资产绑定

```powershell
# 在 PowerShell 7 中运行；模型和 head 均保留为外部文件。
dotnet run --project src/Sezika.Cli -- inspect `
  --model-format independent-v1 --model <独立模型目录> `
  --head <head.asset> --head-binding <train-original-report.json>

dotnet run --project src/Sezika.Cli -- predict `
  --model-format independent-v1 --model <独立模型目录> `
  --head <head.asset> --head-binding <train-original-report.json> `
  --input <decision-v1-request.json> --deadline-seconds 30 `
  --command-timeout-seconds 300 --input-timeout-seconds 30 --max-tokens 1024
```

`--head-binding` 首选直接使用训练工具生成的 schema 2 报告。CLI 按完整必需字段、重复字段、未知字段、字段类型及分母校验读取报告，不从文件名推断身份。报告的 `identity`、`feature_sha256` 与 `head_sha256` 传入 `IndependentDecisionV1Runtime.Load`；head 必须同时匹配 model ID、encoder/tokenizer/data manifest SHA-256、hidden size、`decision-v1` / `strict` 协议、特征 SHA-256 和实际 head 文件 SHA-256。

兼容的 schema 1 精简绑定如下，各 hash 必须替换成实际受核对的值。它不是旧 schema 1 开发训练报告，两种结构不可混用。

```json
{
  "schema_version": 1,
  "identity": {
    "model_id": "sezika/mmbert-base-independent-s4",
    "encoder_sha256": "<64 个十六进制字符>",
    "tokenizer_sha256": "<64 个十六进制字符>",
    "data_manifest_sha256": "<64 个十六进制字符>",
    "hidden_size": 768,
    "protocol": "decision-v1",
    "length_policy": "strict"
  },
  "feature_sha256": "<64 个十六进制字符>",
  "head_sha256": "<64 个十六进制字符>"
}
```

CLI 产品入口固定调用已有独立 loader，不接受 synthetic package，也没有 fixture 开关。缺失或错误独立资产返回结构化错误；独立入口不会选择 legacy loader。未指定 `--model-format` 的旧命令仍使用现有 legacy 入口，也可显式写 `--model-format legacy-laya`。

## 输入、结果与失败

独立 `predict` 从 `--input` 文件或 stdin（省略 `--input` 或使用 `-`）读取 `decision-v1` JSON；UTF-8 BOM 可接受。请求 schema、模型身份、重复键、未知字段及三题型候选由已有严格 parser / runtime 校验。Choice、Score、Boolean 使用同一完整逐题 encoder 与 head 推理路径。

输出使用静态类型和 source-generated System.Text.Json。独立结果报告 model/encoder/tokenizer 身份、`decision-v1`、候选 ID/概率、selected index 和 distribution concentration，校准状态固定为 `uncalibrated`。概率与分布集中度不代表实际正确率或业务执行授权。独立 runtime 当前没有校准拒答策略，因此指定 `--minimum-concentration` 会返回 `cli_option_invalid`。

独立 `inspect` 实际装载并释放 encoder 与 head，验证完整 loader 合同与双方绑定，成功报告 `runtime_load_verified: true` 和 `quality_verified: false`。它会读取、校验并分配模型权重；没有把文件存在检查写成推理或质量验收。加载失败在 stderr 写 `{ "code": ..., "message": ... }`，退出码为 2；成功 JSON 写 stdout，退出码为 0。legacy inspect 继续使用既有资产报告格式。

## 有界执行

| 预算 | 默认 | 范围与行为 |
| --- | --- | --- |
| 命令总墙钟 `--command-timeout-seconds` | 1800 秒 | 1–1800 秒，覆盖输入、绑定、装载与推理 |
| 请求读取 `--input-timeout-seconds` | 30 秒 | 1–300 秒；超时返回 `cli_input_timeout` |
| 单次 runtime 推理 `--deadline-seconds` | 30 秒 | 1–300 秒；独立 runtime 返回自身 deadline 错误 |
| 独立 token 上限 `--max-tokens` | 1024 | 2–1024，与固定独立 encoder 预算一致；严格拒绝截断 |
| legacy token 上限 `--max-tokens` | 4096 | 1–4096，保留既有默认 |
| 请求字节 | 1 MiB | 文件预检查及有界读取；取消 token 每次读取传递 |
| head 绑定报告 | 64 KiB | JSON 深度最多 8，完整报告最多 6 个开发分组 |
| 参数 | 32 项 | 重复选项、缺值、未知格式/选项和无效预算拒绝 |

Ctrl+C 和命令总预算经 linked CancellationToken 传给文件读取、hash、loader 与 runtime。总预算取消返回 `decision_cancelled`；错误路径由 `using` / loader 的异常清理释放已装载资产。CLI 不创建外部子进程。已有核心 loader 的 block 取消粒度仍决定实际响应延迟，本次没有取得真实大权重装载取消延迟数据。

## 验证范围

`tools/Sezika.CliChecks` 是独立检查程序，由监督者串行运行构建和检查。它从真实 `IndependentTrainingWorkflow.Run` 取得 checkpoint/head/schema 2 报告，再使用同一 CLI application 入口执行 inspect/predict。成功路径通过仅检查程序集可见的 internal loader seam 装载 tiny C# ModernBERT 与实际 head 文件；产品 `Main` 仍调用固定 loader，并另测 synthetic package 被拒绝。

检查覆盖格式选择和旧默认、三题型 JSON 结果、完整报告直接消费、错 head/feature hash、重复请求/绑定字段、未知报告字段、模型身份和 token budget、非法参数、预取消、阻塞输入超时及命令总超时。整个检查程序有 30 秒 / 80 项上限，临时 fixture 文件只在本任务 `.artifacts/supervision-20261007/cli/cli-check-<GUID>` 内创建，并在 finally 逐个核对绝对路径删除，最后仅删除空的专属目录。

此 fixture 只验证工程合同、实际 C# 算子与资源生命周期，不是实模输出、人工数据准入、多语言质量、校准、上游数值 oracle、CUDA、性能或两 RID AOT 推理验收。构建、运行与 AOT 检查的真实结果及日志由监督者在本轮证据中记录，不在执行前宣称通过。
