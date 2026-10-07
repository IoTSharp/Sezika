# 离线教师运行前核查：2026-10-08

已取得官方 Qwen3.5-9B 固定来源的完整 Apache-2.0 许可正文，并定位 Tomur 的固定数据目录、启动、模型登记及最小 CPU native 构建入口。**当前检查范围内没有已构建 Tomur 程序或 Windows native bundle，也未取得带固定 digest 的公开 Tomur release 资产**。下一步需要准备程序与最小 llama CPU bundle，再进行固定 GGUF 下载 / 实际 hash 校验和 1 题有界试运行。本轮仅完成 AI 来源工程核查；采集用途由监督者另行登记为 audit collection，训练、再分发和人工审核状态均未授予。

## 来源与许可正文

量化目标保持前一轮锁定：`unsloth/Qwen3.5-9B-GGUF@3885219b6810b007914f3a7950a8d1b469d598a5/Qwen3.5-9B-Q4_K_M.gguf`，LFS 声明 5,680,522,464 字节，SHA-256 `03b74727a860a56338e042c4420bb3f04b2fec5734175f4cb9fa853daf52b7e8`。本轮未下载实际 GGUF；Tomur catalog 仍登记 8,953,803,264 字节与浮动 `main` URL，后续下载采用固定 revision，实际权重身份必须再次核验。

官方来源通过 HF API 锁定为 [`Qwen/Qwen3.5-9B@c202236235762e1c871ad0ccb60c8ee5ba337b9a`](https://huggingface.co/Qwen/Qwen3.5-9B/tree/c202236235762e1c871ad0ccb60c8ee5ba337b9a)。本轮并行子任务先读取单 API pilot，再读取该 revision 的 README 和独立 LICENSE，共 3 个小文件；每文件直连失败后唯一代理成功。

| 官方证据 | 字节 | SHA-256 |
| --- | ---: | --- |
| HF API `source-api.json` | 21,235 | `2B7C72A60778B961F188263795B2CDD4281BCFBDB8FA4DED8ED81AD60B69FCAB` |
| 固定 README | 77,643 | `C5F5A8C2DDDAB69CFBF05279235AA5FDDB137939A06539C4C7637AA900FEF6D0` |
| 固定独立 LICENSE | 11,544 | `BBEDC3FDA3305820B977265F01B8619D87570A6739DE3A5582C3464840F1E57A` |

[固定 LICENSE](https://huggingface.co/Qwen/Qwen3.5-9B/blob/c202236235762e1c871ad0ccb60c8ee5ba337b9a/LICENSE) 标题为 `Apache License Version 2.0, January 2004`，版权通知为 `Copyright 2026 Alibaba Cloud`。实际正文 §2 含 reproduce、prepare Derivative Works、sublicense 与 distribute 的授权，§3 为专利授权，§4 为再分发条件。该许可正文的取得与条款文字检查可诚实登记为 AI 来源工程核查；不伪造人工 reviewer 或审批身份，也不能据此自动将模型输出或蒸馏数据标为训练 / 再分发已审核。

量化模型卡将 `Qwen/Qwen3.5-9B` 声明为 base model，但没有锁定 base revision。本轮取得的官方当前 revision 不能证明量化产物恰好由该 base revision 构建。保留 `HumanLicenseReview=pending`、`QuantizedBaseRevisionEquivalence=not established`；建议后续 collector source identity 中明确记录固定量化资产、固定官方许可快照、AI 工程核查方式及 collection-only 范围，`training_use_allowed=false`、`redistribution_allowed=false`。实际审签字段由监督者按真实证据填写。

官方小文件及完整进程证据位于 `.artifacts/supervision-20261008/teacher-runtime-preflight/official-license`；`conclusion.json` 与 `revision-lock.json` 为归一化结论，`revision-lock.raw.json` 保留初次将命令诊断混入 metadata 的问题，原始 runner 日志未改。最终核对 14 个已记录进程身份无存活对象，partial 为 0，cleanup errors 全空。

## Tomur 程序、数据目录与依赖

只读仓库为 `D:\source\Tomur`，HEAD `5cd90dd7565a4668016217a56cd2876383869560`，已读取其 AGENTS、README、CLI、配置、catalog、installer、native manifest / preparer、CMake wrapper、许可与第三方通知。12 项主要来源文件的字节 / SHA-256 记录在 `teacher-runtime-preflight/local-source-metadata.json`。本轮未修改 Tomur、构建程序或运行 CLI。

检查仅限 PATH / `Get-Command`、明确固定路径和已命名小目录的非递归列表。PATH 没有 `tomur.exe`；以下候选均未发现程序 / bundle：

- `D:\source\Tomur\app\bin`，包括已明确检查的 Release `net10.0`、`net10.0\win-x64\publish` 与 Debug `net10.0` 候选。
- `D:\source\Tomur\.artifacts`、`D:\source\Tomur\artifacts`。
- `D:\source\Tomur\native\runtimes\win-x64\native`。
- `C:\Users\mysti\AppData\Local\Programs\Tomur\Tomur.exe` 与默认数据目录中的 `Tomur.exe`。
- 默认 `C:\Users\mysti\AppData\Local\Tomur` 数据目录也未出现，`TOMUR_DATA_DIR` 未设置。

这些事实只覆盖所列候选，不证明全机没有其他安装或服务。没有递归扫描用户目录、Program Files 或盘符根目录。

Tomur `Program.cs` 明确分发 `serve`、`pull`、`native`、`doctor` 等命令；服务命令为 `Tomur.exe serve --urls <loopback URL> --data-dir <absolute path>`。`--urls` 优先于 `ASPNETCORE_URLS` 和配置；`--data-dir` / `TOMUR_DATA_DIR` 可指定隔离目录。服务启动会创建配置 / 本地状态，并执行 `NativeBundlePreparer.Prepare()`，属于写入动作，本轮未执行。采用任务专用数据目录，避免默认用户状态：

```text
D:\source\Sezika\.artifacts\teacher-tomur-data
  config\tomur.json
  models\text\qwen3.5-9b-q4-k-m\Qwen3.5-9B-Q4_K_M.gguf
  models\models.manifest.json
  runtime\<bundle-id>\<version>\runtimes\win-x64\native
  logs\
  tomur.db
```

已核对 `ModelPackage.RelativeDirectory` 为 `<segment>/<folder>`，此目标为 `text/qwen3.5-9b-q4-k-m`。`pull qwen35-9b-q4km` 遇到已存在文件且无 `--force` 时跳过网络下载，重新计算 SHA-256 并登记安装清单；该行为可用于固定下载完成后的本地登记。**installer 不以 `package.SizeBytes` 比较实际文件长度**，而是验证 `ExpectedSha256` 后将 `FileInfo.Length` 写入安装清单，因此旧 catalog 的大小不会阻断上述正确已有文件的登记。`pull` 不支持任意固定 revision 参数，其 HF resolve 使用 `main`；下载 `HttpClient.Timeout` 为无限且 CLI 传入 `CancellationToken.None`，因此后续不得裸执行无界 `pull`。固定资产应先下载、核验并原子放入准确目标路径，再以有界 `pull` 登记已存在资产，登记过程失败就停止采集。

Tomur 自有源码为 Apache-2.0；GGUF 教师路径使用 llama.cpp / ggml native runtime。`native/llama.cpp` gitlink 与实际 submodule HEAD 均为 `1bc7a5af0d14b1fb72f266abbd1237b394187115`，该子模块 LICENSE 为 MIT，1,078 字节，SHA-256 `94F29BBED6A22C35B992C5C6EBF0E7C92F13B836B90F36F461C9CF2F0F1D010D`。`Tomur.csproj` 固定 .NET 10.0.5、Microsoft.Extensions.AI 10.9.0、Microsoft.Agents.AI 1.19.0 与 OpenTelemetry 1.16.0 等直接依赖，源码构建会按 `web/package-lock.json` 执行 npm 安装 / 前端构建。主许可不能替代解析后的第三方依赖、模型或可选 CUDA / cuBLAS 许可；本次没有制作分发包或宣称完整 SBOM 审查通过。

## 最小 CPU bundle 获取路径

主 `native/bundle.manifest.json` 登记组件来源 / 版本 / 动态库名称，却没有发行下载 URL 或预登记逐文件 SHA-256。`NativeBundlePreparer` 从随程序的 source bundle 复制文件并计算本地 hash，不能作为上游发行资产 hash 绑定的证明。`deploy/cpu.bundle.manifest.json` 同时要求 plate 等库，不能直接把它写成仅四库的文本教师 bundle。

只读检查公开 [`IoTSharp/Tomur` latest release API](https://api.github.com/repos/IoTSharp/Tomur/releases/latest)：直连 TLS reset 后唯一代理返回 HTTP 404 / `Not Found`。本轮未取得固定 tag、release id、Windows 程序或 CPU native asset / digest，也未追查其他网络文件；404 只说明本次公开接口没有提供可用 latest release，不能推断全部历史 / 私有发行不存在。小 body 与日志保留在 `teacher-runtime-preflight/release-assets`。

可实际安排的最小源码路径是 `native/llama.native` 自带 `windows-x64` CMake preset：使用 Visual Studio 18 2026，只构建 llama / ggml 与本包装层的 mtmd / vlm 桥，不展开 Whisper、OCR、TTS、stable-diffusion、plate。公开 CLI `native build --backend cpu` 在 `NativeBuildPlanner` 中会展开全部 6 个组件，不能把该 CLI 命令写成仅 llama 的最小构建。

已用 PATH / 明确固定路径核实：

- dotnet：`C:\Program Files\dotnet\dotnet.exe`。
- CMake：`C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe`，文件 ProductVersion `4.3.1-msvc1`。
- MSVC / MSBuild 复用机器级已知 VS 2026 固定路径，无新扫描或安装；项目 Windows preset 明确采用 MSVC。

监督者已另行开始最小 CPU wrapper 构建，配置成功 PID 32372；本证据子任务没有执行构建。实际构建使用 Sezika `.artifacts` 的任务独占 build / install 目录，源仍为 `D:\source\Tomur\native\llama.native`，VS18 2026 / MSVC 与上述 CMake，install / build 上限 600 秒、parallel 2；不写 Tomur 源目录的默认 preset build 输出。C# host 构建另设上限，监督者计划禁用 Web assets 增量构建。构建后登记准确产物路径、版本、全部 native 依赖闭包 / hash，再执行 `--help` 15 秒、`native prepare` / `doctor` 各 45 秒极小试运行。来源、ABI 导出、Qwen3.5 GGUF 支持、库加载和真实推理仍须由实际构建 / smoke 验收。

`NativeBundlePaths` 从 `AppContext.BaseDirectory` 向父目录寻找首个 `native/bundle.manifest.json`，source runtime 则解析为该 manifest 目录下 `runtimes/win-x64/native`。任务安装输出需复制为以下布局，逐项 hash 核对；只操作任务产物，保留源安装文件：

```text
<监督者的任务 host-output>\
  Tomur.exe / Tomur.dll / 依赖
  native\bundle.manifest.json
  native\runtimes\win-x64\native\
    llama.dll
    ggml.dll
    ggml-base.dll
    ggml-cpu.dll
    tomur-llama-mtmd.dll
    tomur-llama-vlm.dll
```

manifest 使用任务副本，仅保留 `llama` 与 CPU variant，声明准确库闭包与独立 bundle ID / version，并记录原 manifest 的 SHA-256 与派生步骤；不使用包含 plate 必需库的 `deploy/cpu.bundle.manifest.json` 来声称最小文本 bundle 已完整。`serve` 会据此复制到 `<data>/runtime/<bundle-id>/<version>/runtimes/win-x64/native`。此布局来自实际路径解析源码，文件存在 / ABI / doctor 与真实 forward 仍待监督者验证。

## 实测资源与有界执行方案

只读 `nvidia-smi` 固定路径为 `C:\Windows\System32\nvidia-smi.exe`。PID 44336，exit 0，runner 上限 15 秒，官方记录实际 2.354 秒，cleanup errors 为空，随后按记录 PID 查询无存活对象。输出为 RTX 4070 Laptop GPU、总 8,188 MiB / 空闲 7,625 MiB、driver 596.08、compute capability 8.9。系统当时总 RAM 68,137,205,760 字节、空闲 RAM 25,552,248,832 字节，D 盘空闲 28,852,973,568 字节。GPU / RAM 是当时可用量，不是模型加载或生成峰值。

建议首次采用 CPU / `gpu_layers=0`，任务进程环境设置 `DOTNET_PROCESSOR_COUNT=4`，对应源码 `Environment.ProcessorCount` 的 llama 线程数；不使用不存在的 Tomur `--threads` / `--memory-limit` 参数。任务配置 schema 2，显式 `runtime.accelerator.preference="cpu"` 与 `gpu_layers=0`，各 `paths` 固定到任务数据目录；不改用户配置。模型声明 minimum RAM 为 16 GiB，实际 resident / KV / scratch 未测得；外部监控进程 resident 达 16 GiB 或系统空闲 RAM 低于 8 GiB 时取消 owned 服务树，记录失败，不靠静态 GGUF 大小推断运行已满足。Tomur 没有 `enable_thinking` 请求字段；Qwen3.5 / qwen35 名称匹配分支在 assistant 头后预填充 `<think>\n\n</think>\n\n`，后续需确认实际发现的模型名字会进入该分支，并以首题真实回执核对非思考输出。

以下是后续任务的具体顺序与界限，均尚未执行：

1. **准备程序 / CPU native。** 按前述最小入口和固定子模块构建；构建槽由监督者协调，失败后不重复扩展组件 / 更换工具链。核实实际 `Tomur.exe` / DLL 与 4 个主文本必需库 `llama`、`ggml`、`ggml-base`、`ggml-cpu`，保留 wrapper 安装的桥及许可，不将“文件存在”写成模型 forward 通过。
2. **固定 GGUF 下载。** 使用 `https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/3885219b6810b007914f3a7950a8d1b469d598a5/Qwen3.5-9B-Q4_K_M.gguf?download=true`，仅 1 资产、直连 20 秒失败后唯一代理 1,200 秒，整体 1,300 秒、下载磁盘预算 8 GiB；HTTPS 重定向上限 3，curl `--retry 0` / `--max-filesize 5680522464`。创建前登记 GUID partial 绝对路径，收到完整文件后校验 5,680,522,464 字节与固定 SHA-256，通过才原子改名到上述模型目录。取消 / 超时只清理本任务 partial；已通过的交付物保留。
3. **本地登记和启动。** 有界 `Tomur.exe pull qwen35-9b-q4km --data-dir <上述固定目录> --no-proxy`（90 秒，无 `--force`，已有固定文件已先核验），然后以 `serve --urls http://127.0.0.1:5148 --data-dir <同目录>` 启动 owned 单进程，固定服务寿命最多 1,800 秒，无 `--open`、不安装系统服务。启动前先读该端口监听 / 原有服务状态；发现已有对象就保持并停止本任务。服务启动与退出均记录 PID、创建时间、完整 argv、父链。
4. **先 1 题试运行，再最多 32 题。** 健康 / models / runtime 三个 endpoint 最多各 3 次、单请求 10 秒、总 60 秒，失败间 1 秒退避；不因健康接口成功宣称 native 推理已就绪。首题使用实际发现的模型 ID、`context_size=2048`、`max_tokens=128`、`temperature=0`、非流式、单请求 120 秒；先检查真实 HTTP / 模型错误和严格教师 schema。通过后最多 32 个非封存通用样本、串行并发 1、每题 90 秒、整轮最多 1,500 秒，单 body 128 KiB；失败保留原始回执并停止扩量。Tomur OpenAI 请求确有 `context_size` 字段；Qwen3.5 源码有空 `<think>` 前缀以选非思考输出，其真实行为仍需首题确认。
5. **取消、退出与收尾。** 请求 / 监控循环同时限制最多 1,800 次和服务 1,800 秒，取消 marker 每秒检查、每 30 秒进度、1 秒 backoff。完成时仅对 owned endpoint 调用 `/api/runtime/session/unload` 一次（10 秒），然后停止本任务记录的服务。正常、失败、取消、超时均走 `finally`：按 PID / 创建时间 / 完整命令行 / 父链确认归属，再回收 owned 完整子树；最多 6 轮 / 10 秒，不按名称杀进程。保留模型、native 产物、输出和日志；临时对象只在绝对路径、创建归属与无需保留均已核对后处理，最多 16 项 / 10 秒。

任何后续生成的循环先人工检查退出比较，并以 1 项 / 短超时试运行；上述预算是下一工程任务的显式计划，不能被描述为本轮执行证据。GPU 加速、满 offload、吞吐 / 峰值、32 题完成、数据质量和训练准入均未取得新证据。

本轮只读工作总界限为 15 分钟，网络使用 4 个小文件输入（官方 3 个、release API 1 个），每个最多直连 / 唯一代理，未达到 6 个小文件限额。官方许可、release 与 GPU 的子进程均由既有 bounded runner 记录 / 回收；没有启动 Tomur、构建、安装、下载大权重或执行教师作答。所有证据与交付文档保留，Git 与共享文档由监督者复核提交。
