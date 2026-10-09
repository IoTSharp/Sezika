# 当前工具与实验合同

本参考描述仓库已经执行的固定实验。未来身份或合同变化须先同步参数和验收绑定；下面的数值不是允许自动追加的训练计划。

## 工具入口与迁移前核对

路径相对仓库根。先检查实际参数和源码，再构造执行命令；本技能不提供一条会误连历史主机的默认训练命令。

| 工具 | 已有用途 | 新任务须核对的绑定 |
| --- | --- | --- |
| `tools/rental-training/Prepare-Package.ps1` | `-Pilot` 准备两文件探针；正式准备；`-VerifyOnly` 复核 | 默认 TaskDirectory、旧 `token-limit-20261009-8192/upload-staging` 路径、模型/数据 SHA、package task ID、来源和许可；Pilot 用独立任务目录，不能留在正式 prepared 中 |
| `Invoke-RentalTraining.ps1` | 参数化 SSH、预检、上传、合同/tiny/训练、优先下载、验证/回归、收集 | task ID/远端后缀仍固定；deadline JSON、阶段最坏耗时及 overlay 选择；当前模型/结果解包逻辑须先探针 |
| `Invoke-Ssh.ps1`、`Askpass.ps1/.cmd` | stdin Bash、真实 exit、独立 known_hosts、内存认证 | 依赖的 PS7/OpenSSH/stdin wrapper 的准确本机路径；不持久化环境密码 |
| `Invoke-BoundedProcess.ps1` | Windows 任务树登记、有界守护与精确回收 | 每命令实际墙钟、取消文件、任务专属日志；上限不能超过剩余租期 |
| `remote/preflight.sh` | 只读系统、磁盘、GPU/使用者、依赖预检 | 实际 host/GPU/driver 身份；不能复用旧快照 |
| `remote/lib.sh`、`install.sh`、`stage*.sh`、`collect.sh` | 身份/安全解包/锁/阶段守护/结果清单 | lib task/root 校验、stage 和 install 的 manifest SHA、output 名、唯一训练限制、资源阈值、contract overlay 目录 |
| `remote/Invoke-BoundedLinux-v2.sh` | Linux PID/starttick/父链、有界执行和回收 | 作为新包经 SHA 绑定；先 `bash -n`，再 `/usr/bin/true` 小输入核 payload0/final_check1/live0/unknown0，随后才 tiny GPU |
| `Download-Model.ps1` | 下载新 head/checkpoint/report，补齐本机冻结资产，核完整模型 | RemoteRoot 校验和 task ID、报告字段与 encoder/tokenizer/model SHA、锁、UTC 期限、两个明确 scp 路径参数；唯一下载者 |
| `Verify-Experiment.ps1` | 独立读档与数值/身份/期限验收，不执行 GPU | `$expectedTask`、`$expectedRemote`、GPU UUID、全部输入 SHA、目录布局、阶段名；`-ReaderProbeOnly` 仅 reader 探针，真实新实验仍 NOT_RUN |

原冻结 `files.sha256` 必须维持其意义。需要修正调度时保留原包与失败回执，新增单独 SHA 清单绑定 overlay，不把旧 hash 配给新字节。下一次准备应把已准入版本纳入新的冻结包。修改之后确认资源/清理合同没有被放宽，并跑相应小输入。

## 本轮固定训练与验收

| 项 | 固定合同 |
| --- | --- |
| 数据 | 215：train162/dev53；Choice140/Boolean75；保留全256派生账本和原文/来源/split/family |
| 训练 | 一次24576 updates、LR0.001、seed17、strict8192、零 head、encoder 冻结；Score 新监督0 |
| 前向 | 训练231；独立验证重新编码215原题+16训练 Choice 换序=231；原修正32回归≤32；每阶段总上限384 |
| 资源 | GPU 并发1、owned≤3GiB、host≤4GiB、free≥5120MiB；独占选定设备，不停止他人工作 |
| 超时 | C# 训练/独立验证 CLI 各≤1500s；SGD≤300s；回归 C# CLI≤300s；外层留确定的回收/收集余量 |
| 不在合同内 | 扩量、加训、dev 调参、LoRA、encoder 反传、额外教师请求；需新的用户范围和计划 |

24576/162 是151个完整周期加114个前缀，不称128完整 epoch。独立验收按 record ID 对齐逐题内容，训练 sample_visits/固定 seed 顺序仍须单独精确重放。确定性 head 与旧实验同 SHA 可能正常；通过新 root/GPU/PID/starttick/argv/report/输入 SHA 的绑定证明实际新训练。

## 限时与下载预算

`deadline.json` 最少记录：`rental_created_utc`、`rental_end_utc`、`deadline_utc`、`download_reserve_seconds`、`new_gpu_launch_cutoff_utc`，并给用户所在时区的明确时间。优先目标≤租期末；回收可用租期末但不能据此放宽用户模型下载目标。该 JSON 不存凭据。

阶段准入预算要覆盖当前阶段、剩余必要 GPU 阶段、下载和哈希、回收，不只比较当前 CLI timeout。本轮预留900秒并不适用于任意网络或权重：完整包1,437,381,120 bytes，上传实际约1401秒；下一次根据小传输实测和实际资产决定余量。无需重复下载可 SHA 核验的本机冻结 encoder，但必须及时下载新 head/report/checkpoint。剩余不足时禁止新增 GPU 阶段，以失败或部分完成回执收口。

JSON 解析后日期可能已经是 DateTime。保持 Kind/offset，DateTime 调 `ToUniversalTime()`，DateTimeOffset 保留 offset；字符串用显式 Z/offset 和 RoundtripKind。不要把已转换日期再按本地文本 Parse。

## 最低证据集合

本机准备 manifest/逐文件清单/整包 byte+SHA、来源许可；新机只读预检和 GPU/boot 身份；安全安装/动态依赖/合同/tiny；唯一训练/独立验证/回归的 argv、PID/starttick/父链、payload/runner exit、逐题输出与输入/输出 SHA；资源前后及清理；完整模型 `model-download.json`；结果包 SHA/inventory；本机 `verification.json`；期限与完成时间。

验收缺关键证据记 NOT_RUN，证据不符记 FAIL；不会因 HTTP200、进程退出0、目录存在或训练 CE 下降自动改为质量通过。永久证据仅复制经过检查的原字节并记录映射，不包含大权重、私钥/密码/token或无关用户文件。
