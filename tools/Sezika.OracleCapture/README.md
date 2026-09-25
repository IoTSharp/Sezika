# C# 实际推理 capture

读取固定 Laya oracle 的已展开输入，以 Sezika 类型化 runtime 和实际 scalar/SIMD/CUDA 后端生成独立对照。参考数值不进入 adapter；后端实际输入与公共 prompt builder 逐项核对。

参数、进程预算、支持范围差异和证据语义见 [操作说明](../../docs/oracle-capture.md)。输出 `capture.json` 与 `Sezika.OracleCompare` 的 `sezika.laya-oracle.v1` 合同兼容；完整运行成功不等同于数值比较通过。
