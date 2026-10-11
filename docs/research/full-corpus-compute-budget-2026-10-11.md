# 全语料训练计算预算：历史实测与当前限制

2026-10-12 在本机只读复核 2026-10-11 的历史训练回执。现有实现是一道题一次完整 encoder/head 反向与 AdamW update；百万、千万道训练题对应百万、千万次 update。把更多来源整理成语料解决了数据接入问题，尚未解决这样规模的训练吞吐和收敛问题。以下只是历史样本的算术外推，不是当前最终语料的工期承诺。

## 已核实的历史输入

只读取一轮 `encoder-full-20261011-121827` 的小型 JSON/TSV，未读取权重、未计算模型特征、未连接旧 GPU 实例，也没有租机或查询租价。

| 来源 | 精确身份 |
| --- | --- |
| 本机完整镜像的 `training-report.json` | `C:\Users\mysti\.codex\artifacts\sezika-repository-local-mirror-20261011-192617\model\runs\encoder-full-20261011-121827\training-report.json`；SHA256 `44c5b1a3d2960bef0f7fe83b8b341e0c4336f78222f740d6a0ea3cfb2589f122` |
| 同镜像的 `run-plan.json` | 同 run 目录；SHA256 `db54a65509a9188337b8e560ad77374d55829c82da42e33c6c95e4b3f27c31e1` |
| 固定 48 步计划的原始 profile | `D:\source\Sezika\.artifacts\repository-training-encoder-full-20261011-121827\local-evidence-v2\profile-48__profile.json`；SHA256 `df0ff58b45ed7e37b41f86c5550c382ebcf06a6c7574757198878e01905075c6` |
| 更早原计划 profile，保留区分 | 同证据目录 `profile__profile.json`；SHA256 `50b538e548b1ecc3f32e043ec310c23527954589034c975f9690c35075ab26b0`；plan SHA 为 `f1a311b02e1813eb38538aac853139bf70db8ee44785d2877ab42af06d6d5da7`，不能与 48 步计划混用 |

48 步训练实际 `InitialUpdates=0`、`CompletedUpdates=48`，训练报告内 `ElapsedSeconds=78.6151095`，摊销为 **1.63781478125 秒/update**。这是报告时点的阶段摊销时间，包含报告之前的初始化、读数与 checkpoint 工作；它不是逐步 GPU kernel benchmark，也没有包含报告之后的所有模型打包/传输/下载时间。原 guardian 从启动到结束为 84.260 秒，均完整回收，训练与 profile 的旧 UUID 为 `989DB6F63273D1DDB2B956CCED4F30A4`。

同计划 profile 最大实际单步 **13.617305 秒**，总 **36.278922 秒**，最大长度 **7,740 tokens**，`SyntheticMaximumLengthProbe=false`。历史硬件按当轮来源回执为 RTX 5090。训练 48 步的 owned 峰值 **5,823,711,896 bytes（约 5.42 GiB）**，完整长序列 profile 峰值 **12,017,759,752 bytes（约 11.19 GiB）**。短训练步的显存较小，不能用来宣称本机约 8 GiB GPU 可以执行全部 7,740-token 长输入训练。

这轮训练和 profile 的 `QualityAccepted=false`；训练还记录 `Calibrated=false`。耗时回执不证明准确率提升。

## 一题一 update 的数量级

下表将假设更新数分别乘以上述历史摊销时间或历史最长步时间。两列是不同样本条件下的算术场景，均不能视为当前新语料的下界、上界、置信区间或预计完成日期。一天按连续运行 86,400 秒计算；未额外计入新实例 profile、读取分片、长度检查、阶段恢复、验证、下载和租赁空闲时间。

| 假设 update 数 | 按旧报告摊销 1.6378 秒/update | 每步均按旧最大步 13.6173 秒 |
| --- | ---: | ---: |
| 48 | 78.62 秒 | 653.63 秒 |
| 17,615（旧单源一个 epoch） | 约 8.01 小时 | 约 2.78 天 |
| 100,000 | 约 1.90 天 | 约 15.76 天 |
| 1,000,000 | 约 18.96 天 | 约 157.61 天 |
| 10,000,000 | 约 189.56 天 | 约 1,576.08 天 |
| 15,000,000（举例，非最终语料实测分母） | 约 284.34 天 | 约 2,364.12 天 |

全部 epoch 的全局更新数为 `验收后的 TrainRecords × Epochs`。按现有一题一更新路径，分成很多短阶段只改善保存、恢复与租期管理，不减少总反向计算量。把旧最慢步再乘四是程序的保守阶段准入启发式，也不是实际训练时间：在旧 `CheckpointEvery=128` 的假设下，百万步约 641.29 天、千万步约 6,412.82 天的准入算术值，明显不能据此直接租用一个覆盖全局计划的实例。

## 下一次预算应当如何形成

先冻结最终语料和严格长度回执，获得真实训练分母、各来源/type 占比、token 长度分布和最大候选数。再在明确授权的新 GPU 上执行固定语料 `profile-corpus`；旧 plan/profile/UUID 只能作历史证据。最长形状 profile 用于资源与故障门槛，只有少数步骤，不能代表千万条来源混合数据的平均吞吐。

真实工期需要额外按来源和长度区间做有界 GPU 吞吐测量，使用实际步骤计时与 checkpoint/恢复/验证开销。当前长短输入和来源差异很大，不能把短 48 步的平均值套到全部题目，也不能把一个长题的最慢时间套成所有题目的预计值。`stage-updates` 应由当次 profile 和阶段墙钟预算确定；全局计划仍保持全部 epoch 身份固定。

若希望千万题的完整训练具有实际可承受的工期，需要先设计并验收真正的 batch、长度分桶、算子吞吐和省显存能力，继续满足 C#、静态 CUDA Driver/PTX 与 Native AOT 约束。当前没有把这些能力写成已实现。单独做梯度累积只会改变 optimizer 更新频率，仍要处理每道题的反向计算，不能自动视为吞吐提速。

在没有这些新实测之前，不能以“已经下载很多数据”估算只需几小时完成全部训练；也不能承诺用全部数据一定更准。先用固定来源配额的小阶段检验 loss、独立测试和少数类行为，再据真实吞吐和收益决定更大计算投入。租价和供应商未查询、未选择；若以后取得每小时真实价格 `P`，费用才可按实际占用小时乘 `P`，并单列存储/流量/空闲，不在本报告编造价格。

## 可复算证据

离线工具 `tools/corpus/estimate_compute_budget.py` 最多读三个固定 SHA 小型 JSON、32 个更新数场景、180 秒墙钟，支持取消；它不调用模型、GPU、网络或租赁服务。此次读取的 report/profile 的完整 Inputs、RunId、GPU UUID、plan SHA 和完成分母均一致。

实际机器可读算术结果：

`C:\Users\mysti\.codex\artifacts\sezika-corpus-20261011-01a12b4c\stream-training-agent\historical-compute-budget.json`

启动 gate 与有界 guardian 回执为 `logs/20261011-160213-123-historical-compute-budget.result.json`，状态 Succeeded、exit 0，完整回收；没有为这次只读预算创建 GPU/下载/租赁进程。未来当前语料的预算须使用自己的新 profile，不能把本文件改名后充当新实例验收。
