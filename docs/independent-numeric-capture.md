# 独立 mmBERT CPU 数值捕获工具

状态：工程实现、1→8项自检和固定46-token真实CPU捕获已验收，见[执行证据](evidence/independent-numeric-capture-2026-10-08.md)。本文没有真实上游 oracle、数值通过、质量、校准、性能或许可发布结论。阶段范围已由监督者先登记 ROADMAP；正式训练与批量工作使用GPU，CPU捕获仅供数值核验。

`tools/Sezika.IndependentNumericCapture` 是开发期 C# CLI，复用 `IndependentModelLoader`、`IndependentDecisionV1SequenceBuilder` 和 `ModernBertEncoder.Trace`。它不复制 encoder 算子、不加载 legacy Laya、不包含 Python/远端推理，也不训练或加载决策 head。输入为固定原始 mmBERT 转换 package；输出是供后续独立比较器使用的完整 CPU FP32 证据。

## 固定输入与身份

来源为 `jhu-clsp/mmBERT-base@c5955035435e2bf121cde7f3c8863ef52ff35d82`，独立模型 ID 为 `sezika/mmbert-base-independent-s4`。原始权重身份为 `8ea64ec1ea4eb8fca0fc14b69a2ae571de6bfbc25fd214bb932dd4aba6a3a04e`，本工具**不重新读取原始 `.bin`**，metadata 明确标为固定来源声明。实际载入的 converted encoder 与 tokenizer 由独立 loader 核验固定 hash；原始 config 再核验 `47b40fd2e1df8299426dd5f4bb18c28f028cfafcb51b73645f83e596d187eb37`。package manifest、source lock、原始 config 保存逐字节副本及 hash；source lock 的捕获不是许可批准。始终记录实际 `Environment.ProcessPath` 的 executable hash；managed capture DLL 与可用的 `Sezika.dll` 分列为 companion快照，不能因AOT目录残留DLL而把它称为正在执行的程序。程序文件单项最多32MiB。

在任何CopySmall之前，最多五个 `FileShare.Read` leases同时持有package的 `model.json`、`source_lock.json`、`encoder.safetensors`、`tokenizer/tokenizer.json` 和原始config，覆盖loader核hash、重新打开读取、tokenizer加载与forward，并在所有退出路径释放。每路径进行64祖先/no-reparse检查，不读取原始.bin。Windows共享模式阻止这些文件的写入、删除和替换，闭合核验→读取的文件级窗口；祖先目录恶意同时变更不属于静态前置合同。Unix允许unlink/路径替换，不能把同样的只读句柄写成跨平台原子快照证明；metadata显式记录这一边界。

唯一工程 case 为 `independent-smoke-46-v1`：state `{"text":"independent encoder smoke"}`；Choice ID `intent`、instruction `choose the intent`；候选按 `a:first,b:second` 排列。四段固定 UTF-8 JSON 是：

```text
{"type":"choice","id":"intent","instruction":"choose the intent"}
{"text":"independent encoder smoke"}
{"id":"a","text":"first"}
{"id":"b","text":"second"}
```

结构 ID 为 BOS=2、EOS=1、marker=4；分段不自动添加特殊 token。真实构造器输出必须为46 tokens、markers `[25,35]`、候选 IDs `[a,b]`；I32 小端 token bytes hash 必须为 `BAB3A41CDB62FC4D5E3353D3D88F6BBDCBD0D3CEC9C3DB537841F09604D2D80B`。这里冻结的是工程输入身份，不能称为独立 tokenizer oracle。尚无32-token原始协议 fixture，工具不生成替代案例。

## 张量合同

所有 CPU trace 张量均为有限 F32、小端、row-major，token-major/head channels 连续；`N=46,H=768,I=1152`。没有舍入、量化或 JSON 浮点数组替代原始 bytes。

| checkpoint | shape |
| --- | --- |
| `embedding/raw`、`embedding/norm` | `[N,H]` |
| `layer/{0..21}/norm1` | `[N,H]` |
| `layer/{i}/qkv` | `[N,3H]` |
| `layer/{i}/rope_q`、`rope_k` | `[N,H]` |
| `layer/{i}/attention`、`projected`、`residual`、`norm2` | `[N,H]` |
| `layer/{i}/up` | `[N,2I]` |
| `layer/{i}/gelu` | `[N,I]` |
| `layer/{i}/mlp`、`hidden` | `[N,H]` |
| `encoder/final` | `[N,H]` |
| `marker-vectors.f32` | `[2,H]`，分别取位置25、35 |

267个 encoder checkpoints 的 raw payload 为51,720,192字节。`tensor-000.f32` 等文件名按 capture 的固定顺序映射到 metadata 中的真实 checkpoint 名称；名称、顺序、形状、唯一性与有限值均检查。最终 trace hash 必须等于 `Encode` 返回数组的字节 hash。token IDs 与 marker positions 同时保存为 I32 文件及 metadata 整数数组。

## 执行与发布边界

命令固定最多一个真实 case、128-token硬上限、300-checkpoint上限；CPU scalar 单请求，内部总deadline默认120秒且不可增加。输出及替换临时文件的合计预算100MiB、文件数最多300，路径祖先最多64项并拒绝链接/junction，输出必须位于 package 和原始 source config 目录外，父目录必须预先存在。各循环设置明确数量上限，并检查内部deadline/取消；逐层输出进度并原子更新 `capture.partial.json`。Ctrl+C 或 `--cancel-file` 可取消；正在执行的核心/native调用最终依赖 outer runner 的墙钟约束。

4GiB resident预算通过进程 WorkingSet/PeakWorkingSet 在捕获检查点核验；loader 的 resident估算另保存。检查点观测不能代替外部持续资源限制或性能验收。工具记录自身PID、启动UTC、完整argv/命令行；完整父链、outer timeout与子树清理由仓库 runner 提供，按PID/启动时间关联。此工具不创建子进程、作业、后台服务或网络请求。

先写输出同级的 GUID `.numeric-capture-*.partial` stage 与 owner marker。成功仅在267项完整、最终 hidden 和 marker向量完成、模型释放、deadline与输出冲突再次检查后，原子 `Directory.Move` 发布全新目标，并在实际move后再次检查deadline/取消；禁止覆盖已有目标。move后失败仍进入恢复，不能因为目录已搬出stage就跳过审计。最多共用5秒独立恢复预算，核owner以及自身hash匹配的complete manifest后移除该manifest、更新 `incomplete` 审计；已搬出的目录只有全部journal文件名/字节/hash吻合时，才回退新的GUID `.partial` 路径。每次最多301次枚举检查，拒绝目录/未知内容；恢复失败如实写stderr并保留所在位置，不搬动未知对象。恢复阶段只处理最多1MiB的小文件、原100MiB/300file写界限与严格归属，不重复套用不可逆的进程PeakWorkingSet闸门，也不执行新的模型计算。强制终止时partial可能停在上一完成层；outer结果仍为终止事实。stage保留供监督者审核，不执行宽泛递归清理。

## 监督者验收命令

下面目录均须是尚不存在的目标，父目录先由监督者在其任务证据根中准备。构建、极小试运行和完整自检由监督者占用构建/运行槽；实现子智能体未运行这些命令。

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -Command '$PSVersionTable.PSVersion'

& './tools/Invoke-BoundedProcess.ps1' -FilePath 'C:\Program Files\dotnet\dotnet.exe' `
  -ArgumentList @('build','tools/Sezika.IndependentNumericCapture/Sezika.IndependentNumericCapture.csproj','-c','Release','--nologo','-v','minimal') `
  -TimeoutSeconds 120 -LogName 'independent-numeric-capture-build'

& './tools/Invoke-BoundedProcess.ps1' -FilePath 'C:\Program Files\dotnet\dotnet.exe' `
  -ArgumentList @('tools/Sezika.IndependentNumericCapture/bin/Release/net10.0/Sezika.IndependentNumericCapture.dll','self-test','--output','D:\explicit-evidence\capture-self-test-1','--maximum-cases','1','--timeout-seconds','20') `
  -TimeoutSeconds 40 -LogName 'independent-numeric-capture-tiny'

& './tools/Invoke-BoundedProcess.ps1' -FilePath 'C:\Program Files\dotnet\dotnet.exe' `
  -ArgumentList @('tools/Sezika.IndependentNumericCapture/bin/Release/net10.0/Sezika.IndependentNumericCapture.dll','self-test','--output','D:\explicit-evidence\capture-self-test-8','--maximum-cases','8','--timeout-seconds','20') `
  -TimeoutSeconds 40 -LogName 'independent-numeric-capture-self-test'
```

最多八个 meaningful cases 覆盖：实际非零 tiny encoder 的27项有序完整trace/最终hash/marker提取；NaN与Inf在写张量前拒绝；未知名称、错形状与重复checkpoint拒绝；缺checkpoint阻断完整manifest与发布；目标在发布窗口出现及预先存在时不覆盖；最终metadata之后收到取消信号时不发布；已经实际完成move之后取消时回退目录并移除complete manifest；保持超限的合成不可逆peak传感值在恢复窗口内不变时，仍写出小型失败审计并移除complete manifest。peak检查是internal synthetic sensor，没有真实分配4GiB，finally只重置合成传感器；不改变真实进程的high-water事实。所有数据明确synthetic/fixture-only，tiny直接IDs不调用真实tokenizer，不形成模型输出或质量证据。负向case保留各自如实partial审计；冲突case的sentinel也是本工具创建的合成证据。

真实 capture 需监督者另外验收并采用新输出目录：

```powershell
& './tools/Invoke-BoundedProcess.ps1' -FilePath 'C:\Program Files\dotnet\dotnet.exe' `
  -ArgumentList @('tools/Sezika.IndependentNumericCapture/bin/Release/net10.0/Sezika.IndependentNumericCapture.dll','capture','--package','D:\source\Sezika\.artifacts\models\mmbert-independent\converted','--output','D:\explicit-evidence\capture-original-46','--timeout-seconds','120') `
  -TimeoutSeconds 150 -LogName 'independent-numeric-capture-original-46'
```

`--source-config`默认指向package同级的 `source/config.json`，其他布局必须显式指定。exit0表示本次capture完整或synthetic self-test完成；exit124表示取消/deadline；其他拒绝/失败为exit1。`capture.json`始终将 upstream parity、quality、calibration、benchmark、license approval标为false。独立上游张量/冻结数值容差、原始tokenizer边界、多语言质量、自有head、CUDA与两RID AOT各有后续验收，不能由这份CPU capture关闭。
