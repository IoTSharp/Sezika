# T1b 快速子进程、嵌套 broker 与退出证明验收

日期：2026-10-14（Asia/Shanghai）。本批仅提交执行器、独立进程夹具及对应检查的9个准确路径，155,155字节；原训练优化、PTX/ABI、RepositoryTrainer dirty 字节及历史失败保持。

## 实现与拒绝语义

Windows Job 在启动前关联 completion port，提前读取 NEW_PROCESS 的完整原生出生、父、argv 并保留出生 HANDLE；最多256身份、有界事件/等待/取消/join。只有同出生真实系统 conhost 映像可记录最多4次初始化 argv 修订，普通变化仍失败。

Linux 显式 NestedBrokers 最多4项，逐项核绝对路径、完整原生映像 SHA、参数、父出生、scope 与 broker 配置。未通过准入仍保留永久FAIL，原生已观察的出生/父/原argv可用于精确 pidfd 回收，但不会获得预期 exec 或新 scope 权限。已登记身份的空 argv 退出暂态只有同出生保留 pidfd 的真实 POLLIN/POLLHUP 退出/回收信号才能解除；无信号、未登记或非空陌生 argv 不放宽。

Linux 监督器死亡夹具显式绑定外层回执目录。实际晚出生最多12项/1秒，监督器只在外层独立原生观察到所有完整出生/父/argv后被终止；不从内层成功回执补造外层身份。等待仍最多80次/4秒。

## 精确候选与本机证据

候选：`C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\e6`。`src` 从准确暂存索引导出，`compiled-source-manifest.json` 和 `index-source-bindings.json` 逐项绑定原始 SHA、字节数及 Git blob；没有直接编译夹带 GPU 优化的工作区作为本批基线。

| 门槛 | 实际结果 |
| --- | --- |
| Windows tiny/full | 1/1、19/19 PASS；完整外层身份、EOF、cleanup PASS、remaining0 |
| Ubuntu WSL tiny/full | 1/1、18/18 PASS；Linux原生内层与Windows驱动分别核查 |
| Windows Native AOT | 当前候选实际发布成功；成功、父先退出子树、硬超时三项运行与回收通过 |
| Linux Native AOT | `/usr/lib/dotnet/dotnet` 实际发布，内外PASS；同三项原生运行与回收通过 |
| 错 SHA 负控 | 实际准入FAIL；完整资源扫描、EOF与精确回收通过；未取得新 exec/scope |
| GPU训练/质量/现场远程 | NOT_RUN，本批不作验收 |

Windows使用已核MSVC14.51路径、SDK10.0.26100.0库及官方 `IlcUseEnvironmentalTools=true`，没有PowerShell发现助手。官方SDK的ILC/link Exec仍使用厂商CMD编译助手，已保留完整进程回执；这不构成项目自定义脚本实现，也不宣称构建全树无解释器。产品原生执行另按实际进程树核查。

原e4/e5的watchdog空argv、缺外层门、错SHA清理失败及更早UNKNOWN全部保留；后续PASS不改写旧结果。整体T1b调用者、T2协议/Git、T3调度、T6参考生成、466条退役及T8-code仍分别验收。