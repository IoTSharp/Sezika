# S4-04 可比较审计基线闭环

日期：2026-10-07。本项闭合固定 Laya 与 Sezika 在相同权重、相同输入上的可比较质量和覆盖率**审计基线**。2026-09-26 的真实推理与数值比较保留原身份；本轮实现归档验收工具并复核历史证据。用户确认原始捕获与本地模型已清理，本轮没有加载模型、重新推理、下载权重、训练或拟合校准。

## 合同与证据链

[冻结计划](../../datasets/s4-04/baseline-acceptance.v1.json)绑定归档索引 SHA-256、三套数据的固定 hash/完整分母、六份双方质量报告及16份逐批数值比较。`Sezika.Evaluation --verify-baseline` 校验输入文件快照和档案 source/artifact hash、固定模型与 tokenizer、256/1024长度合同、温度1和 compatible 策略。每份比较必须关联双方质量报告的 capture hash 与 manifest hash，完整通过原冻结合同，所有批次的 ID 唯一且完整覆盖数据分母。

验证器核对双方质量计数、Boolean混淆矩阵与语言/题型切片，保留全部分母，输出指标差值和准确率95% Wilson区间。它不使用离线评分器的程序集身份代替真实 capture 的推理程序身份；actual 的构建和 reference 的环境分别跨批核对。原先tokenizer失败、中断与旧strict报告没有改写。

本轮输出 `measurement_origin=archived_evidence_verification_no_inference`、`raw_capture_revalidation=not_performed`。档案只能证明已保存的比较和派生评分关联完整；没有原始逐题数据，不能重新评分、重算AUROC/校准或证明当前构建执行了这些模型输入。

## 历史基线结果

| 数据范围 | 完整比较 | Laya 正确 | Sezika 正确 | 策略 | 数据用途 |
| --- | ---: | ---: | ---: | --- | --- |
| PAWS | 250/250 | 170/250（68%） | 170/250（68%） | laya_compatible | 已查看外部审计题 |
| Nimble | 324/324 | 137/324（42.28%） | 137/324（42.28%） | laya_compatible | 已查看外部合成题 |
| 原创中英三题型fixture | 12/12 | 8/12（66.67%） | 8/12（66.67%） | laya_compatible | 公开协议fixture |

PAWS/Nimble的语言元数据缺失，继续记为 `unspecified`。原创fixture每语言仅6题，各4题正确，Boolean全为负类；不能代替代表性中英测试。Nimble的Boolean负类召回为1/57，双方都存在该弱点。概率保持 `uncalibrated`。574条外部审计的近并列观察命中0，仍不作为真实近并列门槛通过。

## 验证与执行

PowerShell7.6.6、.NET SDK10.0.401。Solution Release构建及最终Evaluation定向构建均为零警告/零错误。评估器原有17项自检通过；先以12条fixture执行1项极小试跑，再完成16项回归（2项正例、14项精确错误负例），全部通过。负例包含缺批、混合策略、来源/capture hash断链、跨批重复ID、伪造全失败但宣称完整、Boolean计数、切片计数与有限NLL偏移、非法指标、重复JSON、档案hash错误和伪子集。每个负例均为预期exit1及精确错误，且未生成验收报告；无关启动失败不能计为通过。

机器报告见 [report.json](s4-04-baseline-2026-10-07/report.json)，回归结果和进程记录见 [本轮证据目录](s4-04-baseline-2026-10-07/)。全部构建/验证命令通过 `Invoke-BoundedProcess.ps1` 记录PID、启动时间、参数、父链和任务子树回收。验证器有最大数据/批次数、文件与总字节上限、总墙钟期限、Ctrl+C取消和进度；回归先执行合法小范围，再核对全量归档及语义变异。所有变异文件只用于软件测试。

最终逐文件核对97项归档SHA-256通过。[进程审计](s4-04-baseline-2026-10-07/process-audit.json)复核23份构建/验证runner记录及40个已记录任务进程身份，任务进程残留0、清理错误0；14次预期exit1都是精确负向回归。归档辅助脚本的runner也成功退出并无清理错误。编译器命令记录中的13个临时response文件均已不存在；测试副本、报告和日志作为本任务证据保留，没有删除用户文件或共享缓存。

复核命令（输出路径必须全新）：

```text
dotnet tools/Sezika.Evaluation/bin/Release/net10.0/Sezika.Evaluation.dll --verify-baseline datasets/s4-04/baseline-acceptance.v1.json .artifacts/new-baseline-report.json 30
pwsh -NoProfile -File tools/Sezika.Evaluation/Verify-BaselineChecks.ps1 -ApplicationPath tools/Sezika.Evaluation/bin/Release/net10.0/Sezika.Evaluation.dll -PlanPath datasets/s4-04/baseline-acceptance.v1.json -OutputDirectory .artifacts/new-baseline-checks
```

执行时继续用仓库runner包裹，分别设置45/330秒外层期限。验收计划SHA-256为 `7dc25655b791cd55fe38e11f80c6608a41f90dfe751655e7f94597a1929171cc`；原归档索引为 `99b277bffc2b50f8b140ae5edfde05b3027a01a24328bdf49b3ed7ba6e0e63d6`。工具源码、程序集、报告及执行文件的完整hash见本轮索引。

## 验收边界

S4-04验收的是既有同权重审计基线。代表性中英质量、独立来源准入/封存、正式校准和拒答策略继续由S4-05/S4-08验收；本轮没有把这些门槛改为通过。新独立模型的同权重数值参考、CPU/CUDA与两RID AOT按自身身份验收，不能沿用本项Laya基线。后续实现变化必须按受影响范围重新验证，归档复核不替代当前代码的真实推理回归。
