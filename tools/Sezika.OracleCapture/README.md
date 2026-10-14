# C# 实际推理 capture

读取固定 Laya oracle 的已展开输入，以 Sezika 类型化 runtime 和实际 scalar/SIMD/CUDA 后端生成独立对照。参考数值不进入 adapter；后端实际输入与公共 prompt builder 逐项核对。

参数、进程预算、支持范围差异和证据语义见 [操作说明](../../docs/oracle-capture.md)。输出 `capture.json` 支持历史 `sezika.laya-oracle.v1` 与独立 C# 参考对应的 `sezika.laya-oracle.actual.v2` 合同；完整运行成功不等同于数值比较通过。

当前执行入口为 `Sezika.Tooling process run` / `ExecutionRequest`，字段见 [C# 调用合同](../../docs/tooling-current-workflow.md)：准确程序及代码/runtime 身份、结构化 argv、工作目录、新日志目录、内外期限和回收分别绑定。先在相同冻结输入上执行一条，再扩大到明确的最多46条。参考使用已冻结且来源经过校验的独立捕获；本工具生成的是 C# 实际捕获，不能代替独立参考。

2026-10-14 已在真实原始 Laya 五个资产上完成 Windows managed JIT 的 `choice-en-short` 单条 SIMD 捕获，显式预算2 GiB，并与独立 C# port 参考完成1个 answered pair 的冻结容差比较。完整46条清单保留，覆盖1/46、缺45条，`full_manifest_passed=false`、`upstream_executed=false`。六项真实资源参数拒绝和七类深层证明负控已验；后续Linux单条与Native AOT范围见下方T8追加。全46条、语言质量、校准和性能仍未验收。准确产物、原始FAIL及资源闭环见 [最终证据追加](../../docs/evidence/tooling-oracle-capture-final-2026-10-14.md)。

可选 `--maximum-resident-bytes <1048576..4294967296>` 使用十进制 `long`，缺省为原有的4 GiB。显式值传给实际引擎的 `DecisionResourceBudget.MaxResidentBytes`，并写入 `capture.json` 的 `implementation.maximum_resident_bytes`。重复、缺值、非整数、溢出或越界参数在读取模型前拒绝。此项是既有 `DecisionResourceBudget` 合同，不是操作系统的硬内存上限；它不限制加载模型前的进程内存。


2026-10-14 T8 追加：Windows/Linux 三个工具的 Native AOT 发布均已完成。Linux Capture 真实单条 AOT SIMD 与独立 C# port 参考及原生 Compare 通过，覆盖仍1/46；Windows Capture 仅模型前 guard。Windows Numeric真实26项通过，Linux Numeric仅tiny1 fixture、真实26项NOT_RUN。资源审计通过范围为61份完整回执，原MP32 UNKNOWN和严格审计FAIL单列保留。准确范围见[最终证据追加](../../docs/evidence/tooling-oracle-capture-final-2026-10-14.md)。
