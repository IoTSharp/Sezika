# S4-06 审核侧车离线分组工程验收

本任务提供 `group-development-report` 与有界 `check-reviewed-slices`。它仅处理最多32行已有 development 诊断报告及 hash 绑定的外部声明审核标签，不读取题文/原数据，不调用模型、GPU、教师或训练。正式质量、校准、封存、训练准入、许可与性能接受均保持 false，S4-06 整项仍进行中。

## 固定源码与实现

| 文件 | SHA-256 |
| --- | --- |
| `tools/Sezika.IndependentModelTool/Program.cs` | `A481C8F3AEA2B19ECE35D82F06AED0B34D27A524DBA3559D4A348BEDB168DA5B` |
| `ReviewedSliceDiagnostics.cs` | `D58BF81458AA3DD90BA5B7D23CB8AB8A5DF4D48FC58CB0A7200A97C7A8A3366E` |
| `ReviewedSliceChecks.cs` | `B62D71EECBF0AA1858BF63442D2CECC8C805EFC8D77E788B0D369A99BEF4E671` |

Program.cs 仅增加两个显式命令、各自30秒协作取消和帮助文本，既有入口实现未改。静态 DTO 使用 source-generated JSON；输入 hash/完整模型数据身份、development 状态、唯一记录/候选映射、finite 概率及原分母先核验，再重算逐题派生量。外部审核侧车定义版本化词表与完成审核证据；missing/pending/rejected 保留 unknown 组及全分母，不从文字或索引推断标签。

输入各1 MiB、JSON深度32、32行×32候选、4维×16值、审核证据64 KiB、输出2 MiB。工作30秒；失败发布和合成 fixture 回收分别有独立5秒预算。CPU只做这份小规模离线合同诊断，没有正式训练、批量教师或特征计算。

## 真实构建和合成验收

固定 Windows PowerShell 7.6.6、.NET SDK 10.0.401，既有 `Invoke-BoundedProcess.ps1` 记录 PID/start/argv/父链、外层预算及清理。构建使用 no-restore、disable-build-servers、单节点、关闭共享编译；没有安装、网络、模型运行或 AOT 发布。

| 执行 | 结果 | PID / 实际回执秒数 |
| --- | --- | --- |
| build1 | exit1，仅两项CS8604；首次失败保留 | 47144 / 26.061 |
| build2 | exit0、0 warnings、0 errors | 79368 / 12.2（工具摘要） |
| tiny1 | exit0、1项PASS，实际CLI/lease阻写/成功move | 87796 / 4.065 |
| full12 | exit0、十二项全部PASS | 38240 / 3.412 |

修复仅处理 null-flow、明确UTC解析及补实际CLI基线，之后三源保持上述冻结身份。十二项覆盖获审分组/重算指标、未知分母、全拒答null、异构Choice AUROC、重复/未知ID与JSON、输入身份、非法分布/候选映射、审核词表与证据、数量/字节/深度上限、move前取消、实际move后取消回退、输出冲突与文件身份改变时拒绝删除。合成输出不代表真实模型预测或真人审核。

原始四套回执和stdout位于 `.artifacts/reviewed-slices-20261008/`，前缀分别为 `20261008-004803-393-reviewed-slices-build-1`、`20261008-005436-211-reviewed-slices-build-2`、`20261008-005538-348-reviewed-slices-tiny-1`、`20261008-005548-911-reviewed-slices-full-12`。作者详细证据为 `implementation-evidence.md`。

## 审查与限制

独立静态审查 `independent-code-review.md` SHA `0589308D82F3D343DAFD9812591456370F99BDFEC577E84F5DAA1A472B760083`，已见范围无确认P1/P2；原writer与Render部分工具输出截断，不能称逐行完整审查。该报告写入在原停止新增线后38秒、绝对截止前，越过停止线的事实保留。

root另实际读取原writer行391–467、ValidateRow行143–179及发布行216–325，确认 schema 字段、rejected 的零候选计数/可省略概率、optional training_backend，以及 target probability=0 时同用 double.Epsilon 的NLL。root实际Git diff只增加两条派发/帮助路径。新的受限环境曾拒绝WMI进程查询；只读diff随后经明确权限请求运行成功，原失败保留，没有通过未审执行器绕过。

输入读 lease 维持到输出发布，GUID partial采用CreateNew和无覆盖move，move后检查取消。恢复核创建时间/长度/内容hash后才删除确证任务文件；未知修改保留。同步I/O无法被CancellationToken硬中断；读lease释放后按路径删除仍有并发替换窗口，不宣称恶意并发替换下的原子身份删除。RSS 256 MiB仅目标、未独立实测。

作者UTC00:56:30核专属artifact下synthetic-checks目录为0、已知build1 MSBuildTemp目录不存在；全部最终runner cleanup errors为空。快速check的 `Processes=[]` 是快照局限，不能称完整观察了每个短寿命子进程。最终root原字节与资源复核另保存到同artifact根；没有删除用户/共享缓存/交付物。

## 当前二进制与监督者复验

UTC02:35完成第二轮只读复验，三源身份匹配、历史四回执/原stdout通过，18个历史执行/launcher PID全absent。报告 `final-independent-verification.md` SHA `5ED5CC57764721312017D5DDD23E7D298E6B0B9918DB53492582D5A68D72164F` 明确保留历史运行DLL没有冻结hash的局限，不把当前DLL字节倒写为历史证据。

为关闭这一实质身份缺口，root另在UTC02:35–02:38执行唯一非增量本地构建，冻结三源和csproj，成功14.861秒/零警告错误；固定实际六个运行文件后先tiny1/1.463秒，再full12/1.409秒，全部PASS。源码与六文件在构建/执行后保持相同hash。这是新的当前产物证据，不覆盖作者首次失败或历史验证。

当前 `Sezika.IndependentModelTool.dll` 478,208 bytes，SHA `52880757076B191C2208FD19A6BAEAAF58F4D15F0F180EC56F2B6A0575087840`。PDB、Sezika.dll、Sezika.Cuda.dll、deps/runtimeconfig六文件清单保存于 `root-current-binaries-before.json` / `root-current-binaries-after.json`，源码冻结为 `root-current-build-freeze.json`。

| 当前实际回执（同artifact根的root-processes） | SHA-256 |
| --- | --- |
| `20261008-023502-645-root-slices-current-build.result.json` | `EA03AEA76FCE049362B7BA3B647B4024CF5D12C3540784023EF700AE1F035472` |
| `20261008-023640-870-root-slices-current-tiny.result.json` | `A91E32ED6191E310FB989DC4DD3A36D509998AA58984D15C9A281B2370BF3BD8` |
| `20261008-023642-376-root-slices-current-full.result.json` | `2073822220CE33344CE66C4D49B658D72E06547EDD2710B86BA75CFC91FC5727` |

root终核四份当前runner结果（含只读diff）、16个记录身份，无同身份存活，专属synthetic-checks目录为0；`root-current-resource-closure.json` SHA `C07BB38C91E441DA2C4DE8D853A39B29C702F1ACCEAD756236B105C9F09D270D`。当时仍运行的自身reader没有写成已退出。受限环境拒绝WMI后，当前构建/测试的进程跟踪经明确权限请求执行，未绕过限制；读文件/定PID存在性在原权限内完成。

没有真实development报告导入、真人审核身份核验、语言或切片质量、封存/校准、AOT、性能/RSS或正式训练结论；这些门槛继续由S4-05/S4-06/S4-08承接。权重托管目标另按用户要求登记Hugging Face与模力方舟，本工程没有对外上传或发布。
