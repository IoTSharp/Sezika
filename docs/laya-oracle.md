# 固定 Laya oracle 导出合同

S3-08 已在固定源码、现有权重与隔离 Python 环境中完成 1 条极小验证及完整 46 条真实参考捕获：42 条 answered，4 条 validate/invalid_question。版本化 [reference.cpu-fp32.v1.json](../tests/fixtures/laya-oracle/reference.cpu-fp32.v1.json) 保留原始字节；命令、版本、哈希、进程和环境边界见[执行证据](evidence/laya-oracle-2026-09-25.md)。参考捕获与 C# 数值门槛分别报告，唯一阶段计划仍在 [ROADMAP](../ROADMAP.md)。

## 独立性与来源

导出器在 [tools/Sezika.LayaOracle](../tools/Sezika.LayaOracle/README.md) 内，依赖固定 [Laya 0.3.20 源码提交](https://github.com/NandhaKishorM/laya/tree/4066d5d5fbf08b66c6757ddeedbd797bd7655bc0)。上游模型、tokenizer 与配置沿用 Sezika 已锁定的 `052592a15d198d9ad47da779604259b10b47b7aa` 资产；核对五个文件 hash 后才加载。上游代码与模型各自 Apache-2.0，底层 encoder 的 MIT 来源仍按既有 [模型来源记录](model-source.md) 管理；Python 包的传递许可随执行环境清单独立审核，不合并为项目许可声明。

Python 是开发期 oracle 的明确例外，不属于 Sezika 推理或训练运行时。导出器直接调用上游 `Agent._check_question`、`_to_internal`、`_encode_state`、`_forward`、`_decode_answers` 及 `common.build_sequence/collate_items/serialize_state/render_options`。固定提交的私有 API 是审查点，源码漂移会在模型加载前被拒绝。没有重写 encoder、head、候选构造或截断算法。

上游公开答案将数字 round 至四位。工具保留整个上游答案，并使用同一 `_forward` 原始 logits 和相同 NumPy softmax 表达式记录未舍入概率；温度先验证固定为 1。Score 使用未舍入概率算 `Σ i×pᵢ`，Boolean 原始上游返回 `P(true)`，本工具额外按事先冻结的 `P(true) >= 0.5` 产生 bool。`action.act_probability` 仅留作上游证据，绝不是业务执行许可。

## 输出形状

顶层 `schema_version` 固定为 `sezika.laya-oracle.v1`。`provenance` 包含模型/源码 revision、权重/tokenizer hash、输入/合同文件原始字节 hash、1024/256 长度策略和固定温度策略；这些字段必须在 C# 对照文件中匹配。另保存 Python、直接和传递包版本、PyTorch 构建信息、CPU FP32 后端、线程数、seed、导出器和 source lock hash。六项直接依赖已在本机 CPython 3.12.10 环境实跑验证，尚未提供完整 wheel hash lock；不能声称跨环境复现已验证。

每个 `cases` 项目包含：

| 字段 | 合同 |
| --- | --- |
| `id` / `primitive` / `status` | 冻结 ID；`choice`、`score` 或 `boolean`；`answered` 或 `failed` |
| `input` | 配方展开后真实 `state`、Laya 公共题目 `question`、语言及长度参数；禁止拿不同文本输出直接比较 |
| `token_ids` / `marker_positions` / `candidate_labels` | 上游真实数组；Choice 保留标签顺序，Score 为 0 起十进制字符串，Boolean 为 false/true |
| `raw_logits` / `probabilities` | 逐 marker 原始 logits 和未舍入概率；非有限值是失败 |
| `prediction` | `choice_label`、`score`、`boolean`、`probability_true` 四个可空字段，仅本题型字段赋值 |
| `failure` | 归一化 `stage/type`，保留 `upstream_type/upstream_message`；成功为 null |
| `sequence_diagnostics` | state/说明/候选渲染、decoded 序列、原始和保留 token 数、state 保留起点和截断方向 |
| `upstream_answer` / `act_probabilities` | 上游四位舍入答案和原始 action 概率，仅作证据，不用于替代未舍入数值比较 |

上游非法题目映射为 `validate/invalid_question`，构造阶段 ValueError 映射为 `encode/sequence_budget_exceeded`；其他阶段仍保留原始异常以审阅。OOM、非有限结果与一般推理失败分别记录 `resource_exhausted`、`non_finite_output`、`inference_failed`。未来 C# 导出应依据自己的实际失败码归一化，不能为满足比较器强行改写真实语义。失败行没有概率和预测；它可以具有失败前已经真实获得的 token/marker。

导出整体的环境、资产、材料配方生成异常与上游单题失败分开：整体失败留下 `run.json` 和 partial，没有成功的最终 capture。`measurement_status=selected_cases_only` 只覆盖所选前 N 条；完整导出是 `complete`，都不表示数值比较通过。partial 文件不能作为整套完成证据。

## 冻结比较门槛

[contract.v1.json](../tests/fixtures/laya-oracle/contract.v1.json) 在读取任何本轮 oracle 输出前固定：token、marker、语义标签和真实输入精确一致；数值满足 `|actual-reference| <= absolute + relative×|reference|`。

| 比较量 | absolute | relative |
| --- | ---: | ---: |
| raw logits | 0.0005 | 0.0001 |
| 未舍入概率、P(true) | 0.0001 | 0.0001 |
| Score 期望 | 0.001 | 0.0001 |

概率和误差上限另为 0.000002。Choice 和 Boolean 预测要求完全相同；参考前二 logits 差不超过 0.001 时标注近并列，但不把不一致自动通过。量化路径不在此合同范围。这里是待测量的工程容差，不是实测误差或精度承诺；若失败，先调查而非事后放宽 v1。

## 有界执行与证据

最多 64 条、每条 1 题、最多 64 候选，输入 JSON 不超过 1 MiB，state 不超过 131072 字符/16384 tokens，总超时最高 1800 秒。字符串长度配方最多 4096 次重复、32 次二分探测，比较条件保证区间严格缩小；只有真实 tokenizer 观测值恰好命中目标才接受。上游 BPE 不保证对任意字符串单调，未命中会失败，不能换成近似边界。本轮获准先运行 1 条短输入，再运行 46 条完整清单；所有边界配方均精确命中。

PowerShell 7 入口调用既有 `Invoke-BoundedProcess.ps1`，保存 PID、启动时间、命令行、父链及 stdout/stderr，并对任务子树有界回收。Python 同时检查墙钟、Ctrl+C/取消文件，逐题输出进度；正在运行的原生调用由外层超时最终约束。没有线程池、数据加载 worker、后台服务、网络请求或自动重试。Git 身份检查只通过 PATH 中的 git 启动短命只读进程，记录其 PID、参数和父进程，单次最多 10 秒。

上游 `_fix_tokenizer_config` 可能改写 config，所以输出目录包含独立配置工作副本；原始模型目录只读。工具不清理输出或模型文件；工作副本中的 hardlink 只指向不会被上游写入的权重与 tokenizer 数据。跨卷复制可能增加磁盘占用，版本与 config 修补前后 hash 均留证。

后续获准执行应保留 runner 日志、capture、环境清单和源文件 hash，先核对完整序列/marker，再进行 C# 输入修复和真实数值比较；语言质量、校准、Native AOT 与性能各自单独验收。
