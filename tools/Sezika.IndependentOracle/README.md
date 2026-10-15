# 独立上游参考归档

当前 C# 数值比较使用既有、独立生成且通过来源审查的冻结参考，由 [IndependentNumericCompare](../Sezika.IndependentNumericCompare/README.md) 只读核对；实际捕获与比较均通过 [C# 调用合同](../../docs/tooling-current-workflow.md) 中的固定目标和有界进程运行。原导出器与上游源码只保留原字节/SHA 来源身份，不能作为现行执行入口。

当前 C# 参考生成入口为 [ReferenceTool](../Sezika.ReferenceTool/README.md) 的 `capture` 命令，读取固定原始 mmBERT 权重并独立计算参考张量。当前原权重已完成26项比较、884736个元素检查；结果标识 `port-derived`、`upstream_executed=false`，不代表运行了上游程序，也不能替代独立上游执行验收。消费者须核对新合同、来源文件、完整编译证明及原始资产身份。详见[本轮参考证据](../../docs/evidence/tooling-reference-final-2026-10-14.md)。

## 历史证据

以下记录原开发期离线 CPU 导出器的合同与边界。原实现从固定原始 mmBERT 权重构建已安装、未修改的 Transformers `ModernBertModel`，导出 25 个上游 checkpoint 与 2 行 marker 向量；这些描述不是当前 C# 参考生成已支持的声明。

源模型为 `jhu-clsp/mmBERT-base`，revision 为 `c5955035435e2bf121cde7f3c8863ef52ff35d82`；原权重、config、tokenizer 的 bytes/SHA-256 必须匹配 `model-manifests/mmbert-independent/oracle-contract-v1.json`。原始权重来源标示 MIT；发布许可、完整通知与 tokenizer attribution 尚未完成，本工具不会改变这些准入状态。

### 2026-10-08 验证状态

2026-10-08 已在现存 Python 3.12.13 中完成标准库自检：tiny 1 项、完整 16 项均通过。自检不导入模型依赖，其输出均标识 `fixture_only=true`，不得作为真实 parity。

当时的实际参考候选环境冻结为 Python 3.14.3、Torch 2.12.0、Transformers 5.13.1、NumPy 2.3.3。记录中 Torch build 为 `2.12.0+cpu`，NumPy 缺失导致 Transformers import 失败；真实 upstream forward 为 `NOT_RUN`。`requirements.txt` 只列版本，不提供完整 wheel/transitive hash lock。这些历史环境信息没有在本次文档切换中重新验证。

### 原自检与运行合同

标准库16项检查覆盖重复/非有限/过深 JSON、整数布局、路径逃逸、checkpoint 顺序、F32 序列化、非有限张量、输出预算、发布前取消、实际 Move 后取消/回滚与临时路径归属；tiny1/full16 是 fixture 验证，未加载模型。原 CLI 拒绝重复选项，并以准确任务 file ID/SHA 约束临时文件清理。

原参考合同要求固定源码、原权重、实际捕获整数输入、合同 SHA 和上游源码 SHA `1C03E59EBA7B59D206F12BC40B7030BF44C5D0E2686CF9F7AD849B6ACB6A38E7`；新输出不得与源模型或实际捕获目录相交。原内部期限180秒及外层进程回收证据属于历史执行合同，不能用来证明新 C# 路径已实现。

### 原数据与执行门禁

工具读取真实 C# capture 的 metadata、request 与已冻结的 I32 token/marker 文件，并核对 SHA 与 `[25,35]`。它不读取 C# checkpoint/marker 的数值来制作 reference，独立 tokenizer 构造不在当前范围。

CLI 外部合同 SHA 必须匹配实际合同 bytes；上游源码 SHA 同时匹配 CLI、合同 `execution.modernbert_source_sha256` 与实际导入文件。工具核对实际解释器、distribution 与已 import 的 Torch/Transformers/NumPy 版本，并记录 upstream source path/bytes/SHA、oracle program identity、package inventory、Torch build 与进程 identity。Windows 的原文件读租约拒绝写入和删除；POSIX flock 仅为 advisory，祖先目录变更不受该租约保证。

源码身份核查在任何 Torch/Transformers/NumPy/ModernBERT import 之前完成：要求这些 backend 未出现在 `sys.modules`，通过 `importlib.metadata.distribution('transformers').locate_file(...)` 定位固定 ModernBERT `.py`，先取得读租约并 hash，再导入，租约保持至 capture 发布或退出。设置 `sys.dont_write_bytecode=true` 与本任务 UUID、确认不存在且不创建的 `sys.pycache_prefix`，隔离旧 pyc 读取；导入后核该策略/前缀未变、前缀仍不存在、module `__file__` 与租约路径相同、loader 为标准 CPython `SourceFileLoader`，且 `__cached__` 位于未创建前缀内并不存在。不满足时明确失败。metadata 保存实际策略和前缀；此门禁针对 fresh 标准 CPython 的固定源码窗口，transitive Python/native 依赖和祖先目录仍未全面 hash-lock。

原权重通过 `torch.load(weights_only=True, mmap=True, map_location='cpu')` 加载，只取原生 `model.*` encoder 的 134 个 CPU F32 tensors，并与 upstream model 的名字集严格匹配。不允许下载或 remote code；CPU F32、eager attention、eval、4 线程、deterministic algorithms、禁用 compile/autocast。partial metadata 的 `original_weights_verified` 起初为 false；只有真实源 bytes/SHA 验证、torch.load 与 strict encoder state 加载完成后才为 true。

所有输出为 contiguous little-endian F32：`embedding/raw`、`embedding/norm`、22 个 `layer/i/hidden` 与 `encoder/final` 均 `[46,768]`；marker 为 `[2,768]`。输出 `capture.json` 使用 `sezika.independent-reference.v1`，complete/kind/fixture、输入/source/contract identities、execution、25 tensors 与 marker 都要完整。reference 完成只代表捕获完成，`represents_upstream_parity`、quality、calibration、benchmark 与 license approval 仍为 false；独立比较器须另行报告 26 项完整分母及固定容差。

输出预算为 20 MiB，metadata 1 MiB、深度 16、最多 100000 nodes；hash 最多 2048 个 1 MiB blocks；1 case、46 tokens、25 checkpoints，内部 deadline 不超过 180 秒，观测进程 peak 不超过 6 GiB。内存采样在检查点进行，native 调用中的瞬时分配不能由此形成硬限制。

正式 capture 和目录发布目前仅支持 Windows：Windows rename 不覆盖竞态出现的目标目录，POSIX no-replace 发布尚未实现。捕获先写本任务 UUID partial 目录，完成文件均 exclusive-create、flush/fsync；目录 rename 前后都检查 deadline/cancel。Move 后取消时先核目录及 complete manifest 的 file ID/SHA，再回滚到原 partial，并移除仅本次完整标记、尽力写 terminal partial metadata。已被外部替换的对象保留并报告。外部强制 kill 可以跳过 Python finally；没有完整且验收一致的 reference 不得通过比较器。

### 原能力边界

该 case 只覆盖固定 46-token realized input 的 25 层级 checkpoint 与 marker。它不验收完整 267 内部算子、local attention 半窗 64 截断、独立 tokenizer、多语言质量、训练、校准、AOT、性能或对外发布。冻结容差不能在看到结果后扩大；任何真实失败必须保留并交监督者处理。
