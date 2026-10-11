# 仓库训练本机接续证据（2026-10-11）

最新结果：用户已明确授权IoTSharp单一master历史改写，一次精确lease发布 `9c522c4e227c52c86f493a90595a894a41e1935a` 成功。fresh匿名全历史确认旧详细报告提交与validation.json路径不再可达；模型文件差异0，55个owned+1个保留文件、6,174,855,372bytes再次匿名全SHA通过。private模型/数据及optimizer原验收不变，平台旧对象/缓存物理删除仍NOT_VERIFIED。详见[历史清理证据](repository-training-history-cleanup-2026-10-11.md)和同名JSON；本文件JSON及资产根 `final-publication-readback-v3.json` 记录当前范围。

## 历史清理前的本机接续快照（保留）

私有模型与私有数据集已从新clone和独立LFS缓存完整读回，通过全文件bytes/SHA验证；模型optimizer已实际恢复为完整检查点。IoTSharp 当前公开提交 `c68049c735c24da5f1c611c2b229619bb4b391ac` 已正常Git/LFS推送，55个owned文件与1个保留文件、6,174,855,372bytes全部匿名完整SHA验收通过。当前验证报告只含按Split/Type/Rotated分组汇总，128原题加68换序共196前向，换序稳定67、改变1；权重、tokenizer、package、训练报告、checkpoint、optimizer原SHA保持。旧 `dfef170bc9e26dff7e4317fc08686d0abd013438` 详细验证标签仍在公开Git历史，整体历史隐私收尾未完成；历史清理候选已准备但尚未授权/推送。当前接续无需GPU、旧实例连接或新租期。正式质量、校准、新权重AOT/性能仍未验收。

本机根为 `C:\Users\mysti\.codex\artifacts\sezika-repository-local-mirror-20261011-192617`，带 `OWNED_BY_SEZIKA_LOCAL_MIRROR`。源训练run为 `encoder-full-20261011-121827`。此前[实机证据](repository-training-real-2026-10-11.md)与480秒认证读回超时、租期结束PARTIAL均保留历史；本文件记录后续实际验收，不改写这些失败。

| 资产 | 当前证据 |
| --- | --- |
| private模型 `e2e5bc97d62dfbd4e2bbd070542324bb544e6b32` | 正常认证clone、LFS pull/materialize；底座18文件/1,245,737,297bytes，本run33文件/4,929,157,012bytes逐SHA通过 |
| 完整optimizer | 三分块逐SHA与串联全流通过；实际恢复 `recovered-checkpoint`，原2,455,565,752bytes/SHA `2cd5a601d63c658598e5717ce4c05cd26d1197a14212f82f3b47fba4bfb83426` 一致 |
| private数据集 `d19515802d31310b4050226b51ab85c60f130d42` | 正常认证clone、LFS pull/materialize；原275资产/1,076,726,933bytes、新8派生文件/64,322,231bytes逐SHA通过，准确HEAD/index/跟踪集合一致 |
| 原数据pointer诊断 | objects PASS，pointers FAIL且仅两个既有Git blob；原字节SHA和Git blob身份均通过，保留属性诊断 |
| 公开目标 | `IoTSharp/Sezika-Decision-Base v0.1-preview`，当前 `c68049c735c24da5f1c611c2b229619bb4b391ac`；55个owned模型/介绍/清单文件+1原英文README保留，6,174,855,372bytes；正常hook/commit/push、匿名准确ref/HEAD/clean、objects/pointers和全部实际bytes/SHA PASS |
| 可见性与ref | 旧独立visibility回执中，私有模型和数据匿名Git各HTTP401、组织model28824匿名metadata/refs HTTP200、visibility1；新汇总推送回执及本次只读历史提案再次核匿名远端master准确为c68049 |

主要原回执（相对于本机根）：`model-clone-v1.json`、`model-pull-v2.json`、`model-materialize-v1.json`、`source-assets-readback.json`、`dataset-clone-v1.json`、`dataset-pull-v1.json`、`dataset-materialize-v1.json`、`private-dataset-readback-v1/receipt.json`、`visibility-anonymous-v1.json`、`mirror-prepared-v2.json` 和 `mirror-state/owned-mirror.json`。第一次mirror准备因目标既有英文README拒绝，保留 `mirror-prepared.json`；窄修固定975bytes/SHA保留路径后10个微样本通过，再真实准备通过。

工具仅做Git/资产运输和字节校验，不执行模型算子。Windows使用PowerShell7.6.6、Python3.14.3、Git2.56.0.windows.2、GitLFS3.8.0。正常标准basic LFS下载已完成，本轮没有使用draft加速助手。凭据只由限制目标路径的helper读取安全存储，无凭据进入argv/仓库配置/证据或公开资产。

普通组织推送回执为 `mirror-state/transport-prepare-commit-v3.json` 和 `transport-push-v1.json`，推送仅一次，没有force或no-verify。准备v1的极快Git进程采样竞争保留FAIL，运输助手改用真实Windows Popen handle区分活跃与已退出并通过2次实际Git查询；准备v2发现两个原二进制文件被属性强制当文本，保存原属性和清单、仅owned unstage、修正两条 `-diff` 后v3通过。原模型文件字节完全不变，生成文件空白门槛保持严格。

运输脚本冻结SHA为 `b119b1a477cc3f13ca6401bf454f7c1dadf262613eb3245097247c846cbcfbbd`；修前v1原件和全部tiny/真实失败保留。测试进程已确认退出；三个测试gate小文件因工具执行策略以 `blocked by policy` 拒绝精确清理而保留在 `local-mirror-preparation/org-transport-tiny*-gate/go`，未换壳绕过，不进入公开模型。

匿名v1为fresh clone及空缓存，单并发下载按计划取消以改为四并发；v2在同一匿名checkout完成全部LFS对象下载和objects/pointers fsck，clean门槛失败。v3将8个任务文件保留到`anonymous-partial-checkout-before-materialization-v3`，Git restore因取消留下的零字节index.lock拒绝。v5确认旧任务身份退出，原锁归档为`anonymous-stale-index-lock-preserved-v5`，标准Git restore及LFS checkout、准确HEAD/clean和fsck均通过。`mirror-assets-readback.json`全55+1文件及原optimizer串联SHA通过；全过程无认证、未复用private LFS缓存。诊断v4的快速进程登记UNKNOWN保留，随后仅核该原PID已退出，没有按PID或名称杀进程。

`final-publication-readback-v1.json`及本文件同名JSON首先形成的是完整运输与资产身份快照。其后发现的逐题报告隐私问题不得被该PASS掩盖；新的脱敏发布回执与当前隐私状态优先于该历史运输快照。详细报告原件留在private源和任务保留目录，未更改private数据或private模型；完整性校验通过不代表公开内容范围已经通过。

当前汇总发布的原回执是 `mirror-state/transport-push-public-summary-publish-v1.json`，最新精确清单是 `mirror-state/public-summary-owned-v1.json`。发布脚本按全部55+1文件循环核bytes/SHA，全部通过才写当前PASS；其继承Stage的旧默认字段 `full_file_readback_acceptance=false` 未赋值，原回执保留不改，不能把该默认字段解读为全SHA未执行。匿名checkout来自原fresh无认证clone和独立空缓存，新commit的LFS对象不变，未复用private缓存；新树全部字节仍重新验收。

本地历史清理提案为 `mirror-state/transport-prepare-commit-history-cleanup-proposal-v1.json`，状态 `READY_FOR_EXPLICIT_HISTORY_REWRITE_APPROVAL_NOT_PUSHED`。远端expected为 `c68049c735c24da5f1c611c2b229619bb4b391ac`；候选 `9c522c4e227c52c86f493a90595a894a41e1935a` 的tree `d7743fde522c22352aee1048b29f303924f6b5b1` 与当前版本相同，文件差异0，父为安全介绍 `2a410d64a70955364439514cba60207b8b9ee796`，旧详细报告提交不是候选祖先。实际远端写入0、未获得历史改写授权；只对IoTSharp master拟用准确force-with-lease，private仓库和大LFS对象均不改。平台旧对象/缓存是否可访问及物理清理仍需另行确认。

当前完整结果另记为 `final-publication-readback-v2.json`，保留v1原件和全部历史FAIL。文档校验临时PyYAML6.0.3已按安装回执核归属、44文件SHA后精确删除，6个目录回收，共享环境未改；回执 `optional-validation-dependency-cleanup-v2.json`。三个旧tiny gate因执行策略拒绝精确清理而继续保留，无活跃进程，不进入公开模型。
