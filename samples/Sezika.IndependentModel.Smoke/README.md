# 独立真实 encoder 与诊断 head smoke

开发样例静态引用 C# core/CUDA，使用生产 IndependentModelLoader、公共 runtime/pipeline 构造器；不改变产品 CLI 或默认模型。固定原始 mmBERT、46-token decision-v1 输入以及显式三行内存诊断 head。诊断算法不是训练：`((i*17+11)%101-50)/10000f`，seed=0、[3,768]、9216 little-endian F32 bytes。

报告 `head_origin=diagnostic_initialization`，head_file_present/head_training/formal_training/training_data_present/feature_cache_loaded/quality/parity/calibration/benchmark/license_approval 均 false。容器 DataManifest/Feature/FileSHA token 是实际 diagnostic descriptor 摘要，不能解释为训练 manifest、feature cache 或 head 文件。单次诊断推理不是正式自有 trained-head、语言质量、校准、性能或独立 oracle 验收。

命令为 `self-check --max-checks 1|8 --timeout-seconds 30`、`probe --output <new-json> --device 0 --require-aot --timeout-seconds 20`、`run --backend cuda|cpu --package <converted> --capture <capture.json> --source-freeze <source-freeze.json> --output <new-json> --device 0 --require-aot --timeout-seconds 100`。CPU模式仅显式单case兼容参考，本阶段不运行实际CPUforward。输出父目录必须存在；报告最多1MiB、CreateNew/Flush+Move不覆盖，Move后取消精确file ID/SHA回收成功输出，失败另存唯一audit文件。

固定输入核 request SHA、46个I32 token SHA、markers[25,35]及其 bytes/hash；生产 tokenizer 重新构造序列必须逐元素一致。模型加载核134原始encoder tensors与固定converted/tokenizer hash；另核原weights/config/source-lock，持有文件只读leases到运行结束。CUDA缺失直接失败，不SKIP/CPUfallback；caller保留device，实际runtime预取消核新增launch/workspace为0，再唯一forward，最后释放runtime/model/pipeline并核owned bytes/allocations/modules/releaseFailures/workspace归零，最后释放caller device。

`--require-aot` 要求 DynamicCodeSupported=false、DynamicCodeCompiled=false、TPA=null。sourcegen typedJSON，无反射JSON、动态程序集/launcher或IL warning suppression。Linux x64源可编译设计不等于Linux已发布/运行；owned file-ID实现使用Windows GetFileInformationByHandle或Linux x64 fstat，后者需独立真实验收。

所有外部执行经仓库 Invoke-BoundedProcess，PS7+，single build并发、关闭build servers；构建120秒，Windows publish最多2次每次300秒，self-check runner45秒（先1再8），probe runner30秒，真实CUDAcase runner120秒/内部100秒，host peak观测4GiB、CUDA owned观测3GiB、最多46tokens。记录source freeze/hash再publish/run；source freeze是外部实际测量清单，报告记录清单bytes/hash和实际进程exe identity。文件路径拒绝symlink/junction，祖先并发变更与瞬时native分配仍依赖外部执行边界，不宣称硬内存隔离。

首次失败、runner日志与结果保留在 `.artifacts/independent-aot-20261008/head-checkpoint`。真实Windows Native AOT/CUDA是否通过以阶段证据为准。

source-freeze 文件由程序记录bytes/hash；程序不解析该清单或验证它与当前源码/已发布binary的关联，这部分由root独立核对6个源文件和publish identity，报告明确contents_validated=false。CUDA启用profiling以获取真实launch/copy计数，此执行含逐kernel同步，不能当吞吐benchmark；预取消比较resident owned counters稳定，唯一forward须有正launch和552-byte readback。active/释放后peak均核3GiB；cleanup与failure audit有独立≤5秒预算，避免已超峰值或取消阻止回收已确认归属的对象。
