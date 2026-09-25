# Laya 独立参考与 C# scalar 核心矩阵对齐（2026-09-25）

固定 Laya CPU FP32 oracle 与 Sezika C# scalar FP32 的 **18 条核心矩阵全部通过预先冻结的数值容差**：3 种题型 × 中英 2 种语言 × 短/中/长 3 种长度。18 行均实际 answered，token、marker、候选顺序及离散预测一致，0 个比较差异、0 个近并列 case。相对于原始 46 条输入清单，覆盖率为 **18/46 = 39.1304%**；`complete_input_coverage=false`、`full_manifest_passed=false`。

该结果来自真实 scalar 模型推理。没有复制参考 tokens 或数值作为 C# 输出，也没有为此分支修改原始参考文件或冻结容差。这里只验收所列 18 条 scalar 输入的数值复现；不构成 scalar 全部 46 条边界、失败语义、语言质量、校准、Native AOT 或性能验收。

## 执行顺序与结果

先实际捕获并比较 `choice-en-short` 单条输入；它通过后才启动 18 条矩阵。共运行 **2 次 OracleCapture + 2 次 OracleCompare**，没有构建或重试。两次 capture 均明确指定 `--backend scalar --length-policy laya_compatible`；核心矩阵明确指定原 manifest 前 18 个 `--case-ids` 与 `--max-cases 18`，参考输入使用原始 `full-01/capture.json`。

| 范围 | answered pair | failure pair | 最大 logit 绝对误差 | 最大概率绝对误差 | 结果 |
| --- | ---: | ---: | ---: | ---: | --- |
| 单条 smoke，1/46 | 1 | 0 | 0.000030517578125 | 0.000000000900044261342714 | 通过冻结容差 |
| 核心矩阵，18/46 | 18 | 0 | 0.000038387253880500793 | 0.000008816924735144394 | 通过冻结容差 |

比较规则仍为 `abs(actual-reference) <= absolute + relative * abs(reference)`：logits 的 absolute/relative 为 0.0005/0.0001；概率为 0.0001/0.0001；Score 为 0.001/0.0001。Choice/Boolean 离散预测要求精确相同；近并列阈值 0.001，没有放宽失败条件。

| 题型与语言 | 短输入实际 token 数 | 中输入实际 token 数 | 长输入实际 token 数 |
| --- | ---: | ---: | ---: |
| Choice / en | 61 | 384 | 960 |
| Choice / zh | 60 | 384 | 960 |
| Score / en | 65 | 384 | 960 |
| Score / zh | 62 | 384 | 960 |
| Boolean / en | 49 | 384 | 960 |
| Boolean / zh | 48 | 384 | 960 |

对应 case ID 完整保存在 capture 的 `selected_case_ids` 和比较报告的 `requested_case_ids`。比较器读取两份原始完整文件，先校验身份及所有行，再按明确 CSV 选择 18 个 ID；报告保留参考总行数 46、实际总行数 18，以及其余 28 个未覆盖 ID。没有人为裁剪参考文件以制造“完整通过”。

## 可复核文件与身份

核心矩阵的原始字节已归档：

- [scalar-capture.json](laya-parity-2026-09-25/scalar-capture.json)，SHA-256 `af9e201c7d804572e979896c7e8c9c0ab36c3719016f900c33c5e65cd9f37381`。
- [scalar-core-comparison.json](laya-parity-2026-09-25/scalar-core-comparison.json)，SHA-256 `6c1f8939ae64201c226a68fff444e9ac473782d9f2362f5daefe5a19871a9623`。

原始完整上游参考 SHA-256 为 `bf0d537305149672f07d34dc6b3e2d3614aeb2c26aa6d11aa7902578100c53fa`。输入清单 SHA-256 为 `d0fb41807322f27e60fe5eabd2a146d4dc8ce31b326eb4f292bcc20ab85b4a7a`，冻结合同 SHA-256 为 `771ca781c52f1d6f9cb22859bed007ef483a54a2543c644de33f88d6e0687eee`。

模型为 `convaiinnovations/laya-multilingual`，revision `052592a15d198d9ad47da779604259b10b47b7aa`；上游 source revision `4066d5d5fbf08b66c6757ddeedbd797bd7655bc0`。固定权重和 tokenizer 哈希在归档 capture 的 `provenance` 中，加载时由 Sezika 运行时验证。温度策略为 checkpoint 固定 1，无覆盖。

运行环境：PowerShell 7.6.6、.NET 10.0.11、Windows 10.0.26200、X64，使用已构建的托管/JIT 工具。执行二进制身份：

- `Sezika.OracleCapture.dll`：`0d50584b8357ab2cbf42fa2e02ae33cb8ce1894c8c9de6e7b1d809c85cea3ba6`。
- `Sezika.dll`：`738ba1bcb6a186d6081785c994774d05ef0e2926a5df0c9b46100da3939660a6`。
- `Sezika.OracleCompare.dll`：`56fa3487f5f7af5b3c1e62cd9eabe3dd0767139c159ef1664125370b73820246`。

单条前置 gate 的本地文件保留在 `.artifacts/laya-oracle-work/csharp-scalar-smoke-01`。其 capture SHA-256 为 `2d63ee1f90c20dc900af891d193712d3a09a1405d8073829a8db6ebde8492c43`，比较报告 SHA-256 为 `5644b80072696a46d36130156b3fdc115074f57eb8aa115f7ca08c1a61437836`。核心原输出保留在 `.artifacts/laya-oracle-work/csharp-scalar-core-01`。

## 有界进程与清理

所有工具均由 `tools/Invoke-BoundedProcess.ps1` 启动。capture 的内部截止时间为 1700 秒、外层硬上限为 1800 秒；compare 的内部截止时间为 30 秒、外层上限为 60 秒。外层记录秒数仅用于超时与任务取证；本轮可能与其他验证并行，不作为性能测量。

| 进程 | PID | 启动时间（UTC） | 直接启动方 PID → 上级 PID | 外层 result 秒数 | 退出码 |
| --- | ---: | --- | --- | ---: | ---: |
| scalar smoke capture | 65636 | 14:23:18.4314042 | 42968 → 53660 | 11.795 | 0 |
| scalar smoke compare | 78768 | 14:24:34.9375863 | 62144 → 53660 | 2.174 | 0 |
| scalar core capture | 68324 | 14:25:04.5039282 | 69176 → 53660 | 805.786 | 0 |
| scalar core compare | 45236 | 14:38:48.2642221 | 54788 → 53660 | 1.504 | 0 |

4 次运行的 result、identity、stdout、stderr 共 16 个文件逐字节归档至 [processes](laya-parity-2026-09-25/processes)，对应前缀为 `20260925-142317-632-oracle-capture-scalar-smoke`、`20260925-142434-247-oracle-compare-scalar-smoke`、`20260925-142503-840-oracle-capture-scalar-core`、`20260925-143847-845-oracle-compare-scalar-core`。每个 result 的 `CleanupErrors` 均为空，没有按进程名称清理，也没有删除用户文件。

两次 capture 的 CIM 观测父 PID 与启动方记录相同。两次很短的 compare 在 CIM 查询前已退出，`ObservedParentPid` 为 null；改进后的 runner 明确以 `Process.Start caller` 保存直接启动方 PID，并保存 launcher 自身身份和上级 PID，没有把它伪装为额外的原生进程观测。退出状态及文件/参数记录仍完整保留。
