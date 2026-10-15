# T5 DatasetTool 真实 Parquet 调用者修复与回归

原 corpus prepare 在连接打开后重复设置 DuckDB 的 enable_external_access，实际报错并保留 partial。仅删除这条冗余 SET，保留线程、内存、期限和取消合同；原 v17 FAIL 与全部输出不改。

独立 v18 两平台 JIT 自包含构建及14个实际 CLI 案例通过预期判定：6正例、8精确负例（原始 FAIL/exit1 保留）。真实合成 Parquet 固定两行，prepare/export 各平台均为2行、隔离0；候选文字与概率、语言、标签、来源文件 SHA/row 均独立重读。XZ各5项含托管预取消、截断、解码限额与拼接；不宣称外部 Parquet 进程取消验收。

Root独核245份证据、21个原生/外层结果、两平台4份 canonical 记录；4次资源审计 live0/unknown0。完整400 runtime、1116生成源码及4份NuGet资产元数据已固定身份。SDK隐式网络零访问未获证明，保留 UNKNOWN；实际 SDK 版本为工具身份，项目不锁 SDK。见[逐项证据](dataset-reader.json)。

不授予真实语料许可、模型质量、正式训练、GPU、租用服务器或模型/数据发布验收；最终102能力门及466条调用者检查另验。
