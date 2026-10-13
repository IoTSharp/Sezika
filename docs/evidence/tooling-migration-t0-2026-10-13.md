# T0 分类冻结与检查

2026-10-13，北京时间。主智能体在原 `6534661541ea41231f7dd8c24ea6a9deecc97531` 基线上接管；远端 `origin/main` 独立读回同 SHA。现有训练优化、用户证据和文章保持原归属，没有纳入本任务。

`Sezika.Tooling inventory freeze` 以严格 UTF-8/CSV、唯一路径、行数/数量/字节/时间和取消边界读取原基线，固定所有466条入口的工作包与处置。分母为 tracked282、ignored定点样本174、共享技能5、已有未跟踪用户证据5。新冻结清单保留原清单 SHA 和状态；`ImplementationsAccepted=false`，不能将分类通过用于声称整个迁移实现完成。

Windows .NET10 编译为0 warning/0 error。先执行1项极小检查，再完整4项 CSV/唯一路径/归属检查，既有进程审计和解包32项也重新实际通过。测试程序在运行初期写入自身实际 PID、出生身份、完整 argv 和父 PID；输出目录为 `.artifacts/tooling-migration-20261013-01/{t0-tiny2,t0-full,baseline-recheck}`。当前分类 CLI 实际生成 [466条冻结清单](tooling-migration-classification-2026-10-13.json)。

构建期间用任务专属 MSBuild 选取目标进行切片复核，尚未完成的并行模块不属于T0验收；完整最终树的Windows/Linux/AOT检查仍为T8-code后续范围。原首次LibraryImport编译失败、旧PowerShell启动握手失败、Windows控制台子进程身份失败原回执保留，不改写为成功；新执行器仍须单独完成监督器退出和子树回收检查。

T0没有新增长期脚本、执行教师、启动正式训练、租机或发布模型/数据。分类后的每条能力仍按对应T项完成实现、调用者切换、检查和单独提交。
