# Linux 真实 Numeric26 独立收口（2026-10-14）

现有真实 `compare-port` 结果为 **26/26、884,736 元素、零失败**，`fixture_only=false`、`accepted_port_derived_reference=true`、`accepted_independent_reference=false`、`upstream_executed=false`。对象是固定 `independent-smoke-46-v1` 的单个 46-token 输入、25 个 checkpoint 与 marker vectors。它不表示 Laya 全清单通过。

原 Linux CPU producer 从原始 mmBERT 权重生成新的 C# port-derived reference，耗时 22.171715 秒，记录的峰值 RSS 为 95,244,288 B，数值线程为 1；producer 使用既有 Linux managed JIT 来源构建。原比较由已固定 SHA 的 Linux Native AOT `Sezika.IndependentNumericCompare` 执行，耗时 18.1636068 秒。它读取固定原 actual；本次独立收口没有重新执行 actual forward、capture、compare、SDK 或 GPU。

| 固定对象 | SHA-256 |
| --- | --- |
| 原 actual | `681497c6b6f1a599804cac492b3a1094c623f825341563020b8c6f534a44dfa9` |
| 新 Linux reference | `0f113d6be7bef80de8383cdd68eaf8ad5cb1c1b9dd060b582d2f746c91320977` |
| 新 Linux run contract | `76d16ac08a967c33d210015cab8e5e0fbdcbe4da5c8dd8a3ad01a55c32c9d8ca` |
| 原权重 ZIP 容器，1,231,188,142 B | `8ea64ec1ea4eb8fca0fc14b69a2ae571de6bfbc25fd214bb932dd4aba6a3a04e` |
| Linux AOT Numeric，10,357,528 B | `aa5be70cc5ba6e91766116eba5a70dec26080b8f84168ba68a29e889c5089bfc` |

2026-10-14 15:54:19–15:54:24 UTC 完成新的独立读取：391 个具名文件含原 301 项输入与 90 项证明/失败记录，原 301 项零漂移，所有固定 SHA 零不匹配；另重新核对原 ZIP 内 134 个张量区间共 1,227,758,592 B。总读取量 2,514,439,239 B，耗时 5.0272357 秒，在 400 文件、4 GiB、60 秒边界内。这是本轮新结果，没有把旧检查改成新检查。

独立 packet 核查绑定了显式 port-proof、upstream3、程序来源24、生成来源221、runtime5、原 Linux producer PID562186 的 ticks/boot/scope/实际 argv、两数值阶段的内外四份成功回执、EOF、退出码及 cleanup。固定 template/run contract 的容差相同。2026-10-14 15:59:49 UTC 对原数值任务 45 个 Windows/Linux 身份重新读取，匹配存活0、UNKNOWN0；本轮未启动新的 C# 审计客户端或常驻进程。

| 精确出处（Linux 新根 `/home/maikebing/sezika-linux-real-numeric26-v1-20261014`） | 读取字段/范围 |
| --- | --- |
| `comparison.json` | `status`、`results[0..25]`、`compared_count`、`element_denominator`、失败分母与全部 acceptance flags |
| `export-receipt.json`、`reference/capture.json` | 完成状态、25 checkpoint、134 原张量、来源/runtime/build/request/process pins |
| `inputs/port-proof.json`、`inputs/run-contract.json` | 全部显式证明身份、未改变的固定容差 |
| `capture-native.json`、`compare-native.json` | `Processes`、`Launcher`、PID/birth/ticks/boot/argv/parent、EOF、退出及 cleanup |
| 旧 retained 根 `/home/maikebing/sezika-t8-proof-v3b-20261014/build-summary.json` | 真实 managed JIT 构建、ProgramSources24、GeneratedSources221、RuntimeFiles5 |

旧证据根 `C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\linux-real-numeric26-execution-v1` 保持。原 Host 保留变量准备失败、snake_case 读取失败、三目录拼成一个参数的 comparator-audit FAIL 都保留；额外一次纠正 audit 由 root 单独授权，v2 为8身份/live0/unknown0。原资源回读于14:31:07.5196582 UTC完成，超过14:30截止67.5196582秒；原窗口最终 summary 未创建、额外完整 final rehash 未完成的历史状态不变。

诊断记录限制：本轮首次只读 `rg` 子进程没有单独捕获历史 PID/birth，保留 UNKNOWN；其同步父 shell 已退出，当前直接子进程数为0。该缺口不被表述为数值模型进程身份缺失，也不宣称所有诊断子进程历史记录齐全。

Laya 仍为1/46、缺45、full=false、upstream=false；没有新增完整算子对齐、模型质量、训练、校准、性能或生产部署验收。权威的本轮派生文件为同目录 `independent-final-summary.json`、`current-full-hash-receipt.json`、`current-full-file-readback.json`、`current-original134-readback.json`、`independent-original-packet-binding.json`、`current-exact-process-and-resource-readback.json` 与 `exact-evidence-index.json`。

Root于2026-10-15（Asia/Shanghai）核交付；原始副本及精确出处见 [验收清单](tooling-linux-real-numeric26-2026-10-15/acceptance.json)。未复制模型权重或修改旧证明。
