# Laya CPU FP32 独立参考捕获：2026-09-25

在用户明确授权的源码准备、隔离依赖安装和有界推理范围内，固定 Laya 0.3.20 原实现已完成 1 条极小试运行及随后 46/46 条真实捕获。完整捕获包含 42 条 `answered` 和 4 条 `validate / invalid_question`，没有环境失败、推理失败、超时或工具重试替换结果。本文只证明独立参考环境和捕获完成；C# token/数值对照由其单独报告证明，语言质量与校准另行验收。

## 版本化交付与身份

- 真实参考文件：[reference.cpu-fp32.v1.json](../../tests/fixtures/laya-oracle/reference.cpu-fp32.v1.json)，695,430 字节；从本地 `full-01/capture.json` 逐字节复制并核对 SHA-256，未编辑输出数值、case 或来源字段。
- 参考文件 SHA-256：`bf0d537305149672f07d34dc6b3e2d3614aeb2c26aa6d11aa7902578100c53fa`。
- 原始输入 SHA-256：`d0fb41807322f27e60fe5eabd2a146d4dc8ce31b326eb4f292bcc20ab85b4a7a`。
- 冻结合同 SHA-256：`771ca781c52f1d6f9cb22859bed007ef483a54a2543c644de33f88d6e0687eee`。
- 测前 source-lock SHA-256：`901527bdd7da7732c2d12903a45dcc1984ac2187e14214ae780286c585f2e287`。
- Python 导出器 SHA-256：`0269db702235a42c12d1c84fa781264b0d63c5427e051f286dfec7d7b9abf279`。
- 上游 Git 提交：`4066d5d5fbf08b66c6757ddeedbd797bd7655bc0`；导出前检查 HEAD、版本与 clean status；`agent.py` SHA-256 为 `1473ffd335a1becf55bf7aadde6d31f9d6be27862f5deaa5f4ebd3451fa154e3`，`common.py` 为 `4ab41be6a4d9fdbad74eccf4f64c43e2c7404d227988d97ed8233ded8b182cb1`。
- 模型 `convaiinnovations/laya-multilingual` revision：`052592a15d198d9ad47da779604259b10b47b7aa`；使用现有 `.artifacts/models/laya-mmbert`，没有下载模型权重。权重 SHA-256 为 `9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204`，tokenizer 为 `609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f`。

`source-lock.json` 保持测前内容，因此其 `prepared_not_executed` 是原始锁文件的写入时状态；本次完成事实由本证据、参考文件和原始进程结果记录。没有因观察到结果而改变输入、依赖版本或冻结容差。`.gitattributes` 对版本化参考文件禁用换行转换，保留测量字节与哈希。

## 实际环境

PowerShell **7.6.6**，Windows 11 `10.0.26200` / AMD64，uv **0.11.19**，CPython **3.12.10**。六个直接依赖均严格符合冻结版本：

| 依赖 | 安装版本 |
| --- | --- |
| torch | 2.8.0（实际构建 `2.8.0+cpu`） |
| transformers | 4.57.1 |
| tokenizers | 0.22.2 |
| safetensors | 0.6.2 |
| huggingface-hub | 0.35.3 |
| numpy | 2.3.3 |

参考文件 `provenance.dependencies` 保存全部 25 个安装包版本，`torch_build` 保存具体 CPU wheel 的构建信息。依赖组合已经实际加载并推理，但没有声称完成所有传递 wheel 的 hash lock。此次 upstream 模型加载和推理均为 CPU FP32、单线程、batch size 1、seed 0、确定性算法，关闭 AMP、compile、FastLaya 和网络访问；`max_len=1024`、`head_max_len=256`，温度固定为 1。

任务目录：

- checkout：`D:\source\Sezika\.artifacts\laya-oracle-work\upstream`。
- CPython：`D:\source\Sezika\.artifacts\laya-oracle-work\python\cpython-3.12.10-windows-x86_64-none\python.exe`。
- 隔离 venv：`D:\source\Sezika\.artifacts\laya-oracle-work\venv`。
- 工作 cache：`D:\source\Sezika\.artifacts\laya-oracle-work\uv-cache`。
- 原始捕获：`D:\source\Sezika\.artifacts\laya-oracle-work\smoke-01`、`full-01`，各含 `run.json`、`capture.json`、partial 和模型配置副本。

Git fetch 直连出现 `Recv failure: Connection was reset`，随后仅该次 Git 调用设置 `http.proxy=http://127.0.0.1:7890` 成功。CPython 与依赖直连成功。没有改变全局代理或 PATH。uv 安装另创建了 `C:\Users\mysti\.local\bin\python3.12.exe` 启动 shim，并提示该目录不在 PATH；因为安装前未记录该外部路径的存在状态，未删除该文件。后续准备环境应把 uv 的 bin 目录也显式定向到任务目录，或使用禁用 bin 安装的选项。

## 实际命令与过程记录

所有外部安装、Git 和 Python 进程均通过 `tools/Invoke-BoundedProcess.ps1`，记录 PID、创建时间、命令行、父链和结果。以下命令从 `D:\source\Sezika` 的 PowerShell 7 执行；`Invoke-LayaOracle.ps1` 内部调用同一 runner。

```powershell
& tools/Sezika.LayaOracle/Invoke-LayaOracle.ps1 `
  -PythonPath 'D:\source\Sezika\.artifacts\laya-oracle-work\venv\Scripts\python.exe' `
  -UpstreamDirectory 'D:\source\Sezika\.artifacts\laya-oracle-work\upstream' `
  -ModelDirectory 'D:\source\Sezika\.artifacts\models\laya-mmbert' `
  -OutputDirectory 'D:\source\Sezika\.artifacts\laya-oracle-work\smoke-01' `
  -MaxCases 1 -TimeoutSeconds 180

& tools/Sezika.LayaOracle/Invoke-LayaOracle.ps1 `
  -PythonPath 'D:\source\Sezika\.artifacts\laya-oracle-work\venv\Scripts\python.exe' `
  -UpstreamDirectory 'D:\source\Sezika\.artifacts\laya-oracle-work\upstream' `
  -ModelDirectory 'D:\source\Sezika\.artifacts\models\laya-mmbert' `
  -OutputDirectory 'D:\source\Sezika\.artifacts\laya-oracle-work\full-01' `
  -MaxCases 46 -TimeoutSeconds 1800
```

以上输出目录已存在，复现必须另选新的任务目录。输入生成循环先检查了缩小区间的比较条件；极小输入成功后才执行 46 条。所有 recipe 的精确目标均实际命中；没有用近似 token 长度替代边界。

| 执行 | runner 根 PID / Python PID | 根进程开始时间 UTC | 超时 | Python 记录耗时 | runner JSON 耗时 | 结果 |
| --- | --- | --- | --- | --- | --- | --- |
| 1 条 smoke | 31368 / 72412 | 2026-09-25 13:59:46.548225 | 180 s | 12.921 s | 14.880 s | Succeeded，exit 0 |
| 46 条 full | 74476 / 63080 | 2026-09-25 14:00:28.677414 | 1800 s | 70.437 s | 72.596 s | Succeeded，exit 0 |

父链分别是 `28728 → 31368 → 72412`、`64352 → 74476 → 63080`；短命 Git 校验进程另外记录在各自 `run.json` 的 `child_processes`。runner 的 `CleanupErrors` 均为空，记录的进程均自然退出，无按名称杀进程。runner 仍存在其文档所述三秒采样窗口限制：窗口内出现并退出的中间进程不保证出现在树快照中。

本地详细日志保留于 `.artifacts/processes`：

- `20260925-135656-102-oracle-git-init.result.json`。
- `20260925-135744-792-oracle-source-fetch-direct.result.json`，失败原始证据。
- `20260925-135805-834-oracle-source-fetch-proxy.result.json`。
- `20260925-135744-830-oracle-python-install-direct.result.json`，600 s 上限。
- `20260925-135840-569-oracle-source-checkout.result.json`。
- `20260925-135840-604-oracle-venv.result.json`。
- `20260925-135901-222-oracle-dependencies-direct.result.json`，900 s 上限。
- `20260925-135946-519-laya-oracle.result.json`，以及同前缀 identity/stdout/stderr。
- `20260925-140028-648-laya-oracle.result.json`，以及同前缀 identity/stdout/stderr。

`.artifacts` 是本地运行证据目录，不宣称以上日志均随仓库分发。版本化参考文件包含完整环境和来源身份；本页记录关键进程事实以便审查。

## 捕获内容与边界观察

首条 `choice-en-short` 实际长度 61，marker `[14, 24, 33]`，候选顺序为 `z_delivered / a_transit / m_unknown`，raw logits 为 `[16.92413902282715, -4.59832763671875, -4.596772193908691]`。这是该固定输入的参考实现输出，不是语言质量正确率。

| 边界输入 | 未截断总 tokens | 实际输入 tokens | 观察 |
| --- | --- | --- | --- |
| sequence-255 / 256 / 257 | 255 / 256 / 257 | 255 / 256 / 257 | 精确命中，未截断 |
| sequence-1023 / 1024 | 1023 / 1024 | 1023 / 1024 | 精确命中，未截断 |
| sequence-1025 | 1025 | 1024 | 保留 state 的前 978 / 979 tokens |
| state-string-right-truncation | 1025 | 1024 | 保留 state 的前 990 / 991 tokens |
| state-conversation-left-truncation | 3255 | 1024 | 保留 state 的后 990 / 3221 tokens，起始偏移 2231 |
| options-48-token-cap | 125 | 125 | 两个 option 均从 102 tokens 截为 48 |
| prefix-instruction-truncation | 275 | 275 | instruction 从 2052 截为 32；32 个 option 各从 70 截为 6（另含 marker） |

三种题型、中英短/384-token 中/960-token 长输入均得到真实输出。Choice 1 / 2 / 32 / 33 候选和 Score 1 / 2 / 10 / 11 等级均被该上游版本接受；是否兼容 Sezika 产品限制必须通过显式契约处理，不能把范围差异伪装成推理数值通过。

四条失败分别为 `invalid-empty-choice`、`invalid-null-score-level`、`invalid-boolean-criteria-key`、`invalid-duplicate-boolean-display-label`，均为上游 `ValueError`；capture 保留完整原始错误，并且没有 token、logits、probabilities 或 prediction。输入本身带有工程边界标签，未附人工语义正确答案。
