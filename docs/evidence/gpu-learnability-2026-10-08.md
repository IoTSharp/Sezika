# 同24题 GPU 可学性与候选位置诊断

2026-10-08，固定数据/模型/旧权重身份见[执行交接](../training-next-handoff-2026-10-08.md)。评价工程已独审并提交 `2111ed9`。本轮没有重新采集教师或扩大数据，24train/8已曝光development保持不变；标签仍未人工审核，正式许可发布/质量/校准/封存/AOT/性能未通过。

## A：旧72步权重只读评价

单次实际CUDA编码train24及8条训练Choice固定左循环变体，32/32 encoder forward。原数据、候选对象及目标ID完整保留；换序题重新构造token/marker并真实GPU编码。旧report/head/checkpoint与前置固定hash相同，不写旧产物目录。GPU logits和完整GPU CE实际执行，没有参数更新。

train24教师一致10/24：Choice2/8，Score4/8，Boolean4/8；完整GPU CE `1.1749068026741345`，与旧报告相同。训练Choice8原顺序与换序后都预测渲染顺序的首候选，因此8/8预测ID改变；换序教师一致2/8、CE `1.3898979127407074`。这些事实表明旧head在训练题上未充分拟合且Choice明显位置敏感；不能用“标签多数类”解释平衡的训练Choice位置，也不能单凭本样本判定encoder表示能力不足。

train原题zh5/12、en5/12；Choice分别1/4、1/4，Score3/4、1/4，Boolean2/4、2/4。换序Choice中英各1/4。全部逐题logits/probability/目标排名与真实token/marker/feature身份在[原字节A报告](gpu-learnability-20261008/stage-a-evaluation.json)，报告SHA `B3868C86946B8E283B12C6B0DFA7D77A5BA895E5B524940A326BE0FF09B51082`。未经校准的概率与教师一致率不表示金标正确率。

Linuxrunner actual completed/exit0、22,270ms（清理3,660ms），complete1/live0/unknown0/unchecked0/incomplete0/budget0；Windows PID72736 actual Succeeded0/27.397秒。GPU owned峰值1,309,369,952 bytes、host峰值2,723,254,272 bytes，低于3GiB/4GiB；device/model Dispose返回，owned bytes/alloc/modules/release failures全零。独立Linux后核runner-tiny及stage-a的8条已登记身份（含重复）全absent，可用显存仍15294MiB；不声明全机无进程。旧报告原hash再次核相同。

## B/C 固定新实验

单次完成零初始化新三行head、768次真实GPU SGD（32×24）、LR0.001、seed17、strict1024，encoder冻结、不resume。dev8只诊断，不选参数；A结果没有改变这个预先固定配置。B保存真实head/checkpoint/训练与评价报告，C独立新进程从磁盘加载，重新GPU编码、完整CE和train24/dev8/换序8验证。B/C分别48/48 encoder forwards，上限各72；两阶段实际单次，无失败后重训或参数搜索。

| 完整GPU CE | 零初始化 | 768更新后/磁盘加载 | C独立进程 |
| --- | ---: | ---: | ---: |
| train24 | 1.229626456896464 | 0.9920696603755156 | 0.9920696603755156 |
| development8 | 1.2687934339046478 | 1.2740284875035286 | 1.2740284875035286 |
| 换序training Choice8（只诊断） | 未测 | 1.3999227583408356 | 1.3999227583408356 |

新head训练题教师一致17/24（70.83%），仍有7题未拟合：Choice6/8、Score6/8、Boolean5/8，zh8/12、en9/12。开发题仅1/8（12.5%）：Choice0/4、Score1/2、Boolean0/2；开发CE高于本轮零初始化。它不是质量改善的证据，也未据此选择参数。训练Choice换序从原6/8降至4/8，6/8预测ID改变，位置敏感仍存在。完整逐题目标排名、logits、概率和中英题型分组分别见[B原评价](gpu-learnability-20261008/stage-b-evaluation.json)与[C原评价](gpu-learnability-20261008/stage-c-evaluation.json)。

C `fresh_disk_outputs_matched=true`，实际参数、原始/换序feature、逐题候选文本/token/marker/logits/probabilities/排名/决策以及完整CE比对通过；本轮实际B/C数值逐项相同。训练特征SHA仍 `44DF5374A9A39C1B064A7AC8E34F1B4C13488F274A45DCCB4B837DB1A2EA145F`，三行2304个F32参数SHA为 `C09F73C98139BFF1D7F429343F08CD5EFC0FDAF28492B07802940B6C3A0C9D8D`。原数据、encoder/tokenizer与旧head保持原身份，新权重独立管理，不嵌入程序、NuGet或Git源码包。

| 磁盘实物 | bytes | SHA256 |
| --- | ---: | --- |
| SZMH1 head | 9619 | 7B4CE09F22AD80E175030F2A73FC878974347D53259DC16D3727B92E7F3184A4 |
| checkpoint | 9619 | 972D6CADDF5743CDA7EAD4CA0F3D657D572DF15B8710A36ADC659B892547EB92 |
| [v1训练report](gpu-learnability-20261008/training-report.json) | 原字节 | 74CE86F566DE7357A0643FFA476AF1D3F651B1FBE83B0BAA8A73A9919456BCAA |
| B评价 | 原字节 | 8CFC1257FA44DFCA62CF119324F46C2083A70D24517EE3FFA4C0863F1CAAF43A |
| C评价 | 原字节 | 11C7CEC6399D33E1C9A85A97DF37E80C7D9A7168073B3EFA9A7CA3484E4A0EEA |
| C payload原stdout（含输出换行） | 原字节 | 738BD68443D5854158AAE9545D4C68687CEE5064B6774B793A76CDF6F7DB36CB |

远端新权重在 `/root/sezika-experiments/20261008-head-learnability-v2/output/run-768/`，本地完整证据在 `.artifacts/gpu-learnability-20261008/retrieved/`，另有先前逐项下载的 `stage-b/`。实际报告与原日志/回执未截取最后一行；远端固定证据归档1,699,840 bytes / SHA `A5B2A09C9441D1691D7A76CEE01A3219286A7D23431289AE428EC7C70C72D1D0`，下载逐hash匹配、限定路径清单后提取。

## 实际退出、资源与边界

B Linuxrunner completed/exit0、26,150ms（清理3,720ms），Windows PID57308 Succeeded0/31.108秒；C Linux completed/exit0、26,370ms（清理3,720ms），Windows PID53104 Succeeded0/32.69秒。全部complete1/live0/unknown0/unchecked0/incomplete0/budget0。GPU峰值均1,309,369,952 bytes，B/C host峰值2,724,798,464 / 2,726,555,648 bytes；实际device/model Dispose返回，bytes/allocations/modules/release failures归零。时长仅用于核执行预算，不是性能验收。

独立Linux最终后核tiny/A/B/C的18条已登记身份（含重复）均absent，原有3个GPU服务保持，空闲仍15294MiB。用户追加授权显存不足时可停止占用服务，本轮没有需要停止。冻结runner仅合作检查；同步native、阻塞I/O、短命中间进程与Windows start/end及三秒snapshot观测局限保留，不把登记范围扩成全机零残留。任务锁、防重复stage目录、准确cancel.request、数量/墙钟双界及全部阶段真实exit均有原回执；微型fixture不作训练证据。

B/C [独立审查](gpu-learnability-20261008/stage-b-c-review.md)与[文档实物核验](gpu-learnability-20261008/document-review.json)通过；前者另从logits复算概率/CE并确认磁盘参数实物，后者确认40条观察完整解析相等。Windows实际阶段回执已按原字节保存：[A](gpu-learnability-20261008/stage-a-windows.result.json)、[B](gpu-learnability-20261008/stage-b-windows.result.json)、[C](gpu-learnability-20261008/stage-c-windows.result.json)；正文的Windows时长已改为这些回执中的ElapsedSeconds。

最终Windows[全量登记核查](gpu-learnability-20261008/windows-pre-commit.closure.json)通过：34回执、151身份记录、144个唯一PID，matching_live=0、unknown=0，10项暂停文件hash均保持；ROADMAP授权结果更新另行核对。其checker PID7672由[独立新进程](gpu-learnability-20261008/windows-checker-independent.json)核为absent。保留[初次失败](gpu-learnability-20261008/windows-pre-final-failed.closure.json)：历史直接子PID80712的CIM argv为null，严格登记在查询前拒绝；未倒填该失败为通过。修订[核查脚本](gpu-learnability-20261008/windows-closure-checker.ps1)只在已验证完整root父链/时间的直接子记录中保留这一缺失，独立精确查询absent或PID已重用才可关闭，同启动身份仍在且argv缺失则unknown/fail且不发信号；先[单回执试核](gpu-learnability-20261008/windows-argv-gap-tiny.closure.json)后以新label全量通过。

该核查范围只覆盖已登记身份。B/C审查部分早先短命reader未保存OS PID/start/argv/父PID，[缺失登记说明](gpu-learnability-20261008/review-reader-identity-limitations.json)保留真实tool exit0及其局限，不冒称这些reader也已独立逐身份核清；新checker观察者本身以实际工具退出回执收口，不递归宣称全机零进程。[临时资源回收](gpu-learnability-20261008/temp-cleanup.json)只移存本任务一个已核编译器临时日志、删除原精确文件及空目录，原8文件fixture也已不存在；模型、冻结工具、原日志和证据归档作为交付物保留。无新增训练、GPU操作、下载、推送、发布、部署或服务停止。

结论是预定768步已完成真实训练、保存和独立加载验证，训练集仍未充分拟合且候选位置敏感。当前证据不能区分增加优化步数、表示或head架构等因素的贡献，不证明泛化、正式质量、校准、AOT或性能。本轮已停止新增训练，其他ROADMAP继续暂停，后续研究等待用户新指令；不自动扩数据、教师采集、调参、MLP/LoRA或encoder反传。
