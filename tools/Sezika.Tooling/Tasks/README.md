# 任务与断点身份

`task prepare`、`bundle freeze`、`archive pack`、`checkpoint verify`、`host inspect` 使用 C# / .NET 10。请求字段与边界见 [TaskOperations.cs](TaskOperations.cs)，通过 Tooling 的类型化 JSON 入口调用，输出及目标目录必须新建。

- 准备固定 source/dataset/model commit、完整更新分母、期限、保存预留及唯一 GPU 配置；回执明确训练、服务器和发布尚未运行。
- 冻结原文件与独立复制后的完整 bytes/SHA，拒绝脚本载荷、路径别名和既有输出；TAR 归档与解包分别核完整清单。
- 四文件断点核独立预期 Inputs、计划、权重/moments SHA、checkpoint/cursor 绑定、全局位置及结构化调度；结果仅为 `PASS_four_file_sha_cursor_only`，保留 `OptimizerLoaded=false`、`GpuRun=false`。
- 宿主检查读取目标目录所在文件系统的余量，并保持 GPU 与服务器验收为 `NOT_RUN`。

[2026-10-14 切片证据](../../../docs/evidence/tooling-task-core-2026-10-14.md)记录 Windows/Linux 检查及 Windows Native AOT 的实际命令。完整 SDK bundle 构建和 resident 控制器另按 [ROADMAP](../../../ROADMAP.md) 的 T3 验收；现场训练和发布保持独立门槛。
