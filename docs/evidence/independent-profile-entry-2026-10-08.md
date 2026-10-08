# 独立模型画像入口工程验收

Benchmarks 新增 `independent-profile` 和 `check-independent-profile`。前者显式采用独立 decision-v1 runtime/CUDA pipeline，后者只运行 H2 微型 CPU 兼容合同。本轮编译与 CPU 合同通过，尚未运行此入口的真实 CUDA 画像、正式性能矩阵或 Native AOT。

## 范围与身份

| 最终源码 | SHA-256 |
| --- | --- |
| Program.cs（仅两个顶层 dispatch） | `A47AEC11DA3F8E57413FFF2F0E771E9D2DAA78007C722812D73CD919E8DAF49B` |
| IndependentProfile.cs | `EE1423F1E223E4CD18423998615578CFEE9084B3459975C641D5229BADAB9A95` |
| IndependentProfileChecks.cs | `27131D2B4F0C14E54F0A332AA6DE79EC391500A8FEC36D38271FAAF39F6D5493` |

没有修改 legacy 画像、核心、CUDA 或 csproj。输入显式绑定 package/head/expected identity/request 与各自 SHA；小输入保持读 lease，模型身份由既有独立 loader 验证。生产入口拒绝 CPU 参数，无 GPU fallback；CPU 仅用于这次极小兼容检查。

E2E 只计 `runtime.Evaluate` 返回/抛出所用时间，包括内部序列构造及观察日志，不含外部 discovery、响应验证或序列化。pipeline Score 进入/返回、完整 typed request 返回、typed answer 验证分别计数，不能把 Score 调用称为 encoder/kernel 已进入。encoder/head 分项及深层 forward 计数为 null；正式样本0，性能、质量、校准、训练、许可和封存接受均 false。

参数最多40项；request 1MiB、identity16KiB、head128KiB、JSON深度32；samples1–5、warmup0–1、每请求1–32题，最多192个逻辑 Score 调用。工作1–300秒；共享 owner 回收5秒，报告发布5秒及失败 partial 回收独立5秒。批量/正式运行须使用GPU并另冻结真实资产及资源预算。

## 失败记录与发布

成功解析参数即预建全部 not_started 行，题数在 discovery 完整成功前保持 null。资产验证后立即记录已核模型/head身份；GPU打开失败不能丢失这些实值。Evaluate 与 typed 验证同属行级异常处理，验证失败保留真实返回标志、仅 Evaluate 时间及失败/取消错误。

报告采用 GUID partial/CreateNew、flush、无覆盖 move。创建后立即记录归属；写失败时从仍独占的 handle 核实际 prefix。取消或发布预算耗尽后，独立清理预算仍可核创建时间、长度及hash并回收确证 partial；未知对象保留具体路径，原始与清理异常分别保留。同步I/O只能协作取消，关闭核验 lease 后按路径删除仍存在并发替换窗口；不宣称对外部并发篡改的原子身份删除。

## 实际构建与检查

固定 PowerShell7.6.6、.NET SDK10.0.401，Windows 有界 runner 记录进程/启动时间/argv/父链和 finally 清理。离线 no-restore、单节点、关闭共享编译及 build servers；没有安装、下载、教师、训练或 GPU 调用。

| 阶段 | 实际结果 | PID / 回执秒数 |
| --- | --- | --- |
| build1 | 0 warning、2 error；与既有 ProfileSequence 同名 | 66032 / 17.739 |
| build2 | 0 warning、0 error | 1464 / 21.516 |
| tiny1 | 实际 H2 encoder/head 经 Evaluate，3个 typed answer；PASS | 69660 / 2.930 |
| full8 | 八项全部PASS | 25092 / 3.188 |

第二次构建仅把新模块的两处类型名改为 `IndependentProfileSequenceObservation`；反向替换精确还原 build1 的已审源码，原失败保留。没有追加第三次构建。

八项合同覆盖真实 tiny Evaluate/typed顺序/报告语义、head身份拒绝且零Score、预取消与完整计划分母、第二次Score注入失败的部分计数、真实runtime响应对错误期望题号的验证拒绝、非法参数/零GPU前置失败、owner释放失败、输出冲突/实际prefix写失败/发布取消后的partial回收。故障负例没有伪造成功模型响应；fixture不代表业务正确率或训练收益。

root 核最终三源及 build2/tiny/full 的三个实际 DLL 身份不变。`Sezika.Benchmarks.dll` 541,184 bytes/SHA `EB8FF4819E1628B44F4600AA7CD5F4B38A7672BE0CB674BD2B5586119E0994FB`；Sezika.dll SHA `4B280E9B9E379EDEAD0DC36E508632B0ED2BAAF4FB5F03B8305EC268469ABBD0`，Sezika.Cuda.dll SHA `814C41FF7DD63B0B6A1D6F22D384040689C752F58C6D80CC0974DE0412E09773`。所有四份 runner 清理为空，24条执行/launcher/子进程身份记录无匹配存活，tiny/full 两个专属目录均0项；构建输出和证据保留。

## 审查与接续

独立报告 `v2/independent-final-review.md` SHA `C08F65D32FCD2B9187D2859E134B64DA0D1A46C0D2A9B44D750078860E9A38EA`，完整读取初版500行/Checks283行及dispatch22行，再用最终全字节逆变换核仅rename。三个P2已静态修复，未见新的P1/P2。报告写入晚停止新增线29.964秒，但在硬截止内；中间两个短reader身份未捕获，明确unknown，不能称全机进程终核。

原实现窗口的 build 请求等待后被中断，没有启动/退出或通过证据；后续有界1077项CIM检查仅证明当下零匹配候选，不回写历史零启动。本轮两个真实build独立记录。旧null字段freeze、早于窗口的guard拒绝和准备失败均保留，不把旧freeze用于最终验收。

原始证据根 `.artifacts/independent-profile-20261008/v2/`：`source-freeze-build2.json`、`root-build1-logs/20261008-041030-564-profile-build1.*`、`root-build2-logs/20261008-041654-232-profile-build2.*`、`root-tiny-logs/20261008-041812-925-profile-tiny.*`、`root-full-logs/20261008-041843-531-profile-full.*`、`root-final-validation.json`。没有持久fixture、模型下载、服务或独占MCP。

本工程已单独提交 `06de2ed`；后续八份Git回执全部成功、cleanup为空，16条执行/launcher身份记录无匹配存活，见 `root-git-resource-closure.json`。新资产恢复审计确认文档指向的历史 `head-v2.asset` 与 `development-report-v2.json` 当前不存在；仅这两条已核路径，不能推论全仓库无head。历史hash不作为当前文件身份，原审计保留在 `asset-recovery-audit.md`（SHA `14F2EDB6A06DB37C2DB71C068E1F637AA21EC9C062477326079D66636FF8375E`）。

下一真实 GPU 诊断需完整绑定实际 package/head/request/expected identity，另设数量/墙钟/资源预算；本轮不以既有GPU前向结果代替此入口验收。正式性能、上游数值、获准训练数据、真人审核、封存、质量/校准及正式trained-head交付仍开放。S5-06整项保持进行中，Sezika不push、发布或部署。
