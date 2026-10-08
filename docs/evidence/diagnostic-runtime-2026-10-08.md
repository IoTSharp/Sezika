# 独立诊断runtime与CUDA接线工程证据

本批在已提交 `c72ab5a` 的SZDH1资产基础上实现显式诊断推理。`IDiagnosticDecisionPipeline` 与 `DiagnosticDecisionV1Runtime` 只绑定DiagnosticHeadIdentity、真实asset/初始化descriptor SHA；pipeline必须显式非null，不创建训练state、数据或feature identity。typed response记录UntrainedDiagnostic、零更新、loss null及uncalibrated分布。结果不构成语言质量、训练收益或执行授权。

## 代码与独立审查

共同 `DecisionV1ExecutionSession` 仅管理sequence、有限logits、稳定softmax、共享deadline、busy/reentry及Dispose生命周期。正式Load/构造的SZMH1身份校验与显式CPU默认语义保留；response factory仍在active gate/owned finally内。诊断构造失败的主异常及cleanup失败聚合，model始终尝试释放。

共同 `CudaMarkerLogitScorer` 真正执行 `RunOnDevice → Linear(hidden,3-row weights) → Synchronize → Copy logits → marker selection`，encoder hidden留在GPU。正式与诊断CUDA wrappers各自保持typed provenance，不转换身份。scorer预备owner后取得encoder/kernels/head buffer，主错误和清理错误保真，caller保留device/model。

15秒native recovery是协作deadline，不抢占同步driver调用；超期保留尚未清的owner并报具体失败，后续Dispose重抛已记录失败。既有encoder内部partial constructor/深层release fault未改；本批未注入driver release fault，不称所有故障均回收成功。

core作者05:25:46开始，05:40:13锁源，14短调用/10明确源；CUDA作者05:25:37开始，05:37:17锁源、05:42:25复核，12短调用/8明确源。均未自行build/test/GPU。root最小接线两个Tests Program，再冻结11源。core独立复审SHA `089FB5C64F8B44D63C451359B41DA88B5746D452662B21E12C636CCDBC1BC246`，逐字机械逆变换确认formal迁移；CUDA复审SHA `D8054AEE1ED205BE541302F2FC371D94283FA90FD914387E41AE1840CED6495B`，正式签名/身份与实际GPU执行核查。均无P1/P2。早期core reviewer只有消息、未保存报告，后续独立复审不冒充早期已有交付。

| 冻结源 | SHA256 |
| --- | --- |
| IDiagnosticDecisionPipeline.cs | 664A07E3F54B88F38BE49D69250BA4582520D8B8B64273911A36CE2C5868FEBE |
| DecisionV1ExecutionSession.cs | 4B118D09364336B36C8D1E0708932945915A0EC5666060D2395C9965D711ACF7 |
| DiagnosticDecisionRuntime.cs | 3A0B89AB8D3F12B095935D4BB94CCFBD5065C2470A24C5024540D70FE0B67280 |
| IndependentDecisionRuntime.cs | F04972173FD25FE23DEE0B4EF2BD290E6817FE370F7EF10D622FE9CBC215C62E |
| DiagnosticRuntimeChecks.cs | 9F07A55B6574F8FEC82B1B2E83903B35BE4920ED1F880C24090FF7138737ADC5 |
| CudaMarkerLogitScorer.cs | FCC44F264B3FCC963CACDF7F9E73F53B3B6D825DF6E9427605083AB85BDA6395 |
| DiagnosticCudaDecisionPipeline.cs | 60E428A1C2201E7896ADBE4DF65A1BC138A47F3F43DC94E3237A77FDE446427C |
| IndependentCudaDecisionPipeline.cs | 266DD171E5F07941094A953C3B2562F5787E69B4677EA9F46B72B95F3F3D6375 |
| DiagnosticCudaChecks.cs | 5FC153923F7B7C485B68010DFCB8E99C9F9481B42C4F337F245379D622FA5526 |
| core Tests Program | 092777CECEEC23B06B55DF20E96A03A7D3C40962C1EE5230A83E66A52A9A8BE6 |
| CUDA Tests Program | 093BF81A4EDF52F8A052033E1183615DF61F0B0FB77A0FEB33BAA574DF3BCE32 |

## 实际验证与资源

固定PS7.6.6/dotnet10.0.401；两项目各一次Release构建，禁build servers、共享编译与节点复用、m:1，专属output和TEMP/TMP，180秒runner。检查各60秒runner，源与实际DLL前后绑定。

| 执行 | PID | runner秒 | 实际结果 |
| --- | --- | --- | --- |
| core build | 86820 | 30.346 | 0warning/0error |
| CUDA checks build | 84344 | 13.747 | 0warning/0error |
| core tiny | 39432 | 3.358 | 1组/5check |
| core full | 59368 | 2.338 | 8组/41check |
| core regression | 46680 | 3.436 | 247check，固定mmBERT tokenizer oracle明确SKIP |
| diagnostic real CUDA tiny | 2184 | 2.386 | 19check |
| formal CUDA pipeline regression | 86956 | 2.581 | 16check |

CPU仅H2合成兼容与数值参考。core八组核三题型/真实tiny encoder、provenance、null pipeline、六绑定负例、预取消/100ms内部deadline恢复、同线程重入/延迟Dispose、NaN/shape/请求失败及主/cleanup双错误。GPU用H8、6 tokens、1 layer、真实SZDH1文件，三题型各与scalar数值比较（abs1e-5+rel1e-4），每次仅72bytes logits D2H，request resident snapshot复原；另一次拒绝后恢复forward。预取消/坏身份/markers前置失败、重复Dispose、disposed forward、caller保留及正常allocation/module/release-failure归零通过。没有运行真实H768 encoder或GPU正式数据训练。

两build的dotnet runner均Succeeded0/cleanup空；原外层脚本exit1是compiler TEMP各3项残留检查失败。后续精确核任务目录、时间、无link及build root退出，非递归删除两空目录，并按hash把workload log移到保留证据；原failure JSON不改、没有重复编译。首次清理因默认JSON日期转DateTime后再Parse丢时区而guard拒绝，未开始删除；改DateKind String后两根关闭。

五DLL在全部检查保持：Tests225280bytes/SHA `37FBD7DAF78DE752D4027F8170FEE9B4AF70320599F368A62D11F85F5BB85D75`，CudaTests352256bytes/SHA `4A68E0B74A785BAAD0AD59CC472CC3807E79B1F1333558C8B16C8C3F47FCB753`，Sezika718848bytes/SHA `4F7259878060BE7482CAD78E71AB6FABB5AFAB1556E5321A2B3E417CF19BBB02`，Cuda151552bytes/SHA `E4A96E06CF90A2E69C679FFB2E156472F044DB1919AE431559D7FA0E33519FC7`，IndependentModelTool478208bytes/SHA `1F3FD28C38C39634866B1D4A7D331E93FEB2A35EB43373CC41FBF24B466053CA`。

证据根 `.artifacts/diagnostic-runtime-20261008/`。七成功runner/37条记录身份无匹配、unknown0，11源/五DLL核验、七专属fixture根已删除；`root-final-closure-v2.json` SHA `295CB62C20AF0CF37D7500511A04DD6C0D92814B9452A82CA2644AACFE9A73F0`。v1作者PID88444的start误写为后续时间，v2改为原manifest实际 `05:40:12.9137134Z`，v1保留；两版本均观察该PID不存在，不倒填元数据。未观测的极短进程、作者失败调用缺PID与未知父链明确在原handoff保留，不宣称全机完整零残留。无服务/下载/独占MCP，用户文件和日志保留。

CLI资产准备、独立诊断profile报告、真实单请求CUDA画像及新两RID AOT另行验收；正式许可、人工准入/封存、可信上游数值、训练收益、质量/校准和发布仍开放。
