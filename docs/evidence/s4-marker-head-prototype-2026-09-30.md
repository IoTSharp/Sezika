# S4-07/S4-12/S4-15 独立 marker head 与 encoder 证据

核对日期：2026-09-30。本文记录本轮实际构建、测试、资产转换、独立 encoder smoke 和有界开发训练。开发结果不等同语言质量、校准或发布验收。

## 原型实现

`src/Sezika/IndependentMarkerHeadTraining.cs` 提供三题型 `[3,H]` 线性 marker scorer、稳定 softmax 交叉熵、解析梯度、固定种子 SGD、取消/步数/墙钟边界，以及绑定模型、encoder、tokenizer、协议、长度策略和完整特征集合 hash 的训练 checkpoint v1。

`src/Sezika/IndependentDecisionV1.cs` 提供独立 typed `decision-v1` 输入、固定 JSON 段、BOS/EOS、显式 mask marker、严格 1024 token 上限和完整序列 marker 特征导出。`src/Sezika/IndependentMarkerHeadAsset.cs` 提供与优化器状态分离的推理 head asset v1 保存/加载，并拒绝 hash、身份、形状和有限值错误。

## 构建与测试

以下命令均通过仓库 `tools/Invoke-BoundedProcess.ps1` 执行并保留 PID、启动时间、命令、父链、超时和结果 JSON：

| 命令 | 结果 |
| --- | --- |
| `dotnet build Sezika.slnx -c Release -v minimal --nologo` | 0 警告、0 错误，约 30 秒 |
| `dotnet tests/Sezika.Tests/bin/Release/net10.0/Sezika.Tests.dll` | 350 项 PASS，约 3 秒 |
| `dotnet build tests/Sezika.Tests/Sezika.Tests.csproj -c Release` | 0 警告、0 错误 |
| `dotnet build tools/Sezika.IndependentModelTool/Sezika.IndependentModelTool.csproj -c Release` | 0 警告、0 错误 |

新增检查实际覆盖：三题型有限差分梯度、softmax 归一化、确定性恢复、错误 hash/身份/篡改/错形状/NaN 拒绝、`decision-v1` JSON 重复/未知字段拒绝、BOS/EOS/marker 顺序、完整序列特征导出、legacy 模型 ID 拒绝、严格 token 超限拒绝，以及独立推理 head asset 往返和身份错配拒绝。

## 原始 mmBERT 资产准入与转换

按 [独立模型合同](../independent-model-contract-s4-14.md) 固定并核验 `jhu-clsp/mmBERT-base@c5955035435e2bf121cde7f3c8863ef52ff35d82`：

- `tokenizer.json`：17,525,329 字节，SHA-256 `197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356`；实际 C# tokenizer 加载成功，special IDs 为 bos=2/eos=1/mask=4。
- `pytorch_model.bin`：1,231,188,142 字节，SHA-256 `8ea64ec1ea4eb8fca0fc14b69a2ae571de6bfbc25fd214bb932dd4aba6a3a04e`。
- `config.json`：SHA-256 `47b40fd2e1df8299426dd5f4bb18c28f028cfafcb51b73645f83e596d187eb37`；模型声明 `ModernBertForMaskedLM`、22 层、hidden 768、12 heads、intermediate 1152、max position 8192、MIT 许可标签。
- 模型卡 `README.md`：SHA-256 `35724415037ee3159aa733c16050620cc19a76219112a6ceeb3949572e0b37e9`；卡片声明 MIT，并列出预训练数据和 Gemma 2 tokenizer 来源。数据再利用和 tokenizer 归属仍需独立许可审签。

`tools/Sezika.IndependentModelTool` 的 `convert-pytorch` 只解析已知 C# pickle 子集（`_rebuild_tensor_v2`、FloatStorage、连续 row-major view）；未知 opcode、非连续 view、非 FloatStorage、尺寸/hash 不符均拒绝。实际转换输出仅保留 `model.*` 的 134 个 encoder tensor，排除 tied decoder/MLM head：

- `encoder.safetensors`：1,227,772,549 字节，SHA-256 `6168c84d2498cdacdcafee16b71a69b5c1636ad569bfabaf2855f5ce7dabd528`。
- 独立 manifest：`model-manifests/mmbert-independent/model.json`；source lock：`model-manifests/mmbert-independent/source_lock.json`。
- 独立入口为 `sezika/mmbert-base-independent-s4`，不会读取或回退 `convaiinnovations/laya-multilingual`。

`IndependentModelLoader` 已用转换结果实载 1.2 GB encoder，并执行 `decision-v1` CPU scalar smoke：46 token，marker 位置 25、35，hidden 长度 35,328，全值有限；输入 token hash `BAB3A41CDB62FC4D5E3353D3D88F6BBDCBD0D3CEC9C3DB537841F09604D2D80B`，hidden hash `1D94C9DA34D7FEC59A714A55ADA65001FE18F0B480B87C3542C32762B5644325`，min/max `-40.36507/33.564888`，resident 估算 1,227,758,592 字节。该结果是本地导入、形状和有限值证据，不是独立上游逐层数值 oracle。

## 原创人工开发 smoke

`datasets/independent-v1-original/records.jsonl` 是项目自有、人工编写的 12 条中英记录，Choice/Score/Boolean 各覆盖两种语言；train/development 各 6 条。`manifest.json` 明确许可、用途、split 和 `represents_model_quality=false`，当前仅允许开发 smoke，尚无 calibration 或 sealed test。

实际命令：

```powershell
dotnet tools/Sezika.IndependentModelTool/bin/Release/net10.0/Sezika.IndependentModelTool.dll train-original `
  --package .artifacts/models/mmbert-independent/converted `
  --records datasets/independent-v1-original/records.jsonl `
  --head .artifacts/models/mmbert-independent/converted/head-v2.asset `
  --report .artifacts/models/mmbert-independent/converted/development-report-v2.json
```

结果：真实独立 encoder 导出 12 条完整序列特征，固定种子 `20260930`、24 步、学习率 `0.05`；feature hash `00068803BE2D6291E52C1D7963C0F6F02A581AC959078A730A14D27C983FBC5F`，head asset hash `461BB72228CF4095DD446E67B402FA0EE89CA6AD22087600AD70E5273BAA8E82`，最后一步 loss `3.5083892269737023`。development 6 条结果为 4/6（66.7%）：英文 choice 1/1、英文 score 0/1、英文 boolean 0/1；中文 choice 1/1、中文 score 1/1、中文 boolean 1/1。每个分组分母为 1，不能外推语言质量，也不能替代封存测试或校准。

## S4-05 候选准入与 split 合同

`datasets/independent-v1-original-admission/` 将同一批项目自有候选记录转换为 S4-05 `audit-splits` 合同：16 条记录分为 Training 6、Development 6、Calibration 2、SealedTest 2，并为每条记录固定 source、family、entity、language、domain、derivation、exposure 和审核状态。records SHA-256 为 `d56bc2f478b3c665650eaeef33dac856d3e5b92d97f3b7d935dcbaad00349f1e`；审计报告的 manifest SHA-256 为 `3066b45f1e61a028e23e393ee20c15194d7266005ef2c4512a8f6e307fa46908`。

通过 `tools/Invoke-BoundedProcess.ps1` 运行 Release DatasetTool 的一次审计（60 秒内部期限、90 秒外层期限），实际比较 120 对，耗时约 0.1 秒，结果为退出码 3、`blocked`。报告见 [`audit-report-20260930.json`](../../datasets/independent-v1-original-admission/audit-report-20260930.json)，阻断项为：来源许可 `license_not_approved` 1 项、16 条 `human_review_required`、`test_not_sealed` 1 项。报告明确 `requires_human_acceptance=true`、`represents_model_quality=false`；这不是校准、语言质量或 sealed test 结果。只有授权审核人补齐许可证据、逐条人工审核和独立保管 seal 后，才可重新生成准入报告。

## 未关闭门槛

1. 原始模型卡中的预训练数据和 Gemma 2 tokenizer 归属仍需逐项许可/通知审签；MIT 模型标签不自动授予数据再分发或训练后权重发布权。
2. 尚无独立上游逐层/端到端数值参考，当前 hash/min-max 仅锁定本地转换和运行结果；ModernBERT 算子差异仍需参考实现对照。
3. 候选数据已具备 train/development/calibration/sealed_test 的合同形状和污染检查入口，但审计仍因许可、逐条人工审核和独立保管人封存缺失而 blocked；smoke 指标不构成 S4-06/S4-08 质量验收。
4. CUDA 反向训练 kernel、独立 head 的 CUDA/AOT 回归、校准 profile、正式质量报告和发布包仍未完成。
5. `qwen35-9b-q4km`/IoTSharp/Tomur 未安装或调用；本轮未把教师作为前置，也未把教师输出混入人工数据。
