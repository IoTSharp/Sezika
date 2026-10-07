# 独立原始资产恢复：2026-10-08

固定原始 mmBERT 资产、runtime package 与真实 scalar CPU encoder smoke 已恢复并核验。北京时间日期为 2026-10-08，进程日志统一记录 UTC。**四个原始文件逐个匹配固定字节 / SHA-256；纯 C# 转换得到 134 个 encoder tensor，输出匹配固定字节 / SHA-256；真实 loader / shape / finite forward 通过**。首批权重请求在 300 秒上限超时并清理 partial，随后另行授权的单权重任务成功，再以独立的 400 秒预算完成转换与 smoke。这些证据为 S4-15 的独立数值 oracle / 实际推理提供输入；不构成 trained head 质量、上游数值对照、校准、CUDA 或 AOT 模型推理验收。

## 来源、身份与许可发现

来源为 [`jhu-clsp/mmBERT-base`](https://huggingface.co/jhu-clsp/mmBERT-base/tree/c5955035435e2bf121cde7f3c8863ef52ff35d82)，revision `c5955035435e2bf121cde7f3c8863ef52ff35d82`。下载 URL 固定为该 revision 下的 HTTPS `resolve/<revision>/<filename>?download=true`，不选择浮动分支，不从 Laya checkpoint 提取或重命名 encoder。来源与 hash 由仓库 `model-manifests/mmbert-independent/{source_lock.json,model.json}` 登记。

固定 README 的 29,650 字节与 SHA-256 `35724415037ee3159aa733c16050620cc19a76219112a6ceeb3949572e0b37e9` 已再次匹配。实际文字证据为：

- 第 2 行：`license: mit`。
- 第 1827 行：`[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)`。
- 第 1925 行：`| Tokenizer | Gemma 2 | Gemma 2 |`。
- 未找到 `Permission is hereby granted` 授权正文。既有 source lock 的 `full_mit_text_in_source_card: true` 与实际固定卡不符，本轮按监督者授权纠正为 `false` 并记录事实；固定模型/hash/revision 及 `source_identity_verified_license_scope_requires_manual_review` 状态均保持。

MIT 标签、授权链接与内部工程下载用途已经核对；完整版权/授权通知、Gemma 2 tokenizer 归属和公开发布审签仍 pending。模型标签不授予原始训练数据再利用/再分发权，也不能代替正式许可审签。

## 固定文件与资源

| 文件 | 字节 | 固定 SHA-256 | 当前证据 |
| --- | ---: | --- | --- |
| `source/README.md` | 29,650 | `35724415037ee3159aa733c16050620cc19a76219112a6ceeb3949572e0b37e9` | 下载 / hash 通过 |
| `source/config.json` | 1,188 | `47b40fd2e1df8299426dd5f4bb18c28f028cfafcb51b73645f83e596d187eb37` | 下载 / hash 通过 |
| `source/tokenizer.json` | 17,525,329 | `197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356` | 下载 / hash 通过 |
| `source/pytorch_model.bin` | 1,231,188,142 | `8ea64ec1ea4eb8fca0fc14b69a2ae571de6bfbc25fd214bb932dd4aba6a3a04e` | 第二独立任务完整下载 / 字节 / hash 通过 |
| `converted/encoder.safetensors` | 1,227,772,549 | `6168c84d2498cdacdcafee16b71a69b5c1636ad569bfabaf2855f5ce7dabd528` | 134 tensor 转换 / 字节 / hash 通过 |
| `converted/model.json` | 1,225 | `c84245ecd2b8f77c35dc02847b62643caf218239dd47bc6d556ffa7bafb1ce3b` | manifest 副本 / hash 通过 |
| `converted/source_lock.json` | 1,970 | `034e7adc8f223962c5c914136860cbe869a0b5f9e29917fd1ce5df59c7f4d00f` | 已纠正许可事实的 lock 副本 / hash 通过 |
| `converted/tokenizer/tokenizer.json` | 17,525,329 | `197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356` | runtime tokenizer 副本 / hash 通过 |

固定目录是 `D:\source\Sezika\.artifacts\models\mmbert-independent\source` 与 `converted`。4 个原始文件完整下载总量应为 1,248,744,309 字节；保留源文件、转换 encoder 与 runtime tokenizer 副本约 2.494 GB。任务磁盘预算 4 GiB，预检查 D 盘空闲约 31.7 GB；系统可用内存约 24.3 GiB，计划转换/loader 使用单一进程、预估不超过 3 GiB resident。权重属于外部忽略资产，未嵌入二进制或 NuGet。

只使用现有 Release 纯 C# 受限转换工具，不调用 Python `torch.load`、不执行 pickle 代码、不新装工具。下载阶段登记的历史二进制身份如下；实际转换阶段监督者重新构建后的身份另列于第三阶段：

- `Sezika.IndependentModelTool.dll`：161,792 字节，SHA-256 `48854A3093C21F7EA2DB5BBD4772A123D408B08E6C51396F59542DEF4FB97174`，mtime `2026-10-07T15:51:01.6023098Z`。
- 同目录 `Sezika.dll`：SHA-256 `4EA04B9A8D72C9E842052D8F16506B2ECB2E33A4DA0ABAC7F4957C5BE2880DD6`。
- 监督者确认来自前轮 Release solution 构建 PID 100308，0 warning / error；源码未改，参考 HEAD `3a7435e5618c1c94c6e346fe16a9c2e1addf3d3f`。已知 dotnet 为 `C:\Program Files\dotnet\dotnet.exe`，PowerShell 为 `C:\Program Files\PowerShell\7\pwsh.exe` 7.6.6。

## 下载过程与失败保留

脚本为任务专属 `.artifacts/supervision-20261008/asset-restore/Restore-Assets.ps1`；下载 / outer PowerShell 均由 `tools/Invoke-BoundedProcess.ps1` 启动，记录 PID、开始时间、完整 argv、父链、结果及 cleanup。循环先人工核对为 `fileIndex -lt 4` 与 `attempt -lt 2`，AST 解析通过后先运行单 README 极小 trial。

首个 trial：直连 curl PID 3388 返回 28（连接超时），仅一次代理请求 PID 50708 返回 HTTP 200，README hash 通过。原脚本按 source lock 的错误“卡内完整 MIT 正文”声明设置硬检查，trial 因此失败，outer PID 53788 / exit 1；失败日志保留。改为实际 MIT 声明校验后，PID 26680 trial 成功且复用已校验 README，没有重复下载。独立审查者再次核对 README hash / 片段，并指出历史 result 匹配缺陷；现脚本每 run 使用唯一短 GUID 的 LogName，保留旧日志同时精确选择本次进程结果。

批量 outer PID 15428；每文件最多 1 次直连与直连失败后的 1 次 `http://127.0.0.1:7890` 代理尝试，不改全局代理。每直接请求最多 20 秒、README/config/tokenizer 代理各最多 60 秒、权重代理最多 300 秒；下载批次总墙钟 660 秒，outer 690 秒，总任务 20 分钟。curl 使用 `--retry 0`、HTTPS 重定向限制、10 秒连接期限和精确 byte / metadata 上限。失败间有 500 ms 退避。

进程 / 下载证据位于 `.artifacts/supervision-20261008/asset-restore`。初始 `download-pilot.json` 保留失败，修正 trial 为 `download-pilot-20261007-162632-823.json`；完整批次结果为 `download-restore-20261007-162721-929.json`。下载 partial 通过独占 GUID 命名并在创建前登记 `ownership-<mode>.json`，finally 仅删除已登记、绝对路径属于固定 source 目录的 partial；最多 8 项 / 10 秒 cleanup。已经 hash 通过的源资产及所有日志保留。

| 文件 | 直连 PID / 退出码 | 唯一代理 PID / 退出码 | 实际代理结果 |
| --- | --- | --- | --- |
| README | 3388 / 28 | 50708 / 0 | HTTP 200，29,650 字节，3.464547 秒 |
| config | 104756 / 28 | 76832 / 0 | HTTP 200，1,188 字节，2.182576 秒 |
| tokenizer | 54748 / 28 | 41208 / 0 | HTTP 200，17,525,329 字节，43.479731 秒 |
| 原始权重 | 69300 / 28 | 65404 / 28 | HTTP 200，563,224,453 / 1,231,188,142 字节，300.039239 秒；不完整文件已清理 |

首批权重实际平均速度为 1,877,172 字节/秒。该速度记录解释本次短预算失败，不能推断来源永久不可达。首批没有发第 3 次权重请求，也没有放宽该批预算；第二个单权重任务另行登记、授权、设置范围并保留独立日志。

outer 批次 PID 15428：官方 result 记录 exit 1，耗时 398.729 秒，`CleanupErrors: []`。完成后立即释放转换 / forward 的单一运行槽。`verification-summary.json` 在 `2026-10-07T16:35:34.7416750Z` 再次核对全部 11 份任务进程结果：cleanup errors 全空、登记 PID 无存活对象、登记 partial 无剩余文件、原始权重与转换 encoder 均不存在；三个保留小文件再次 SHA-256 匹配。只保留已校验模型输入及证据，没有遗留本任务进程或不完整权重。

## 第二独立单权重恢复任务

监督者根据首批速度另行授权：仅原始权重 1 个文件，最多 1 次直连（20 秒）、失败后 1 次代理（1200 秒），总任务墙钟 1500 秒、磁盘 4 GiB。新脚本 / 日志位于 `.artifacts/supervision-20261008/asset-restore/weight-second`；不复用首批已删除 partial，不改变首批请求数或时间限制。

重新核对 PowerShell 7.6.6、D 盘约 31.5 GB 空闲，人工复核 `attempt -lt 2`、1 文件、1500 秒和最多 2 partial / 10 秒清理。AST 通过后，小 trial 只复用 README 的 29,650 字节 / 固定 hash，无网络请求，PID 87400 成功。实际下载 outer PID 3748 上限 1400 秒；首次直连 PID 63920 / curl 28，唯一代理 PID 65584 / HTTP 200 / curl 0。

代理实际下载 1,231,188,142 字节，用时 196.909479 秒，平均 6,252,559 字节/秒；完整长度和 SHA-256 `8ea64ec1ea4eb8fca0fc14b69a2ae571de6bfbc25fd214bb932dd4aba6a3a04e` 通过后，才将本任务 GUID partial 原子改名为固定 `source/pytorch_model.bin`。outer 官方 result 为 exit 0、219.783 秒。源卡 / 配置 / tokenizer 保持先前已验证身份，没有重新请求小文件。

真实结果为 `weight-second/weight-download-fef29e11e509.json`，进程证据为 `20261007-164136-976-weight-second-download.result.json`、`20261007-164139-034-weight-direct-fef29e11e509.result.json`、`20261007-164153-140-weight-proxy-fef29e11e509.result.json`。`weight-second/verification-summary.json` 在 `2026-10-07T16:46:29.6062536Z` 再次计算完整源权重 hash，并确认该任务 4 份进程结果 cleanup 全空、登记 PID 无存活对象、登记 partial 无剩余文件。

第二任务严格在下载 / 字节 / hash 验证完成后停止，没有运行转换、训练、forward 或构建，也没有占用监督者正在使用的 CUDA / AOT 验证槽。完整 MIT 通知、tokenizer 归属与发布审签仍 pending；本次下载成功没有改变这些门槛。

## 第三独立转换与 CPU smoke 任务

监督者另行授权使用独占运行槽完成原始 encoder 转换、package 组装与最小 scalar CPU smoke。总执行预算 400 秒，converter 内部 / outer 上限分别为 120 / 150 秒，smoke outer 最大 180 秒；资源预算为 resident 3 GiB、磁盘 4 GiB。执行前空闲内存约 23.1 GiB、D 盘约 30.1 GB，原始权重 hash 与既有 partial 数量已核对。没有再发网络请求、运行训练 / 教师调用或读取正式封存题。

本阶段精确工具身份记录于 `.artifacts/supervision-20261008/asset-restore/conversion-smoke/metadata.json`：

- `tools/Sezika.IndependentModelTool/bin/Release/net10.0/Sezika.IndependentModelTool.dll`：161,792 字节，SHA-256 `FC13CADF84B3810AE4CC37A0AF23A969A43C4C25FE004DC85C2F2E3287DE9702`，mtime `2026-10-07T16:33:11.0544731Z`。
- 同目录 `Sezika.dll`：692,224 字节，SHA-256 `A4C377DF0001DE5BDECD6931E59B03761001CEF661FDA3277E4F0623DA3F2A60`，mtime `2026-10-07T16:32:42.6420913Z`。
- `tools/Sezika.IndependentModelTool/Program.cs`：SHA-256 `F979C789407EF25348990C4B186CB19C645380939B6CB86CFDEF78AA1E5B7FAE`。运行仍为已知 dotnet 绝对路径及 PowerShell 7.6.6。

转换参数为 `convert-pytorch --input <source/pytorch_model.bin> --output <converted/encoder.safetensors> --timeout-seconds 120`。纯 C# 转换器输出 134 个 encoder tensor，1,227,772,549 字节，SHA-256 `6168c84d2498cdacdcafee16b71a69b5c1636ad569bfabaf2855f5ce7dabd528`，与 source lock 完全匹配；decoder / MLM head 不进入该 encoder package。manifest、修正后的 source lock 与 tokenizer 共 3 个副本逐个核对源 / 目标 hash 后组成 `sezika/mmbert-base-independent-s4` runtime package，证据为 `package-assembly.json`。

首次 package guard 将 `ConvertFrom-Json` 自动生成的日期再次按本地文化文本解析，丢失 UTC 后缀并人为增加 8 小时，误报 batch deadline。保留 `time-parse-failure.json`；随后用 `ConvertFrom-Json -DateKind String` 和 ISO `DateTimeOffset.Parse` 纠正，`time-parse-correction.json` 记录当时实际已用 240.2719329 秒、剩余 159.7280671 秒。实际 400 秒 deadline 未到，未放宽批次预算。组装后根据剩余时间将 smoke outer 缩短为 95 秒，记录于 `smoke-budget.json`。

| 进程 | PID | 启动 UTC | 退出码 | 实际用时 | outer 上限 |
| --- | ---: | --- | ---: | ---: | ---: |
| 原始 encoder 转换 | 51848 | `2026-10-07T16:50:21.6421907Z` | 0 | 8.717 秒 | 150 秒 |
| scalar CPU smoke | 55812 | `2026-10-07T16:55:15.2491358Z` | 0 | 10.214 秒 | 95 秒 |

官方进程记录为 `20261007-165020-383-original-encoder-conversion.result.json` 与 `20261007-165514-456-original-independent-cpu-smoke.result.json`，完整 argv、启动时间、launcher / 子进程父链及 stdout / stderr 路径均在记录中，`CleanupErrors` 均为空。400 秒预算从 `2026-10-07T16:50:20.3114253Z` 开始，到 smoke 退出 `2026-10-07T16:55:25.4631358Z` 为止，实际执行共 305.1517105 秒；包括期间 package 组装与时钟诊断，不以单独 kernel 时间代替总墙钟。

smoke 参数为 `smoke --package <converted>`，读取实际原始权重转换产物并完成 tokenizer / encoder scalar forward。输出记录为：

| 项目 | 实际结果 |
| --- | --- |
| token 数 / marker 位置 | `46` / `25,35` |
| token IDs SHA-256 | `BAB3A41CDB62FC4D5E3353D3D88F6BBDCBD0D3CEC9C3DB537841F09604D2D80B` |
| hidden 元素数 | `35,328` |
| hidden SHA-256 | `1D94C9DA34D7FEC59A714A55ADA65001FE18F0B480B87C3542C32762B5644325` |
| hidden 最小 / 最大值 | `-40.36507` / `33.564888` |
| 工具估算 resident bytes | `1,228,282,880` |

resident 数字来自工具的资产 / 缓冲区估算，**实际进程峰值未测得**：外部采样发生在进程退出后。不得把估算写成测得 peak、性能 benchmark 或内存门槛验收，也未为补峰值重跑。token / hidden hash 仅记录本次确定性输出，尚无独立上游数值 oracle 比对。

`conversion-smoke/verification-summary.json` 在 `2026-10-07T17:03:16.3581592Z` 再次核对 5 项 source / package 交付物的字节及 hash、2 个成功进程结果、实际执行不超过 400 秒。登记 PID 无存活对象，converter partial 无剩余文件，进程 cleanup errors 均为空。保留完整源资产、runtime package 与本批证据；释放独占运行槽，不删除交付物或其他任务对象。

## 验收边界

原始资产恢复、134 个 encoder tensor 的固定转换输出及 runtime package 的真实 scalar CPU loader / shape / finite forward 已有证据。未运行训练、读取正式封存题或生成模型质量结论。独立上游数值 oracle、trained head / 多语言质量、数据人工审核与独立保管人、离线教师来源审签、真实新权重校准、CUDA 和两 RID AOT 模型推理门槛仍待分别验收；完整许可通知与公开发布审签也保持 pending。
