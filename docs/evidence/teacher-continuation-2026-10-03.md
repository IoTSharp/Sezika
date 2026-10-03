# S4-10 / S4-11 离线教师合同续审（2026-10-03）

本轮完成 TeacherTool 的离线合同防护及合成负向验收。没有安装或调用 IoTSharp/Tomur，没有教师输出、人工批准或质量测量；S4-10 服务身份/许可验证与 S4-11 真实采集仍为 blocked/pending。

审计确认原实现允许重复 JSON 属性覆盖审核状态；64 KiB 行限制按字符计数；hash 与解析分别读取文件；pending 未显式计数、格式错误混入人工 rejected；检查输出已存在后仍可能覆盖；同一 family 可跨 split。本轮逐项修复：严格重复属性检查（包括转义别名与嵌套对象）、UTF-8 字节限制与严格解码、受限快照 hash、records schema 3 全状态分母、family 隔离和 CreateNew 写入。无效记录不占用合法 ID/family。

catalog schema 2 明确 `independently_verified=false`；声明 verified 仅返回 `valid_declared_verified_not_independently_verified`，不能代表模型/服务/许可审计。声明可用的 endpoint 限 loopback http(s)，禁止 URL 用户名/密码和 fragment。两种报告均不自动授予训练准入。

主线程统一 solution Release build 通过后，依次运行 `Verify-Contract.ps1 -MaximumCases 1` 和完整矩阵。PowerShell 为固定路径 `C:\Program Files\PowerShell\7\pwsh.exe`，版本 7.6.6；dotnet 通过 PATH 的 `Get-Command` 取得 `C:\Program Files\dotnet\dotnet.exe`。未发现或安装其他工具。

| 验证 | 结果 |
| --- | --- |
| 极小试运行 | 1/1 通过；外层 PID 75904，退出 0，12.728 秒；工具 PID 67516，退出 0 |
| 完整合成矩阵 | 24/24 通过；脚本 116.890 秒；外层 PID 46612，退出 0，122.734 秒 |
| 审核状态 | pending/accepted/rejected/failed/abstained 各一条时 total=5；invalid 单独计数；合法人工 rejected 的格式检查退出 0 |
| 重复字段/隔离 | 普通、转义和嵌套重复字段、重复 record ID、跨 split family、sealed_test 全部按预期拒绝 |
| 字节/记录上限 | 66000 字节 CJK padding 拒绝；无效 UTF-8、空文件和 33 条整文件拒绝；32 条 CRLF 与单 BOM 合法输入通过 |
| 内容/输出 | 可选 hash 格式和类型均检查；报告 hash 与输入匹配；已有输出未被覆盖 |
| catalog | unverified 保持 blocked；虚构 verified 仍非独立验证；远端 URL、URL 凭据与重复 status 拒绝，未进行网络请求 |
| 静态检查 | 验证脚本 PowerShell AST 解析、git diff --check 通过 |

所有字段及审核 ID 均为合成 fixture。source/model/revision/hash 格式通过不证明实际来源有效；工具不核对 hash 对应的原始输入/响应附件，不证明人工 ID 的真实性，也没有采集 token/内存资源统计或真实错误合同。这些工作必须等固定服务接口、身份与输出许可批准后完成，不能把本轮检查器改称已验收的真实采集器。

执行边界：先人工核对循环比较条件，再进行 1 项试运行。工具最多 32 条、深度 16、字节/迭代上限及 3 秒操作预算；验证脚本最多 32 项、240 秒，Ctrl+C 可取消，逐案例进度；每个工具进程使用 10 秒外部上限。外层试运行/全量分别 45/270 秒，调用现有 `Invoke-BoundedProcess.ps1`，采用可选的 5 秒进程身份查询预算；内层保留默认值。无重试或联网。进程 runner 记录 PID、启动时间、argv/完整命令行及 launcher/父链，按身份清理本次子树。

复核了 25 个工具进程及 2 个外层 launcher 的 27 份 result：`cleanup_errors` 全部为空；两次 GUID 目录下 `temporary-inputs` 均已删除。预期退出 1/3 的负向案例在通用进程 runner 中显示 Failed，验证脚本逐项检查预期退出码后才计为通过，不是未处理的执行故障。保留本次本地报告与进程日志作为审计产物：

- pilot：`.artifacts/teacher-contract-35c2ccff5d9049beadcbb43b68644dd1/summary.json`
- full：`.artifacts/teacher-contract-d34bfddd7e99418985917797eef02fc9/summary.json`
- launcher：`.artifacts/teacher-contract-launchers-20261003/*teacher-contract-{pilot,full}.result.json`

验收身份 SHA-256：

| 文件 | SHA-256 |
| --- | --- |
| TeacherTool Release DLL | `B6011137617ECA8CB3305F97BD89BAB0B48C1E30D2614316E590F27C73E839E0` |
| `tools/Sezika.TeacherTool/Program.cs` | `58849B6CAD8ECA6A2C4E6F477B990DBC88CAD10D661445F49489F198CF068FC8` |
| `tools/Sezika.TeacherTool/Verify-Contract.ps1` | `4E59DE71E4DA23F58733ADA71C449F87A416DEA2F554DA62D23E6FC0190774A6` |

本轮仅修改工具、合同文档与本证据；不修改核心推理/训练，不修改 ROADMAP/CHANGELOG，不包含 `docs/wechat-introduction.*`，子智能体未提交。
