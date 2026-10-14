# 任务与断点身份

`task prepare`、`bundle freeze`、`archive pack`、`checkpoint verify`、`host inspect` 使用 C# / .NET 10。请求字段与边界见 [TaskOperations.cs](TaskOperations.cs)，通过 Tooling 的类型化 JSON 入口调用，输出及目标目录必须新建。

- 准备固定 source/dataset/model commit、完整更新分母、期限、保存预留及唯一 GPU 配置；回执明确训练、服务器和发布尚未运行。
- 冻结原文件与独立复制后的完整 bytes/SHA，拒绝脚本载荷、路径别名和既有输出；TAR 归档与解包分别核完整清单。
- 四文件断点核独立预期 Inputs、计划、权重/moments SHA、checkpoint/cursor 绑定、全局位置及结构化调度；结果仅为 `PASS_four_file_sha_cursor_only`，保留 `OptimizerLoaded=false`、`GpuRun=false`。
- 宿主检查读取目标目录所在文件系统的余量，并保持 GPU 与服务器验收为 `NOT_RUN`。

[2026-10-14 切片证据](../../../docs/evidence/tooling-task-core-2026-10-14.md)记录 Windows/Linux 检查及 Windows Native AOT 的实际命令。完整 SDK bundle 构建和 resident 控制器另按 [ROADMAP](../../../ROADMAP.md) 的 T3 验收；现场训练和发布保持独立门槛。

`bundle inputs` / `bundle build` 的输入与构建合同见 [BundleBuild.cs](BundleBuild.cs)。`BundleBuildRequest` 末尾新增可选 `SourceRepositoryRoot=null`，旧 JSON 和旧位置参数调用保持缺省行为。

| 字段 | 作用 |
| --- | --- |
| `SourceRoot` | 已冻结的工程源码快照；完整工程清单、复制及前后 bytes/SHA 始终针对这里，实际 SDK 编译使用新复制的 `build-inputs` |
| `SourceRepositoryRoot` | 可选的真实仓库目录；显式值必须绝对、已存在且无链接祖先。仅构建前后两个 `git rev-parse HEAD` 在此执行；缺省仍使用 `SourceRoot` |
| `SourceCommit` | 上述真实仓库的基线 HEAD，不表示任意源码快照等于该 commit 的纯净树 |
| `EngineeringSources` | 独立固定快照的完整相对路径、绝对源路径、bytes/SHA；不能用另一个仓库里的正确文件绕过快照校验 |

原始请求及 `engineering-overlay.json` 保留 `SourceRepositoryRoot` 与完整工程 pins。实际快照来源须由调用者以真实 `git archive` 和已声明 overlay 独立绑定；工具不创建 `.git`、新 worktree 或假 Git helper，也不将仓库基线自动当作 overlay 已提交的证明。

验收须使用包含该扩展的新候选工具，并固定源、程序及完整 runtime 身份；旧二进制不能代替新字段的真实接线验收。

本次新增两个有针对性的预检回归，原 bundle-inputs 11 项扩为 13 项：相对/缺失/链接仓库在启动前拒绝，以及独立仓库目录不能替代已改变的快照字节。链接检查必须实际创建目录 symlink；若宿主权限不支持则明确失败，不跳过或冒报 PASS。当前源码冻结、构建和 13 项运行状态见 [T3 候选与扩展证据](../../../docs/evidence/tooling-controller-final-2026-10-14.md)。真实 Git + SDK bundle 整链另由主集成者运行，synthetic 预检不代表真实构建成功。
