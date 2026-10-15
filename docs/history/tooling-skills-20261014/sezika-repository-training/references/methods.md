# 完整微调的方法与边界

## 当前全量 corpus 方法

默认 repository-first：既有转换/准入 corpus 的407文件（353分片）按原 bytes/SHA 保存到 private dataset 的 `derived/<run-id>/corpus/`；正常 Git LFS push 与独立认证 fresh 完整读回通过后，服务器按真实 PublishedDatasetCommit 拉取派生入口，不重转换 raw。原来源 DatasetCommit `d19515802d31310b4050226b51ab85c60f130d42` 与新发布提交分别绑定。仓库拉取接续工具固定 `3461fc0dae689d4d471654af5d85b9850d0908a4`（51工程源码、399工具文件SHA、Ubuntu68项调度检查通过；旧d925仅历史），模型固定 `e2e5bc97d62dfbd4e2bbd070542324bb544e6b32`，新训练仍从底座 encoder 加零 head 开始。

使用真实 `CorpusRunPlan`：17来源，`TrainRecords=14592720`、`Epochs=1`，development=1619168。全局更新数 `TrainRecords × Epochs`，一训练记录一更新；按 `source_round_robin_v1` 流式消费 train，不过采样、不把 development/test/calibration/OOD 纳入更新。固定 seed、manifest/bindings/length-report SHA、输入 commit 与 PTX。`--stage-updates` 仅限定这次阶段的增量，恢复相同 plan 与 optimizer/global cursor，禁止改分母以适应租期。

`profile-corpus` 必须在本次实际 GPU 上从固定底座与零 head 重新运行；旧48步 profile不能复用。旧d925f1c5冻结代码只准 RTX5090、至少30GiB显存、CUDA Driver>=12.8；本轮代码增加精确RTX4090/CC8.9候选门槛并保留30GiB下限，必须重新冻结工具、在当轮实卡完成8192 shape profile和gpu-admission.json设备/SHA绑定。福海已完成closed/GSP0规避，但新启动仍故障，实机前反向及训练NOT_RUN。正式训练只执行 C# GPU；CPU仅有界数据/兼容性诊断，不用 PyTorch或其他桥接替代。新 profile 决定整轮、checkpoint、独立验证与本机保存的预算；预计租期不足时提前通知用户续租，不自行缩全局计划或购买。

完整 corpus checkpoint 必须同时有 `weights.safetensors`、`moments.safetensors`、`checkpoint.json`、`corpus-cursor.json`。阶段保存通过仍可能 `FullCorpusEpochCoverage=false`；仅 `CompletedUpdates=TrainRecords × Epochs`、epoch/within-epoch一致、完整来源 EOF 与最终 checkpoint/cursor通过才允许全量完成。未完成时保留真实覆盖、最后完整断点和未完成状态；独立子集 GPU 验证不等于全量质量验收。旧48步checkpoint不兼容当前全量plan。

## 历史2026-10-11单来源方法与实证

以下“本轮/当前”、17615更新与48步流程仅描述历史；不作为本轮全量 corpus 默认值或发布授权。旧215题合同和百万条 `prepare` 上限同样不适用当前完整分母。

本轮目标是完整微调 mmBERT encoder 和独立三行决策 head，从模型仓库底座、零 head 开始。FP32 全参数 AdamW，beta1=0.9、beta2=0.999、epsilon=1e-8；norm 不做 weight decay。encoder/head 各有学习率；每次一条完整序列。每层保留输入、反向时重算前向；所有编码、反向和更新使用 GPU。目前没有混合精度、gradient accumulation、LoRA、FlashAttention 或训练时量化。

`CudaFullEncoderTrainer` 和构建期20个 backward PTX 已完成工程。Choice、Boolean、Score soft-target CE / 全图梯度 / AdamW、重复 token、global/local attention 已通过 tiny GPU 数值检查；checkpoint 保存全部权重、first/second moments、update cursor和冻结数据顺序，tiny 恢复后的1008状态坐标与连续训练逐位相同。2026-10-11 新 RTX 5090 复验 tiny 梯度4项、checkpoint7项，并完成真实 mmBERT 三次反向/AdamW profile：同一800token训练题两次、7740token最长训练题一次，最大单步13.6047607秒、owned峰值12,017,759,752bytes。profile 不导出训练权重，也不作正式初始化。随后新48步计划/profile及底座+零head起点的48步完整encoder/head实验训练完成并保存全量权重/checkpoint；独立新进程加载同包的128原题GPU验证通过，另外68Choice换序检查完成，selectedID稳定67/68、改变1/68，未实现本子集完全不变。guardian分别回收通过。完整运输/回推、质量、校准、新权重 AOT/性能仍分别验收。

第一个适配器仅支持固定 revision 的 `kaivoss/system-one-270m-data` 原始 train，Apache-2.0；teacher soft targets 保留分布，不当实测正确概率。完整25002行最终21913 accepted records：train17615/development4298，隔离3089（2994个无法确认升序的Score、54个不支持的prompt布局、2个候选边界问题、39个超8192token），duplicate0。最长accepted序列7740token。其余17来源需单独适配，不能声称已经混合使用全部仓库。

normalized 原始 state SHA 为 family，SHA前8位mod10<2留作development；原文的所有题保持同 split。不读原始 train 之外的split。当前没有语义近重复、人审或正式质量验收，`formal_training_admitted=false`，执行只接受 `ExperimentalOnly=true`。

Choice2..32候选；Boolean严格原始no/yes；Score2..10明确连续升序数字等级。不能把qtype=score的非序数候选猜作ordinal。Boolean把原始question作instruction和statement，false/true说明保持原文；使用Sezika decision-v1渲染，原prompt和行hash保留。Choice左移时ID与soft target联动，Boolean/Score保持语义顺序。训练type分组，组内seed+record ID SHA排序，再round-robin；大组耗尽后其余继续，不丢数据。固定完整顺序重复使用，checkpoint cursor决定下一条及换序位。

模板：seed17、encoder LR1e-5/head LR1e-3/decay0.01、strict8192、17615updates（一轮）、每128步checkpoint、每split固定64原题及其中全部Choice左移。它是预定实验配置，未经性能/质量验证。租期不足先依据profile明确调整合同与租期，不能偷偷改步数或按development选参。独立验证128原题及Choice对照，属于实验验收，不是全21913题/封存测试/校准。

2026-10-11 本轮最初租期截止北京时间14:18，训练阶段预留至13:48；用户后来已明确续租到当日15:00，当前deadline为UTC07:00，原期限快照保留。完整17615步按原保守阶段预算无法准入，用户明确选择并已完成48步全参数短程实验、独立验证和回推准备。续租不改变已执行的48步合同。48步只消费固定训练顺序前48条，即 train 的约0.27%；encoder和head均更新，仍不能声称全数据训练或训练收敛。必须保留一轮计划和原profile，另冻结48步计划并重新profile；完成多少步、实际题目ID和每类型分母以新训练报告为准。

新包以 `package.json` 为显式入口，`--package-sha`绑定新weights/tokenizer；旧 `IndependentModelLoader` 固定encoder SHA不放宽。当前通过 `RepositoryTrainer validate` 的独立 GPU加载消费新包，公开推理CLI/新权重AOT发行仍待完成。完整微调不能使用本机旧encoder补齐新模型。

2026-10-11 本机加载与比较后续：`RepositoryTrainer predict-package` 已实际消费该48步包的135个张量和新head，新增C# inference-only静态CUDA入口无需AdamW/反向状态。RTX4070 Laptop 8GB按1024token strict/4GiB owned完成JIT及Windows Native AOT Choice/Boolean/Score smoke；原5090 validate/训练计划门槛不改。`evaluate-baseline` 同程序加载旧冻结encoder/24576步head，按预先冻结256来源标签原题+192Choice换序，每模型448真实GPU前向；新119/256、旧126/256，新负类召回0/24、原序Choice全部首候选，正式质量仍不通过。Windows新权重AOT实际执行已验收，Linux/正式AOT发行、8192/受控性能、Score gold、校准/封存/代表性质量和中文组件权利另验；不按已曝光测试标签自动重训/调参。准确CLI/SHA/私有证据路径和失败边界见[本机加载与比较证据](../../../../docs/evidence/new-package-load-comparison-2026-10-11.md)。
