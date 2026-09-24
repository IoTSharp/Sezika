# Sezika 基准与生命周期检查工具

本工具使用已安装的固定版本 Laya/mmBERT 模型包，执行真实 encoder/marker-head 推理，输出 JSON 证据。输入是工具生成的固定短请求，问题按 choice、score、boolean 循环排列，每题两个候选；它不接受任意业务数据集，也不测语言准确率。

模型必须已在本地准备好；工具不会下载模型，不执行校准，也不会把权重写进可执行文件或发布物。模型、tokenizer、许可与校准资料仍独立管理。构建、发布及运行应分别在获得相应授权后进行，下面的运行示例假定对应产物已经存在。

## 后端与参数

| `--backend` | 执行方式 |
| --- | --- |
| `scalar` | C# CPU 标量 FP32 encoder 与 decision head。 |
| `simd` | C# CPU SIMD FP32 路径，默认值。 |
| `int8` | CPU W8A32：线性层权重按行对称 int8 量化，激活与累加为 FP32，embedding/norm 保持 FP32。encoder 与 head 的量化权重分别计数；FP32 原权重仍保留，不能将 int8 权重字节数当作总驻留内存。 |
| `cuda` | C# CUDA Driver 绑定加载构建期导出的 PTX/ABI，执行 FP32 GPU encoder/head。显式选择 CUDA 后，缺少驱动、设备或其他执行错误会失败，不回退到 CPU。 |

| 参数 | 默认值 | 含义与边界 |
| --- | --- | --- |
| `--model` | `.artifacts/models/laya-mmbert` | 本地模型包目录。相对路径以进程工作目录为基准。 |
| `--output` | `.artifacts/s5/report.json` | 新的 JSON 报告路径；已有文件不会覆盖。每次运行使用独立文件名。 |
| `--backend` | `simd` | 上表四种后端之一。 |
| `--samples` | `5` | 每个问题数的正式样本数，`1..30`。 |
| `--warmup` | `1` | 每个问题数的预热次数，`0..5`；预热不进入正式样本。 |
| `--cycles` | `2` | 加载/卸载轮数，`1..2`。完整性能采样、数值对齐和诊断只在第 0 轮执行；每轮都执行加载后的首请求与卸载检查。 |
| `--questions` | `1,8,32` | 逗号分隔，最多三个问题数，每项 `1..32`。这是每个请求的问题数，当前每题独立一次 forward。 |
| `--timeout-seconds` | `1200` | 工具内共享取消期限，`1..1800` 秒；还应配合外部进程超时。 |
| `--cpu` | `unspecified` | 人工填写实际 CPU 型号，工具不自动检测型号。 |
| `--environment` | `unspecified` | 人工填写执行环境，例如 `windows-local`、`wsl-ubuntu`。 |
| `--require-aot` | 未启用 | 检查当前进程不支持动态代码；在普通 .NET 进程下失败。此参数不执行 AOT 编译。 |
| `--self-test` | 未启用 | 仅检查最近秩分位数、固定请求序列化及参数边界；不加载模型、不生成基准报告，不替代真实推理验证。 |

最多接受 32 个命令行参数元素。工具按顺序运行，报告中的单推理线程不代表 .NET GC、驱动或宿主进程没有其他线程。每次请求还受 session 的问题数、总 token、工作区、驻留内存及请求期限约束；工具的总期限不会解除这些限制。

## Windows：通过有界 runner 运行

在 PowerShell 7 中操作，先核对版本。以下普通 .NET 示例使用已有 Release DLL，不隐式构建或恢复依赖；将 CPU 标签改为实机型号，并选用尚不存在的报告文件名。

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile
$PSVersionTable.PSVersion
Set-Location 'D:\source\Sezika'
& '.\tools\Invoke-BoundedProcess.ps1' -FilePath 'C:\Program Files\dotnet\dotnet.exe' -TimeoutSeconds 1250 -LogName 'benchmark-simd' -ArgumentList @(
    'tools/Sezika.Benchmarks/bin/Release/net10.0/Sezika.Benchmarks.dll',
    '--model', '.artifacts/models/laya-mmbert',
    '--backend', 'simd', '--samples', '5', '--warmup', '1', '--cycles', '2',
    '--questions', '1,8,32', '--timeout-seconds', '1200',
    '--cpu', '填写实际CPU型号', '--environment', 'windows-local',
    '--output', '.artifacts/s5/windows-simd-run01.json'
)
```

Native AOT 运行应改为直接启动已发布的原生可执行文件，并添加 `--require-aot`，不要通过 `dotnet` 执行它。例如下列路径只是发布位置示例，需替换为本次实际产物路径：

```powershell
$benchmarkExe = 'D:\source\Sezika\.artifacts\s5\win-x64\Sezika.Benchmarks.exe'
& '.\tools\Invoke-BoundedProcess.ps1' -FilePath $benchmarkExe -TimeoutSeconds 1250 -LogName 'benchmark-aot-cuda' -ArgumentList @(
    '--model', 'D:\source\Sezika\.artifacts\models\laya-mmbert',
    '--backend', 'cuda', '--require-aot',
    '--samples', '5', '--warmup', '1', '--cycles', '2',
    '--questions', '1,8,32', '--timeout-seconds', '1200',
    '--cpu', '填写实际CPU型号', '--environment', 'windows-local',
    '--output', 'D:\source\Sezika\.artifacts\s5\windows-aot-cuda-run01.json'
)
```

runner 在 `.artifacts/processes` 保存 stdout、stderr、根进程身份和结果 JSON，失败或超时也保留已有输出。它支持 `-LogDirectory` 指定独立日志盘、`-WorkingDirectory`、`-Environment @{ NAME = 'value' }`，并默认禁用 MSBuild 节点/编译服务器复用。其执行期限之外还有有界的身份采集、清理及输出排空时间。

runner 按 PID、创建时间、命令行与父链核验任务进程，只终止匹配对象。全机进程快照在开始、结束及至少三秒间隔时采集，root/期限检查间隔为 500 ms；快照间存活时间极短的中间进程可能使后代逃过追踪。性能报告应记录所用 runner，因为监控本身也消耗宿主资源。

## Linux / WSL：同时使用 Linux 超时

Windows 进程树不能证明 Linux 子进程已经退出。WSL 必须在发行版内使用 `timeout`，并使 Windows runner 的期限大于 Linux 的 TERM/KILL 总窗口。示例使用已知 `Ubuntu` 发行版及已有 Linux Native AOT 产物；产物路径仍需按实际发布位置调整。

```powershell
& '.\tools\Invoke-BoundedProcess.ps1' -FilePath 'C:\Windows\System32\wsl.exe' -TimeoutSeconds 1280 -LogName 'benchmark-wsl-aot-simd' -ArgumentList @(
    '-d', 'Ubuntu', '--cd', '/mnt/d/source/Sezika', '--exec',
    'timeout', '--signal=TERM', '--kill-after=10s', '1250s',
    '/mnt/d/source/Sezika/.artifacts/s5/linux-x64/Sezika.Benchmarks',
    '--model', '/mnt/d/source/Sezika/.artifacts/models/laya-mmbert',
    '--backend', 'simd', '--require-aot',
    '--samples', '5', '--warmup', '1', '--cycles', '2',
    '--questions', '1,8,32', '--timeout-seconds', '1200',
    '--cpu', '填写实际CPU型号', '--environment', 'wsl-ubuntu',
    '--output', '/mnt/d/source/Sezika/.artifacts/s5/wsl-aot-simd-run01.json'
)
```

在 Linux 终端可直接执行同一 `timeout --signal=TERM --kill-after=10s 1250s` 及其后面的程序和参数。普通 .NET 模式将程序替换为已知路径的 `dotnet` 和已构建 DLL，移除 `--require-aot`。WSL 与原生 Linux、Windows 的报告必须保留独立环境标签，不能据一个环境的通过状态推断另一个环境也通过。

## 构建 Native AOT 与汇总

在目标系统使用已配置的 .NET 10 SDK 与 native 工具链编译。以下为本轮使用的关键 publish 参数；外层仍使用上述 bounded runner / Linux `timeout`，每次 publish 上限 600 秒。Windows 与 Linux 使用各自独立的 `--artifacts-path`，避免共享 `obj` 冲突。输出目录可放到有足够空间的磁盘，模型目录独立传入。

```text
dotnet publish tools/Sezika.Benchmarks/Sezika.Benchmarks.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:StripSymbols=true -p:IlcMaxDegreeOfParallelism=2 --artifacts-path <windows-build-directory> -o <windows-output-directory> --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false

# Ubuntu 中执行同样的参数，改为 -r linux-x64，并使用 Linux 绝对输出路径。
```

`--require-aot` 的实测结果与程序 hash 写入报告。最终原生目录不包含模型权重；ILGPU 只存在于独立构建工具，不进入此程序的项目引用路径。

全部运行结束后，汇总原始 JSON：

```powershell
& '.\tools\Summarize-S5.ps1' -ReportPaths @(
    '<win-scalar.json>', '<win-simd.json>', '<win-int8.json>', '<win-cuda.json>',
    '<linux-simd.json>', '<linux-cuda.json>'
) -OutputPath '<new-summary.json>' -VerifyMatrix -TimeoutSeconds 30
```

汇总器最多读取 32 个、各不超过 4 MiB 的报告；校验通过状态、模型/input hash 一致性、原始样本与最近秩分位数、数值门槛及资源归还后才创建新的汇总文件。`-VerifyMatrix` 要求 Windows 四后端各有至少 5 个正式样本和 1/8/32 问，以及 Linux SIMD/CUDA 的真实模型 AOT smoke。额外 RID 或后端仍保留为独立行，不合并不同环境的样本。此开关只验证已有证据，不代替运行模型。

## 计时与证据解读

正式 `milliseconds` 样本覆盖请求字符串转 UTF-8/JSON parse、tokenizer、顺序逐题 encoder/head、响应构造及类型化 JSON 序列化。它不包含模型加载、报告写盘、样本间进度输出或采样后的语义检查，也不表示网络服务端延迟。每次请求当前有多少题就执行多少个 micro-batch，不能当作一次真正的多题 GPU 批处理。

每轮先记录模型加载、后端初始化、三题首请求及本轮加载至首响应总时间。只有第 0 轮首请求是该进程的首次请求；操作系统文件缓存、驱动缓存均未清空。同进程第 1 轮重载会复用进程/JIT/驱动等状态，不是独立 cold-start 样本；`--warmup 0` 也不会跳过这次三题首请求。

`p50`、`p95`、`p99` 使用最近秩：排序后取 `ceil(p × n)` 的第一个起算位置。小样本只能作探索性观察，p95/p99 常重合，也无法稳定描述尾延迟。`requests_per_second` 为正式样本次数除以总采样耗时；`questions_per_second` 再乘每请求问题数。这里没有并发负载、排队或持续吞吐测试。

CUDA 正式样本关闭 event profiling。`cuda_profiled_forward` 来自额外一次开启 profiling 的三题请求，包含 instrumentation/synchronization 开销，应与普通 E2E 样本分开解释。`cuda_load` 来自第 0 轮后端初始化；CUDA module load 的墙钟时间包含 Driver 模块加载及可能的缓存/JIT 工作，不能标作纯 PTX JIT 时间。

`peak_working_set_bytes` 是操作系统记录的整个进程生命周期 RSS/working-set 高水位，包含加载、数值参考路径和诊断阶段，不是稳态推理专属内存。`allocated_bytes` 是每个样本前后进程范围的托管累计分配差值，不是存活对象大小。完整 GC 后的托管内存与弱引用哨兵回收用于生命周期检查，不意味着 RSS 会立即归还操作系统，也不是逐个数组的全量泄漏证明。

CUDA owned bytes/count 只计工具成功持有的 Driver 分配；不含驱动/context 的全部开销。free/total memory 是整张设备的读数，可能受其他进程影响。卸载检查要求 owned bytes/count、loaded modules 及 release failures 归零；快照读取发生在 device 最终 Dispose 之前。W8A32 仍保留 FP32 原权重，比较总内存时同时查看量化字节数与进程高水位。

报告保留运行时、RID、环境标签、固定模型/tokenizer 身份、manifest 和当前进程可执行文件哈希、完整输入及输出。普通 .NET 模式的进程可执行文件可能是 dotnet host 或 apphost，该哈希不能单独标识所有托管程序集；保存构建版本和产物哈希时应补齐这一信息。

## 失败与能力边界

成功返回 `0`；参数、运行、数值对齐、诊断或报告写入失败返回非零。输出路径有效且可写时，失败报告保留 `status`、`phase`、`error_code`、`error` 与已经完成的测量。外部强制终止可能来不及写最终基准 JSON，此时 runner 日志/结果仍是检查依据。已有报告、无效参数或不可写输出路径不会保证产生新报告。

数值对齐使用固定四 token 输入比较标量参考、encoder 输出、各题型 logits/概率；CPU 还比较相同 hidden-state 输入的 head-only 误差。诊断覆盖预取消、encoder 首次 trace 后取消、非法题型、取消后恢复、卸载后拒绝请求、CPU 工作区归还、对象回收哨兵，以及 CUDA 预取消构造和资源账目检查。首次 trace 后取消不等价于在任意 CUDA kernel 内部抢占；预取消构造不证明任意部分上传失败路径。

这些检查通过仅说明对应输入、后端和进程中的数值/生命周期检查通过；报告中的回答仍为 `uncalibrated`。它不证明多语言质量、实际正确率或校准质量，也不能将模型概率、distribution concentration 当成执行授权。最终阶段结论与真实测量证据由项目的 `ROADMAP.md` 和 `docs` 单独记录。
