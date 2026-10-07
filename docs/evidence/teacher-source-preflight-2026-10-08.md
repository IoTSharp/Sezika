# 离线教师来源预检查：2026-10-08

本轮只读预检查取得 `unsloth/Qwen3.5-9B-GGUF` 的固定 revision 与目标 GGUF 的 LFS 元数据。**目标 hash 与监督者提供的 Tomur catalog 值一致，但大小不一致**：上游登记为 5,680,522,464 字节，catalog 登记为 8,953,803,264 字节。量化仓库固定 README 声明 Apache-2.0，并链接到上游浮动 `main/LICENSE`；量化仓库的当前完整 siblings 元数据未列出独立 LICENSE 文件。默认 `127.0.0.1:5137/v1/models` 单次连接失败。本轮没有下载 GGUF、启动教师服务、安装工具、生成教师答案或读取正式封存题，不形成教师来源正式审签或全机服务不存在的结论。

## 固定身份与 catalog 差异

API 请求为 [`https://huggingface.co/api/models/unsloth/Qwen3.5-9B-GGUF?blobs=true`](https://huggingface.co/api/models/unsloth/Qwen3.5-9B-GGUF?blobs=true)，取得 40 位 commit `3885219b6810b007914f3a7950a8d1b469d598a5`。API 仅用于发现当前身份；后续 README URL 固定到该 revision。待核对 catalog 项由监督者提供：`qwen35-9b-q4km`，目标 `unsloth/Qwen3.5-9B-GGUF/Qwen3.5-9B-Q4_K_M.gguf`；本轮未修改 Tomur 源码或 catalog。

| 项目 | API 固定快照 | 待核对 catalog | 结果 |
| --- | --- | --- | --- |
| 目标文件 | `Qwen3.5-9B-Q4_K_M.gguf` | 同名 | API 中恰好 1 项 |
| revision | `3885219b6810b007914f3a7950a8d1b469d598a5` | 下载 URL 使用浮动 `main` | 后续资产须明确固定 revision |
| `size` / `lfs.size` | 均为 `5,680,522,464` | `8,953,803,264` | 不一致 |
| `lfs.sha256` | `03b74727a860a56338e042c4420bb3f04b2fec5734175f4cb9fa853daf52b7e8` | 同值 | 元数据一致 |
| `blobId` | `111d9180ac190fcfea6f515887e3c1716ba1bba8` | 未提供 | 记录 LFS pointer 的 Git blob 身份 |
| LFS pointer 大小 | `135` 字节 | 未提供 | 不等于 GGUF 大小 |

上述 hash 是 HTTPS API 中的 LFS 声明，本轮未计算实际 GGUF 文件 hash。资产使用前仍须以固定 revision 下载或核对本地同身份文件，再校验实际长度与 SHA-256；不能将 metadata 匹配写成实际权重已验证。实际 GGUF 约 5.29 GiB；该数字仅由元数据换算，不能据此宣称加载峰值、上下文内存或 GPU 资源已满足。

## 许可文字范围

固定 [README](https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/blob/3885219b6810b007914f3a7950a8d1b469d598a5/README.md) 为 78,888 字节，SHA-256 `E50F8066E39C38CBC2DA5DD94B9A9A2712B64C4921F107A5ADB4B3CA5B6BBAC0`。本地固定快照中：

- 第 5 行：`license: apache-2.0`。
- 第 6 行：`license_link: https://huggingface.co/Qwen/Qwen3.5-9B/blob/main/LICENSE`。
- API `cardData.license` 同样为 `apache-2.0`，`base_model` 为 `Qwen/Qwen3.5-9B`。
- 量化仓库完整 API siblings 未列出 `LICENSE`、`LICENSE.txt`、`LICENSE.md` 或 `COPYING`；因此本轮明确登记“量化仓库未列出独立许可文件”，未构造文件存在或完整授权正文已审查的证明。

声明与上游链接为来源事实；链接仍浮动于另一仓库的 `main`，本轮未读取该上游许可全文，也未把它绑定到独立固定 revision。教师模型的完整许可、量化产物归属、生成输出 / 蒸馏用途范围及数据人工审签仍需明确登记。`HumanReviewComplete=false`；没有代替用户进行法律或数据审签。

## 请求边界、工具与清理

本轮脚本与保留证据位于 `D:\source\Sezika\.artifacts\supervision-20261008\teacher-preflight`，脚本为 `Read-TeacherSource.ps1`。PowerShell 使用 `C:\Program Files\PowerShell\7\pwsh.exe`，实测 7.6.6；curl 仅通过 PATH / 已知固定路径发现为 `C:\Windows\System32\curl.exe`，版本 8.21.0，未安装或广域扫描。

人工核对循环使用 `attempt -lt attemptLimit` 与比较式退出条件，AST 解析通过后先进行单 API pilot，再读取其他输入。最大 4 个小输入、每个 Internet 文件 1 次直连与失败后唯一 1 次 `http://127.0.0.1:7890` 代理，单请求 30 秒、响应 1 MiB；curl 禁用内部重试、限定 HTTPS / HTTPS 重定向与 3 次跳转、连接期限 5 秒。总请求执行预算 300 秒，290 秒停止新增请求，开始请求前保留请求 / cleanup 时间；每次失败重试间退避 500 ms。取消标记为该目录下 `cancel.request`，outer runner 超时 / 取消后清理其记录的任务树。

实际只使用 3 个输入 / 5 次请求：API 两次、README 两次、本地接口一次。唯一 loopback 探测使用 `http://127.0.0.1:5137/v1/models`，10 秒、128 KiB、无重试 / 无代理 / 无重定向。所有请求由 `tools/Invoke-BoundedProcess.ps1` 启动，curl outer 上限分别为 35 秒和 loopback 15 秒，记录 PID、创建时间、完整 argv 与父链。pilot / remaining 两个 outer PowerShell 上限为 90 / 105 秒，二者均成功返回；不代表每个网络请求均成功。

| 操作 | PID | 退出码 | runner 实际用时 | 结果 |
| --- | ---: | ---: | ---: | --- |
| API 直连 | 41516 | 28 | 8.895 秒 | 连接超时，没有响应 body |
| API 唯一代理 | 103480 | 0 | 4.643 秒 | HTTP 200，15,916 字节 |
| 固定 README 直连 | 49028 | 28 | 8.351 秒 | 连接超时，没有响应 body |
| 固定 README 唯一代理 | 23896 | 0 | 5.232 秒 | HTTP 200，78,888 字节 |
| 默认 loopback 单次探测 | 37772 | 7 | 5.191 秒 | 约 2.022 秒连接失败，没有响应 body |
| API pilot outer | 2876 | 0 | 18.232 秒 | 小输入流程完成 |
| remaining outer | 87172 | 0 | 24.000 秒 | README / loopback 状态记录完成 |

共享 batch 从 `2026-10-07T17:11:38.3757780Z` 开始，在 `2026-10-07T17:13:12.3104921Z` 写入完成状态，共 93.9355155 秒，其中包括两个流程之间的只读核对；remaining outer 随后完成 runner 收尾，仍在 300 秒界限内。响应保留前计算精确字节 / SHA-256，GUID partial 在创建前登记绝对路径；`finally` 仅处理本任务目录下已登记的 `.partial`，最多 8 项 / 10 秒，不删除成功响应、日志或其他任务对象。

`verification-summary.json` 在 `2026-10-07T17:14:28.6128003Z` 复核 7 份官方 process result，cleanup errors 全空；按记录 PID 查询无存活对象，登记 partial 无剩余文件。两个保留小文件重新核对通过：

| 证据文件 | 字节 | SHA-256 |
| --- | ---: | --- |
| `hf-api.json` | 15,916 | `590CBE63E9BCF9B9B7C15AD33EBD97687B338BF57571F7BF6C2C759831B30E70` |
| `README.source.md` | 78,888 | `E50F8066E39C38CBC2DA5DD94B9A9A2712B64C4921F107A5ADB4B3CA5B6BBAC0` |

`state.json` 记录每次 URL、代理状态、响应路径 / hash、精确唯一 process result 路径和 partial 归属；各 `*.identity.json` / `*.result.json` 保留进程 argv / 父链。只保留本批小文件与证据，没有创建模型大资产、教师作答或后台服务。

## 后续门槛

32 题通用教师 pilot 的下一步需先纠正 catalog 大小并固定下载 revision，完成上游许可全文 / 来源与数据范围登记，然后根据明确本地安装位置或配置决定是否下载、启服务或使用其他监听地址。默认 loopback 本次不可连接只说明这一地址当时不可用，不证明全机没有安装 Tomur 或不存在其他教师接口；本轮未发现或扫描全机服务。正式运行还应记录实际权重 hash、服务 / tokenizer 身份、输入来源、输出审计与人工审核状态，继续与正式封存题隔离。
