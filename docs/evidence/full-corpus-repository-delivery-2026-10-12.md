# 全量语料私有仓库交付与福海维护

本轮按用户授权，将已转换并准入的完整语料发布到现有 private 数据集仓库，随后以固定提交拉取；另执行福海同版本闭源驱动/GSP关闭规避测试。租用实例已暂停，原北京时间2026-10-12 14:30期限已经过去，不再连接该实例。正式全量GPU训练仍为 **NOT_RUN**。

## 数据身份与当前发布

原始来源提交为 `d19515802d31310b4050226b51ab85c60f130d42`，原407语料文件、353 gzip分片保持全部大小与SHA。17准入来源，train **14,592,720** / development **1,619,168**；这是含属性展开的规范记录数，不是独立题目数，也不等于仓库所有未适配来源都已进入训练。development/test/calibration/OOD不用于更新。

完整派生入口位于 `derived/full-corpus-20261012-dataset-v1/`。407原文件加3入口metadata形成410文件的提交 `368385973bcbd5d2c1ef6b719911d349b8b2337c`。独立拉取脚本审阅发现训练器还必须读取单独的 `CorpusLengthReceipt`，因此在不修改410文件的前提下追加3个冻结旁车：

- `training-inputs/lengths.json`：1,688bytes，SHA `b79247245feb6644f179dd248fcf321d4719e010d3004fcc73c575b0258a4a73`。
- `training-inputs/training-inputs.json`：1,361bytes，SHA `bcf746e859502c5c0a50551ea155bf5eadc3624eaee29cdae70ddd2eb9c7ef0c`。
- `training-inputs/corpus-run-plan.pending.json`：928bytes，SHA `0fcb82b2b253c1d5f6dda02db2dc5847c08a30e868cea137189a6ea42d5c71ea`；实例路径/UUID保持null，不能直接作为执行计划。

最终数据提交 **`5e180c4136f8dd72cb5f37cb8673bddcbe9bc7f5`** 已正常Git/LFS推送，并独立核对master。总413文件、**2,623,103,071bytes**；原始树除本次派生目录和精确`.gitattributes`追加外没有改变。所有运输保持private，未改变可见性、改写历史或发布模型。

354个本次LFS对象、2,616,236,586bytes分20个有界单并发批次上传；原第3批DNS失败先做只读download-batch reconcile，核1已登记/9缺失后仅恢复缺失对象。标准Git push的pre-push还核到4个历史LFS对象，因此最终hook输出358对象；不能把该数字当本次新文件分母。旧失败及启动观察UNKNOWN不改写。

发布前独立进程逐413文件、大小/SHA及目录集合验证通过。**独立远端fresh认证下载/空LFS缓存的完整读回进行中，尚未置发布完整验收PASS。** 已核fresh准确HEAD、新派生前缀稀疏checkout、无共享LFS存储及旧树不变。第一次LFS pull已fetch但因本地未安装filter跳过checkout；随后在该fresh库安装标准local LFS filter并从已fetch缓存materialize，没有重传。第2批运输通过后的汇总字段名错误保留为FAIL，后续从第3批继续。

## 服务器拉取与训练工具

新增 `tools/repository-training/remote/pull-full-corpus.sh`，必须运行于任务Linux guardian下。准确最终发布提交、skip-smudge、稀疏派生入口、独立LFS cache、标准basic单并发拉取；完整413普通文件集合/大小/SHA通过后才写读取绑定。拒绝只含410文件的旧368提交；不执行数据仓库代码或下载旧raw。凭据仅由调用者受保护helper/内存提供，不进入URL/argv/日志。

可信413文件TSV位于本轮独立artifact，63,028bytes，SHA `1bc7753c856b283d269dc7972f182c5135862543afa2bb1f2f0740b127647f40`；其逐项对应完整owned JSON，SHA `8227c2442ab6dfbca3f64998af021414edc0a7b8066fd4b69caa6c20fcce7d78`。小TSV作为工具输入交付，语料仍从仓库读取。13项离线fixture、语法及身份/取消检查通过；服务器实际pull **NOT_RUN**。`CorpusRunPlan.DatasetCommit`保持原来源d195，`PublishedDatasetCommit`另存绑定，不能把发布身份覆盖进原来源。

发布器新增`CommitOnly`、每32路径一批与最多128 Git调用，解决Windows argv上限；原CRLF和来源notice空白按精确路径保真，其余生成文件仍严格检查。已有6项CommitOnly/7项来源空白fixture及410文件preview通过。

新增C# RTX4090候选门槛：精确`NVIDIA GeForce RTX 4090`、CC8.9、至少30GiB、CUDA Driver>=12080和固定UUID；普通24GiB卡不准入。保持旧5090路径。4090须重新在当轮实卡从固定底座/零head执行8192形状前反向profile，保存`gpu-admission.json`绑定profile SHA、设备/driver/架构/显存、空闲显存与owned上限；profile和资源收回未通过不能训练。Release零警告/错误、68项调度检查（22项新门槛）通过，实机前向/反向/稳定性均 **NOT_RUN**。新执行不能复用旧d925工具包，需重新冻结源码与工程清单。

## 福海已授权规避测试

固定580.178.04只将3个open模块包替换为同版本closed包，用户态版本不变；写入`NVreg_EnableGpuFirmware=0`并更新initramfs。已批准的两个GPU容器精确stop/pause：ASR暂停成功；翻译因旧GPU故障无法正常退出，原FAIL保留。没有未知PID kill、卸载当前内核模块、GPU reset或强制重启。

UTC12:56:30发出本规避测试唯一正常重启，约11分钟后SSH恢复；新boot `0bc4e4f7-0ed0-4324-8f9c-3d3259aed4a4`、内核6.8.0-146、closed580.178.04、`EnableGpuFirmware:0`核对通过。**新启动仍出现Xid62/45/158，GPU稳定性FAIL；规避没有修复GPU。** 本任务单次SMI被新启动故障门槛拒绝，实际NVML调用0；业务服务自行创建的D态`nvidia-smi`归属已核，未结束该外部进程。

4个原业务unit active/running；两个原GPU容器自动running/unpaused。ASR健康接口200、翻译健康接口503；实际识别和翻译推理未验，不能写为业务全部恢复。旧阶段超时/未知、安装/验证条件错误及各原失败分别保留，独立实际版本/配置验证与GPU故障结论分开记录。没有追加重启、回退或安装其他驱动。

## 继续门槛

完成独立远端完整字节读回后，数据发布才可完整验收。福海需要另行停机冷启动/供电PCIe检查或健康GPU交叉验证；未获得新增重启范围前不扩大维护。健康GPU重新核实例、驱动、容量与当轮profile后，才能按同一全量cursor进行训练。

当前训练器一记录一更新，整轮固定14,592,720次。仅举计算下限情景：即使0.1秒/条，也约16.89天；实际时间必须由新实卡profile确认，还需检查点和独立验证预算。不能承诺几小时完成、静默缩分母、用48步称全量或以全部数据训练推导准确率提升。

本机证据分别保存在 `C:\Users\mysti\.codex\artifacts\sezika-repository-local-mirror-full-dataset-20261012-1035`、`D:\source\Sezika\.artifacts\repository-training-full-corpus-complete-20261012`、`D:\source\Sezika\.artifacts\pinned-dataset-pull-20261012-2044`、`D:\source\Sezika\.artifacts\repository-training-4090-admission-20261012` 及本轮维护artifact。原失败/UNKNOWN与独立收口回执保留；未修改用户无关dirty或记忆文件。
