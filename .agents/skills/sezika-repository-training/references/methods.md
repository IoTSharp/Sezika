# 训练方法及证据层次

Sezika算子、模型、调度保持C#；正式训练和批量特征用GPU。构建期ILGPU生成PTX/ABI，部署由静态CUDA Driver绑定，训练工具允许JIT。不会以桥接库、远端推理API或关键词规则替代本项目模型。

| 方法 | 选择与验收 |
| --- | --- |
| encoder-full | 当前完整语料主线；encoder/head前反向、AdamW、完整保存恢复、同卡最长序列profile与全局cursor独立验收 |
| encoder-frozen head | 仅用于当轮明确选择的固定实验；保持encoder/tokenizer身份，不能借此声明完整微调 |
| LoRA/其他适配 | 尚无本技能可声明的完整实现/验收；先进入根ROADMAP，不擅自替换用户方法 |

CorpusRunPlan全局分母为TrainRecords×Epochs，stage-updates是同一计划的运行预算。短跑、抽样或阶段上限都不是完整epoch；不按曝光development/test标签选超参或训练。数据源/split/family/许可与原文、排除分母分别保留。

训练profile证明指定真实输入形状的前向/反向/更新与保存成本；GPU利用率不能代替吞吐或提速。配置、代码、本机simulation、数值对齐、真实训练、独立语言质量、校准、AOT、性能、发布分别记录。NativeAOT执行通过只覆盖该准确权重/工具/目标，不能推广到新产物。

先按 [当前执行](execution.md) 固定新输入和预算。早期215题、48updates、一轮17615等历史合同及真实结果见 [原方法快照](../../../../docs/history/tooling-skills-20261014/sezika-repository-training/references/methods.md)，不能自动成为新任务目标或预期输出。
