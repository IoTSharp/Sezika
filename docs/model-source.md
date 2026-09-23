# 固定模型来源

Sezika 的首个真实多语言资产固定为 Hugging Face 仓库
[`convaiinnovations/laya-multilingual`](https://huggingface.co/convaiinnovations/laya-multilingual)，
revision `052592a15d198d9ad47da779604259b10b47b7aa`。该 revision 的模型卡声明 Apache-2.0，模型文件和
tokenizer 以 SHA-256 锁定：

| 文件 | 大小 | SHA-256 |
| --- | ---: | --- |
| `model.safetensors` | 643,835,514 | `9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204` |
| `tokenizer/tokenizer.json` | 34,363,188 | `609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f` |

Laya 权重包含决策头和 mmBERT encoder。其 `rl_agent_config.json` 将底座写为
`jhu-clsp/mmBERT-base`。该底座的固定 revision 是
`c5955035435e2bf121cde7f3c8863ef52ff35d82`，模型卡声明 MIT，tokenizer 架构注明 Gemma 2。
Apache-2.0 的 Laya 资产与 MIT 的底座来源必须在分发清单中分别保留，不能将底座来源或 tokenizer
归属省略。上游代码许可证和模型权重许可分别核验；本文件不把训练数据许可推导为模型许可。

`tools/Sezika.ModelTool` 在离线读取 SafeTensors header 后生成 `.artifacts/models/laya-mmbert/model.json`
和 `source_lock.json`；不含权重的同一份 manifest 与 source lock 也提交在
`model-manifests/laya-mmbert/`，供审计和复现使用。运行时只接收生成的安全格式，不反序列化
pickle 或执行仓库中的 Python 代码。

## tokenizer pipeline

固定 tokenizer 是 tokenizers JSON v1：Replace normalizer 将 ASCII 空格替换为 `▁`，Metaspace
pre-tokenizer 使用 `prepend_scheme=always`，模型为 BPE（byte fallback、fuse unknown），最后由
TemplateProcessing 添加 `<bos>`、`<eos>`。特殊 token IDs 为 pad=0、eos=1、bos=2、unk=3、mask=4、
start_of_turn=106、end_of_turn=107。`tests/fixtures/tokenizer-mmbert-oracle.json` 保存了中英、中文、
阿拉伯文、日文、组合字符、emoji 和混合脚本样本的参考 token IDs；fixture 由固定 `tokenizers 0.22.2`
从上述 tokenizer 文件离线生成，Sezika C# tokenizer 已在 8 个样本上逐项复核通过。

## encoder/head 摘要

Encoder 为 22 层、hidden size 768、12 heads、MLP intermediate 1152、vocabulary 256000；每三层
使用 global attention，其余层 local window 128；global/local RoPE theta 均为 160000，norm epsilon
为 1e-5。权重实际为 F16（`temperature` 为 F32[3]）。决策 head 为两层 pre-norm Transformer，
FFN 3072，PyTorch 默认 activation 为 ReLU；scorer 是 LayerNorm → Linear(768,768) → GELU →
Linear(768,1)，marker 使用 mask token ID 4。Head 的温度配置为 [1,1,1]，状态为 uncalibrated。
