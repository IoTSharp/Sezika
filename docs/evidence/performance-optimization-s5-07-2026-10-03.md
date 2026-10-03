# S5-07：RoPE 因子复用候选（2026-10-03）

本轮实现一个保持数值合同的 CPU 优化候选：`ModernBertEncoder` 在构造期按已校验的 `ModernBertConfig` 一次生成 global/local RoPE 的 `sin/cos` 表，forward 按位置复用表项。原实现每个请求、每层、每个 head 都重复计算 `Pow/Sin/Cos`；新路径仍使用相同的 `float` 运算顺序，attention、mask、token 预算和取消语义没有改变。

表大小由 `MaxTokens × (HiddenSize / HeadCount / 2)` 固定，四个 `float[]` 的字节数通过 `RopeTableBytes` 暴露，并计入既有模型 `EstimatedResidentBytes`。构造期接受取消令牌；请求期继续检查 deadline。表只属于 encoder 实例，不写入模型权重、manifest 或发布包。

验证使用小型 C# fixture，先构造直接调用 `ScalarOps.Rope` 的参考结果，再用缓存表执行同一输入；全量 query/key 的最大绝对差为 **0**。scalar/SIMD 逐层 trace 仍通过，资源测试共 **37 项**通过，工作区和取消回归保持通过。验证命令与程序身份保留在 `.artifacts/processes/` 的 `rope-cache-resources-build` 和 `rope-cache-resources-test` 结果中。

这只是待实测的实现候选。当前没有独立模型质量、CUDA/AOT 或完整 CPU 矩阵，也没有在固定输入/预算下完成前后消融，因此不能宣称 S5-07 性能收益或稳定尾延迟改善。后续必须在同一权重、输入哈希、后端、采样和资源预算下采集基线与启用表后的报告，并通过数值、取消、卸载和两 RID AOT 门槛后才可评估收益。
