# S4-15 独立 linear marker head CUDA 前向

本轮提供显式独立 CPU/CUDA pipeline 接口以及 CUDA 前向实现。目标仍是独立 `decision-v1` encoder 加三行自有 linear head，冻结 encoder；没有 GPU 训练、legacy type embedding、legacy Transformer head/scorer 或动态 ILGPU launcher。复用既有构建期导出的静态 PTX/ABI、CUDA Driver `Linear` kernel 与 `CudaModernBertEncoder.RunOnDevice`，未修改 kernels/PTX。

`IIndependentDecisionPipeline` 固定完整 `MarkerFeatureIdentity`、feature/head 文件 hash、backend 标识、token 上限和三题型 `Score`，保留候选顺序。`IndependentDecisionV1Runtime` 的原三参数 CPU 构造保持可用，新增四参数 `(model, head, options, pipeline)` 显式注入。构造时核对 pipeline/head 的完整七项身份、两个 hash 及预算；输入序列构造、类型映射、softmax、输出有限性与题型结果仍由同一 runtime 执行。返回 response 的 `backend` 标记实际选择的 pipeline，不据此声称质量或性能。

当前 `ModernBertConfig.MaxTokens` 固定在 2–1024，CPU/CUDA pipeline 严格继承它。通用 `IndependentDecisionRuntimeOptions` 的 8192 校验上界不表示当前模型支持 8192；有效 runtime 上限超出 model 或 pipeline 时构造明确拒绝，不截断。

CPU 实现抽取完整 encoder 序列的 marker 行后调用原三行 linear head double dot 参考。CUDA 实现将这三行 `[3,hidden]` FP32 参数常驻设备，直接在最终 device hidden 上执行一次 `[tokens,hidden] × [3,hidden]^T`，得到每 token 三题型 logits；只拷回最多 1024×3 个 FP32 logit 并按 marker position/type 取数，保持 encoder hidden 留在设备。CPU dot 为 double 累加，CUDA Linear 为 FP32 累加，验收必须冻结数值容差，不能承诺逐位一致或借历史 legacy 数值报告关闭新 head 验收。

`IndependentCudaRuntime.Load` 提供静态显式工厂，读取同一个独立 encoder package 和 hash-bound inference head，再创建 CUDA pipeline。CUDA 缺失、设备不兼容、身份不匹配、非法序列/marker 或非有限 logits 返回实际错误，不 fallback。该工厂和 CPU 路径均不能读取 Laya 权重作为独立身份。

runtime 拥有 model 与注入 pipeline。CUDA pipeline 自己创建并拥有 CUDA encoder、head buffer 和 kernel modules；caller 保留 `CudaDevice`，须在 runtime 之后释放设备。runtime dispose 即使 pipeline 清理抛异常也通过 finally 清理 model，重复 dispose 不重入；活动 inference 的同线程 dispose 请求延期至 evaluation finally。CUDA pipeline 的直接并发 score/unload 使用非阻塞 gate，忙时明确失败；每请求临时 buffer 用 using 回收。构造期最长 10 分钟，score 最长 5 分钟并链接 caller/runtime 的更短 deadline；原生 Driver 调用不能在执行中强行打断，外层仍须有界进程 runner。

CPU/CUDA 公开 pipeline 在 gate 内再次检查关闭状态，避免 gate 外观察与 dispose 之间的状态竞态；dispose 同样在 gate 内复查幂等，active score 时不提前释放。runtime 对同线程重入 `Evaluate` 返回 `decision_session_busy`，不会让嵌套调用破坏活动计数或提前清理。

新增检查接线：

- `tests/Sezika.Tests/IndependentPipelineChecks.Run(Check, ExpectDecisionCode)`：15 秒总 token，真实 tiny CPU encoder/head 三题型派发、有限概率、候选顺序、完整身份错配、明确 fixture 非有限/错误数量 logit 拒绝、取消和 dispose 异常后 model 回收。
- `tests/Sezika.Cuda.Diagnostics.Tests/IndependentCudaChecks.Run(device, Check, deadline.Token)`：最多三题型、每次六 token/八 hidden/单 encoder layer，30 秒链接 token；实际 Driver logits 与 scalar CPU 比较，误差 `abs <= 1e-4 + 1e-4*abs(reference)`，验证非零且不同 head 行、D2H 为 `6*3*4` bytes、工作区回收、预取消/非法 marker 零 launch、恢复、卸载及 caller 设备保留。

fixture 模型来源明确 `synthetic/*`，手工 hash 是合同夹具身份，不能冒充真实来源权重 hash。监督者已完成核心190项、CUDA完整59项、CLI41项及预测导出11项回归；Windows Native AOT 独立 CPU18项、CUDA实卡17项通过。首次fixture合同及共享入口接线失败均保留，修复后的结果和资源范围见[实际验收证据](evidence/independent-pipeline-2026-10-08.md)。该证据不关闭真实 mmBERT/自有 head 数值、linux-x64 AOT、质量、校准或性能门槛。既有 CLI/训练/预测导出默认 CPU 入口未接入 CUDA；显式产品命令接线归后续独立任务。

所有检查临时 tokenizer 使用唯一 CreateNew 文件，finally 核对绝对路径/前缀后只清本次文件；长期证据由监督者保留在 `.artifacts/supervision-20261008/cuda`。现行进程监护使用 C# `ProcessExecutor.ExecuteAsync`／`process run`：以 `ExecutionRequest` 明确原生程序的绝对路径、结构化 `ArgumentList`、工作目录、新输出目录、数量/墙钟及取消边界，记录身份并回收本任务子树。具体接线见[当前工作流](tooling-current-workflow.md)；本段更正不新增 CUDA 实测或改写原回执。
