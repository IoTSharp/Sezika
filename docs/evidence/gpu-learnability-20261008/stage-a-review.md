# 阶段A真实结果独立审查

2026-10-08。结论：**阶段A实际只读GPU评价与资源回收证据通过本阶段验收，无未解决阻塞项，可独立提交A证据后按既定配置启动唯一阶段B。** 本结论不改变768步/LR0.001/seed17/strict1024/零初始化/冻结encoder，不按A或dev结果调参。

本次只读审查最多12文件/12分钟/5批，实际12个唯一证据输入、4批PowerShell7.6.6短命令。先tiny1后32行审核，循环明确数量/30秒/取消/8行进度；未运行GPU、网络、Git、模型或源码编辑，无临时文件/长驻进程。逐题派生核结果保存stage-a-audit-metrics.json；不是fixture或训练输出。

## 原始身份与合同

stage-a-evaluation.json实际SHA256 **B3868C86946B8E283B12C6B0DFA7D77A5BA895E5B524940A326BE0FF09B51082**。读取完整payload.stdout.log，其全JSON正文与磁盘评价报告逐字一致，仅stdout额外终止换行；原stdout未被截最后一行。report phase=existing_head_train24，32/32 started/completed encoder forwards、failed_records=0、strict1024。报告中的completed_steps=72为所载旧head的历史更新数，本阶段没有训练。

来源旧head/checkpoint/report的实际本地SHA仍分别为2C93C6A9FD28C491AE5718F00C81C5FFB08859FC7F85C6A8F4DF6B209A5D5AAD / 5C604C9510658373EB2933C2D82FAE0FF0E798D8837C59D3B66E5BE7A63C3528 / C6B065CD0C66AA4544C0AF277C539D82A4E7932C66CD41AAAD37CFC1A0D30EE7。records实物SHA仍A015A8FA182EE62223CAFC307C618C515A19E90F843290DB9DFB53A56B9A041C。旧v1 report完整train CE、train feature44DF5374A9A39C1B064A7AC8E34F1B4C13488F274A45DCCB4B837DB1A2EA145F、参数3C15FFF5DADBBF423DF21BD4DFEB0DAD141A1469CE77C39A85C69187BDFF9B6A均与A逐字/精确数值一致。独立远端后核再次确认旧v1 report hash OK。

CLI身份回执确认实际运行evaluate，PID743849/start_tick483714659；其exec前后身份、完整argv、父链均保留。报告模型/manifest/provenance/device身份与冻结合同相符，正式acceptance全部false。日志含24条原train真实GPU export、8条fresh rotated GPU export与完整CE/logits进度，没有development评价或SGD进度。

## 全分母与逐题核对

逐行对实际固定records查唯一原题，32行全为train：24原题+8固定左移训练Choice，中英/题型共8个组分母正确。候选ID与文本hash按同一左移映射，教师目标ID保持，目标索引由ID跟随新顺序。32行token与marker F32无关的I32字节SHA重新独立计算相同；8条换序的原token/marker/feature引用与对应原题相同，实际新token hash与新feature hash均8/8改变。所用token/feature变化结合已审冻结GPU编码路径和实际forward日志，支持真实重新编码；没有把缓存feature重排冒充换序。

32行GPU logits独立计算softmax/NLL、argmax并列首项、competition rank与目标ID一致；概率最大绝对误差2.77555756156289e-17，GPU CE与host稳定NLL最大绝对误差1.5529872188224658e-7，均在预设容差内。各组count/一致数/CE/目标排名均重新聚合吻合，完整train与swap CE均与行均值吻合。development和development_pipeline为null，阶段A未碰dev诊断。

| 诊断分母 | 教师一致 | 完整GPU CE |
| --- | ---: | ---: |
| 原train24 | 10/24（41.67%） | 1.1749068026741345 |
| 原训练Choice8 | 2/8 | 1.3640286326408386 |
| 固定左移训练Choice8 | 2/8 | 1.3898979127407074 |
| 原训练Boolean8 | 4/8 | 两语言均约0.689 |
| 原训练Score8 | 4/8 | 两语言均约1.47 |

原train24中中文6/12、英文4/12；Score中文3/4、英文1/4。全部8原Choice与8换序Choice的predicted_index均0，Boolean8全预测false。8/8换序的selected_candidate_id改变。Choice的汇总一致率保持2/8，但命中的具体题发生改变；不能用汇总一致率不变声称换序稳定。该旧72步head呈明显候选位置敏感、训练拟合仍弱；不能单凭A归因到encoder或宣称后续768步必然解决。

## 真实退出与资源后核

Linux result实际completed/payload_exit0/final_check_complete1/cleanup_incomplete0；verified_live/unknown/unchecked/cleanup_budget_exhausted均0、fixture=none。Linux总22,270ms、cleanup3,660ms，CLI16.9078356秒，均在约定预算内。report实际model/device Dispose returned true；owned bytes/allocations/modules/release failures全0；ownedGPU峰1,309,369,952B、host峰2,723,254,272B，低于3GiB/4GiB。

另读取监督者执行的独立Linux后核原stdout：A与tiny登记8条身份（含重复）全absent、live0/unknown0，stage-b/stage-c当时不存在；目标UUID正确、free15,294MiB，既有3个服务仍列出；旧report hash再次OK。此终核仅覆盖登记身份，不声明全机零进程。监督者另通报Windows全阶段前核24回执/103身份/99PID live0/unknown0；本12文件审查未重复读取该额外Windows回执，范围不夸大。

本阶段完成的是旧head可学性/候选位置诊断，不是质量、正式正确率、校准、sealed test、AOT或性能验收。下一步维持唯一固定768步实验，不扩数据、重采教师、网格调参或追加训练。
