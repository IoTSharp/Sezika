---
name: sezika-rental-training
description: 准备和执行 Sezika 限时租用 GPU 的完整资产迁移、固定实验、优先模型下载及独立验收；适用于新实例训练和同一任务故障收口，复用仓库工具并核对其任务绑定。
---

# Sezika 租用 GPU 训练

目标是在用户给定的租期内完成已授权实验，并在本机保存可独立使用、逐文件核 SHA 的完整模型和真实执行证据。此技能不授予扩量、加训、购买实例或发布模型的权限；按当前请求和仓库 `AGENTS.md` 判断授权，不因旧交接的暂停措辞重复询问已经授权的训练。

## 先确定本次任务

1. 在当前仓库读取 `AGENTS.md`、`HANDOFF.md`、`ROADMAP.md`、`git status --short`、最新训练交接和资产来源证据。保护既有 dirty 文件和旧实验；计划只写入 `ROADMAP.md`。不要把旧报告当作新机完成证明。
2. 使用实际 SSH host/user/port/auth，新的 task ID、本机 `.artifacts/rental-training-<date>-<task>` 和独立远端根。凭据只从本次授权或已有安全凭据来源取得，不从历史用户名推导密码。缺 SSH 时继续本机准备。
3. 将实例创建时间、时区、租期末、用户提前完成目标、下载预留和停止新增 GPU 阶段的时间写入任务 `deadline.json`。提前目标不能因故障被自动延后。每次准入计算剩余全部阶段的最坏预算及传输余量；不足时优先保存已有产物、收口并报告未完成项。
4. 先读 [当前工具与实验合同](references/tools-and-contract.md)。仓库脚本仍含本轮 task/root/GPU/输入 SHA 的绑定；新日期或新服务器不能仅改 `-TaskDirectory` 就运行。将所需身份统一参数化、确认无旧目标残留、做本机小输入检查，才执行新任务。复用 `tools/rental-training/`，不复制一套训练实现。

## 准备完整资料

- 从 manifest、source lock 和证据定位真实资产，逐项核 byte/SHA/版本/来源/许可：Linux 自包含 C# 工具、PTX/ABI、实际 encoder 权重、tokenizer、模型 manifest、数据和完整派生账本、原文/split/family、修题与教师原件、回归输入及审计、监护/取消/下载入口和 SHA 清单。代码、模型、tokenizer、数据与教师来源分别列示；SHA 通过不等于获得再发布许可。
- 先跑准备工具的小输入模式，再准备和复核完整包。按实际权重大小、工作盘余量、文件数和测得带宽设预算；旧 146 MB 小包不包含完整权重，不能作为完整迁移包。包中不包含密码、私钥、token、无关用户文件或旧主机硬链接。
- 所有算子、模型及调度保持 C#；正式训练和批量特征使用 GPU。CPU 仅限 tokenizer、合同及小规模数值参考。预生成 PTX/CUDA Driver 路径不要求现场安装 PyTorch、完整 Toolkit 或第三方训练桥接；JIT 自包含工具通过不能宣称新 head Native AOT 通过。

## 执行顺序与失败边界

1. **只读预检**：记录系统/架构/libc、磁盘、boot ID、实际 GPU UUID/driver、健康与现有使用者。选择无 compute 使用者且满足预算的 GPU；不停止别人的进程，不 reset、重启或更新驱动。镜像版本建议不替代实际硬件兼容性检查。
2. **迁移准入**：本机/远端整包 SHA、逐文件 SHA、安全解包与动态依赖通过后，做数据合同，再做 tiny GPU。新 runner 或下载/解包/reader 改动分别先用极小输入检查；`bash -n` 只证明语法。
3. **唯一训练**：新根零 head、encoder 冻结，使用本次确认的固定合同。GPU 并发保持 1；并行子智能体可整理文档、只读核证据，主调度独占 SSH/GPU 和有冲突的文件。阶段目录创建后不得删除重置并重训；恢复只复核、跳过有完整成功回执的阶段。
4. **保存后立即下载**：训练 head/checkpoint/report 保存成功后先下载并核 SHA，再继续独立验证。冻结 encoder/tokenizer 可由上传前已核的本机原件补齐，最终重新核所有字节；明确哪些产物远端下载、哪些本机补齐。完整模型必须包含 manifest、encoder、tokenizer、head、checkpoint、report 和清单，不能把只有 head 的包称完整模型。保持单一下载者和锁。
5. **独立验收**：新进程从磁盘加载新 head，重新 GPU 编码全部验证输入及训练 Choice 换序输入，再跑原修正回归。下载报告、逐题结果、资源和进程回执；安全解包后运行本机只读验收，核唯一 record ID/原文绑定、softmax/NLL/argmax/CE、访问顺序、输入和输出 SHA、独立执行身份及期限。
6. **收口**：核 payload exit、runner exit、最终 live/unknown/unchecked 与实际 owned 资源归零。证据中区分 PASS/FAIL/NOT_RUN；保留原失败目录、包、回执和未经观测的历史局限。更新交接与证据，报告模型绝对路径、真实完成时间和质量限制。固定实验完成后停止新增训练；发布需当前请求明确授权和来源许可审查。

## 执行环境与证据纪律

- Windows 使用已核版本的 PowerShell 7；本机既定路径为 `C:\Program Files\PowerShell\7\pwsh.exe`。Bash 脚本用单引号 here-string，经 `C:\Users\mysti\.codex\skills\usm-gateshell-ssh\scripts\invoke-ssh-bash.ps1` 的 stdin/结构化参数传入；仓库 `Invoke-Ssh.ps1` 已封装该路径。不能把 Bash `$()` 等写进 PowerShell 可展开字符串，必须传播真实 exit。
- 密码只以 SecureString/任务专属子进程环境和 askpass 传递，finally 清环境；私钥留在本机。known_hosts 使用任务独立文件，记录首次信任方式；若用户提供可信指纹则比对，后续主机键变化停止连接。永久证据发布前移除连接标识、认证材料与敏感 argv；原件留在 ignored 任务目录。
- 每项外部任务有项目数和墙钟双界、取消、进度/backoff、PID/创建时间/完整 argv/父链及 finally 精确回收。生成循环先人工核比较退出条件，再极小试跑；不能按名称杀进程或删除共享 TEMP。短命进程的 unknown 不补造为历史成功，按登记身份追加补审。
- 工具发现限 PATH/明确配置/已知路径；网络直连失败仅该调用临时使用 `http://127.0.0.1:7890`。不要加载、运行或安装 Graphify。只清理核实绝对路径与归属的本任务临时对象，保留交付物和用户文件。
- 教师一致率、回归审计一致率、CE、概率和正式质量分别表述；训练成功不能代替人审、校准、封存测试、新 head AOT 或性能验收。发生迁移、恢复或读档故障时读 [本轮故障与证据定位](references/lessons-and-evidence.md)。
