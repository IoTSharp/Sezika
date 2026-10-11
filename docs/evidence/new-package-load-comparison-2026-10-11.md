# 新模型包加载与同条件来源标签比较（2026-10-11）

新48步完整微调包已由独立 C# 进程实际读取并在本机 GPU 推理，Windows Native AOT 实际执行通过；本轮固定来源标签子集准确率 **119/256（46.48%）**，旧冻结 encoder / 24576 步 head 为 **126/256（49.22%）**，没有观察到准确率提升。新模型原序 Choice 192/192 选首候选、Boolean 64/64 预测 true；负类召回 0/24，换序 selected ID 改变148/192。**正式质量、校准、封存、Score 金标质量与正式 AOT/性能发行门槛均 NOT_ACCEPTED。**

本轮没有训练、调参、教师调用、租机、旧服务器连接、源代码 commit/push 或公开历史改写。原主会话的发布/匿名读回/历史清理回执保持原件；模型/数据与公开仓库未写入。当前工作树已有大量 unrelated dirty，本轮仅补消费入口与本证据，索引保持空。

本轮私有证据根：`C:\Users\mysti\.codex\artifacts\sezika-load-compare-20261011`。逐题题文、来源账本、标签、概率、错误和失败目录只保存在该私有目录；本文件仅有汇总。主读回根为 `C:\Users\mysti\.codex\artifacts\sezika-repository-local-mirror-20261011-192617`；读取 `final-publication-readback-v2.json` 和当前匿名模型资产，不接管历史清理。

| 项目 | 实际绑定 |
| --- | --- |
| 新包路径 | 主读回根 `anonymous-readback-v1/runs/encoder-full-20261011-121827` |
| package SHA | `36adeeb7531e6212229e8df318bfd8dfd930703140b3be75576e0a873f50c20e` |
| 新 weights SHA | `f683bc38e40627abd03659a1cc07f9123e0776635aa1bf38724a7d19c5185c20` |
| tokenizer SHA（两模型相同） | `197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356` |
| 新 head payload SHA | `8878bce4faa0801c823a65e8c444f4f6a5a46ef601193426cbe8821fcedfc7b7` |
| 旧完整包 | `D:\source\Sezika\.artifacts\rental-training-20261009-01a12099\trained-model` |
| 旧 encoder SHA | `6168c84d2498cdacdcafee16b71a69b5c1636ad569bfabaf2855f5ce7dabd528` |
| 旧 head SHA | `4205457556736ab07799b462d42c377710e765cb14edb9aae508c1558c5e9d64` |
| 旧训练报告 SHA | `efafe1e006e3dd7a3d8bec3a2327651406749fa0bf348c987783ec658ba51e4d` |
| 新训练输入 commits | source `9ee9eda7a45d41fa0f3a283e9377094ff6885760`；data `b8ea13b81aaaca722baf6a3c0f12e2217fc4353e`；base model `06ebe82325e58c0ae723fb9014a38cdb2f87b22a` |
| 冻结比较输入 SHA | `0d3b51c9896e8ffb0e037d5eb4618a16f4577a45ab6a713ea308146cc1487830` |
| 本机 | PowerShell7.6.6；RTX4070 Laptop / 8188MiB / driver596.08 / CUDA Driver API13020；UUID `134BF256A4B04FD4E3073C3D34F79CB4` |

`predict-package` 读取显式 package SHA、原训练/完整检查点回执、精确 tokenizer 和新 weights，严格检查135个张量名称/shape/F32、306,941,952个有限值和非零三行 head。Tokenizer special IDs 为2/1/4。新包仅按当前已验证48步布局消费；不放宽原 `IndependentModelLoader` 的底座 SHA，不拼接本机旧 encoder，不从 filename 构造训练身份。分项许可保持原包 overall unknown、tokenizer Gemma 来源和数据/输出权利边界。

新增 `CudaMarkerInference` 复用现有静态 C# CUDA Driver encoder/head kernels，只分配推理权重/工作区，不分配 encoder 反向或 AdamW moments。旧 `RepositoryTrainer validate` 的原计划/RTX5090/full-training 状态门槛未改。`evaluate-baseline` 实际加载旧 encoder 与显式 SHA 绑定 head/report，在同一新推理路径运行。算子与调度仍为C#，没有 ONNX/LibTorch/cuBLAS/cuDNN 或远端推理。

新工具的 Release 构建和 win-x64 Native AOT publish 均零警告/错误。三个原创真实文本 Choice/Boolean/Score 由 JIT、新独立AOT及最终可执行文件实际推理通过；JIT与AOT的三题概率相同。它们是兼容性 smoke，无gold、无正式正确率。最终 Native AOT 程序为私有根 `aot-v2/Sezika.RepositoryTrainer.exe`，三题输入为 `smoke-cases-v1.json`。

可复用命令（需提供本机准确GPU UUID和新的输出路径）：

```powershell
$taskRoot = 'C:\Users\mysti\.codex\artifacts\sezika-load-compare-20261011'
$package = 'C:\Users\mysti\.codex\artifacts\sezika-repository-local-mirror-20261011-192617\anonymous-readback-v1\runs\encoder-full-20261011-121827'
$cases = Join-Path $taskRoot 'smoke-cases-v1.json'
& (Join-Path $taskRoot 'aot-v2/Sezika.RepositoryTrainer.exe') predict-package `
  --package $package --package-sha 36adeeb7531e6212229e8df318bfd8dfd930703140b3be75576e0a873f50c20e `
  --cases $cases --cases-sha 079b1de0b34afc1ca9fc2ec0abd3e3818476b956b0232afcada64246bfcbe63b `
  --gpu-uuid 134bf256a4b04fd4e3073c3d34f79cb4 --max-cases 3 --max-tokens 1024 `
  --owned-mib 4096 --timeout-seconds 120 --output (Join-Path $taskRoot 'user-new-output.json')
```

这是 `RepositoryTrainer` 显式新包消费CLI。`src/Sezika.Cli` 的 legacy/independent-v1 格式没有自动切换到新包；无需更改其固定身份来实现本轮消费。输出自带 load、逐题JSONL和最终JSON，使用CreateNew保留已有回执，进程中断时已落盘题目可追踪。

| 来源 | 原分割/标签 | 本轮固定原题 | 新模型正确 | 旧模型正确 |
| --- | --- | ---: | ---: | ---: |
| SNLI 英文 Choice3 | 原test；原人标0/1/2，-1预排除 | 64 | 23（35.94%） | 23（35.94%） |
| BoolQ 英文 Boolean | 原validation；原crowdsourced answer | 64 | 40（62.50%） | 34（53.13%） |
| XWinograd 英文 Choice2 | 原test；上游参考answer1/2 | 64 | 29（45.31%） | 32（50.00%） |
| XWinograd 中文 Choice2 | 原test中CLUE补充部分；上游参考answer1/2 | 64 | 27（42.19%） | 37（57.81%） |
| 合计 | 256原题，另192换序 | 256 | 119（46.48%） | 126（49.22%） |

每模型256原题+192Choice左移，共448次；两模型共896次真实GPU前向，均success，failure/refusal=0，覆盖率256/256。冻结的是同一题文、候选ID/说明/顺序、1024token strict、FP32、4GiB owned预算、GPU并发1和同一Native AOT程序；模型自身最大配置8192不等于本轮验收8192性能。独立评分程序对每个原题重新核probability有限值/归一化/argmax/正确性、输出分母和逐题JSONL身份，并逐项核256个原题的token count、sequence SHA与候选ID/顺序完全相同。

配对：共同正确39、新独对80、旧独对87、共同错误50；准确率差-2.73个百分点，描述性exact McNemar p=0.64257。新Wilson95%40.47%–52.60%，旧43.15%–55.31%；这些是固定公开子集的描述统计，语义/翻译家族相关性未完全确认，不是正式封存置信结论。随机均匀选择期望45.83%；固定首候选为103/256，仅作为预先定义的常量对照。

新模型原序192个Choice全部选择首候选，64个Boolean全部预测true；BoolQ负类24题召回0/24。新Choice换序selectedID稳定44/192、改变148，换序正确97/192。旧模型负类召回18/24，换序稳定9/192、改变183，换序正确75/192。换序稳定性改善不替代正确率；新模型的明显候选位置/Boolean单类偏向说明质量门槛仍开放，不按这些测试标签继续调参或训练。

新NLL=0.92205/Brier=0.64281/ECE10=0.20980，旧1.10411/0.69008/0.26419；均为未拟合温度的256原题测量。概率是分布，不是实测正确率；这些数值不代表已经校准。Score仅做加载/推理smoke，没有本轮已准入ordinal gold，因此Score MAE/质量 **NOT_RUN**。

数据处理先用每源1题+Choice换序7次真实前向验证，再固定每源64题，不按结果选题/标签配比。SNLI原10000中176缺label预排除、按premise精确规范化去重6501；BoolQ原3270中154个已曝光/准备state匹配排除、同passage去重295；XWinograd英文2325去重24；中文504排除最初16个原XWinograd行和7个缺单下划线/非法布局行。保留全部raw/revision/SHA及原行号；中文最初16行的排除用于降低未知翻译重叠，不冒称跨语言家族完全验明。使用固定SHA salt+源名+原行内容排序，不做结果筛选。

精确版本：SNLI `cdb5c3d5eed6ead6e5a341c8e56e669bb666725b` / CC-BY-SA4.0；BoolQ `35b264d03638db9f4ce671b711558bf7ff0f80d5` / CC-BY-SA3.0；XWinograd `90b619ef5278605cd4f572e3e8506ab16afd44f2` / 卡声明CC-BY4.0。中文混合CLUE组件的原标注/权利链仍pending，不从顶层声明推广。派生21913行、旧215题和全部历史BoolQ viewer页的可确认原文重叠已排除；本轮48消费顺序重新核Order SHA，实际Choice/Score/Boolean各16条。语义、实体、反事实和底座预训练污染尚未完整审核；不是sealed test。教师215题和旧196前向报告的teacher一致性未混入此准确率，也没有重发教师。

旧Laya资产和原始PAWS/Nimble捕获已在此前用户清理后缺失，只能保留历史分数，未在本轮伪造第三模型同次比较。裸mmBERT无对应决策head，不充当已支持的决策模型。当前已实际比较两个可消费决策模型。

拒绝/失败检查：准确package SHA错误在任何模型加载前exit1；缺tokenizer的受控不完整小元数据包exit1；1200次重复短语的超长完整输入strict拒答；重复Choice ID和空instruction结构化失败。后者报告保留全部3条，0成功/1拒答/2失败，状态 `COMPLETE_with_case_failures`，native资源归零；退出0只表示成功写出完整失败报告，不能当所有请求成功。测试包明确标记controlled negative fixture，未用于任何gold推理。原head SHA大小写拒绝FAIL和快速python观察器CleanupFailed均保留，修正/终核另记，不覆盖旧FAIL。

两模型峰值owned均1,314,645,536bytes；最终owned bytes/allocations/modules/release-failures均0，独立进程退出0。gold guardian实际180.284s/211.777s，均低于900s上限；内部840s、有最大448、取消和逐题进度。时间含render/tokenizer/copy/算子且未控制热状态，不作性能接受。所有进程/PID/创建/argv/父链和收口在私有根process-logs及最终回执。

Parquet解码器PyArrow22.0.0仅安装至本任务独立目录，从PyPI官方wheel用于数据读档；Python仅做资产/数据/离线统计，不进入模型算子或推理。共享Python、驱动、系统/共享缓存未改。任务依赖和进程的最终回收、原始检查日志及资产SHA重新核验另记私有根 `final-acceptance-v1.json`；保留可执行文件、准备/评分脚本、输入和全部成功/失败证据。

可关闭的是此固定包的C#独立加载、Windows Native AOT实际执行、同条件输入和完整比较分母门槛。不可关闭的是代表性质量/校准/人审/封存、Score真实金标质量、完整来源权利、8192长度/受控性能、Linux新权重AOT及正式发行。下一训练或参数选择只能使用独立train/development合同，此已曝光比较集保持评测用途。
