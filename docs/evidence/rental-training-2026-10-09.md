# 租用 RTX5090 固定实验与本地模型交付

2026-10-09，Asia/Shanghai。一次固定训练、独立新进程验证、原32题回归和完整本地模型下载已完成。本机独立证据核验10/10 PASS；这是实验工程与数值证据验收，正式质量、许可发布、校准、封存、新head AOT及性能仍pending。

用户确认实例创建时间北京时间2026-10-09 20:22，四小时租期截止2026-10-10 00:22。本轮目标提前到2026-10-09 23:22；完整模型于 **21:17:08.676** 下载并核SHA，提前7491.32368秒，原期限未被放宽。全部远端阶段及结果下载于21:18:14.813完成，本机独立核验于21:22:31.490完成。

模型目录为 `D:\source\Sezika\.artifacts\rental-training-20261009-01a12099\trained-model`。包含1,227,772,549-byte冻结encoder、tokenizer、8192 manifest、新head、checkpoint、训练report和SHA清单。新head/report/checkpoint由租用机下载；冻结encoder/tokenizer由上传前已核的本机完整原件补齐，最终全部字节再次核验。权重外部管理，未嵌入程序集、NuGet或提交到Git。

## 输入与执行身份

新机Ubuntu24.04.1 x64/glibc2.39、RTX5090、驱动610.43.02；CUDA报告driver_version=13030。GPU UUID为`GPU-c582dc42-1fed-0727-5a1e-dc57f5e314a4`，boot_id为`0e74f433-d83d-44bb-8063-56c2e7bbea6f`。远端独立根`/root/sezika-experiments/sezika-rental-20261009-01a12099`，新成功pipeline为`bd304689`。GPU计算仍由C# PTX/CUDA Driver路径执行，未安装或调用第三方推理、训练算子桥接。

475文件完整迁移包1,437,381,120 bytes，SHA256为`1ff848a6fdaec54a10747672ed500f1f07d5426701806bf9206153ef23dc3434`；本机、远端整包、逐文件与动态依赖核验通过。来源、模型卡、模型/tokenizer/数据/教师许可声明随原冻结资产迁移，正式发布审签保持pending。旧数据215=train162/dev53、Choice140/Boolean75，全256派生账本、原文/来源/split/family、修题/教师证据及原修正32题保持原字节。

| 身份 | SHA256 |
| --- | --- |
| 数据manifest | `E8E74CE0BBF1A72DA5FCDF4DCAD9D302513DC5DC42CB548BFD21D2569621AEB6` |
| 全256派生账本 | `55F3226946831C4B9CBD7F6EE10D1194764860C8343A9386CAA40C678CE24EC0` |
| 8192模型manifest | `B8FB9CECC0912F034E0FEA184E8AA78871D11533093DCCF4F5AAD74646007551` |
| 冻结encoder | `6168c84d2498cdacdcafee16b71a69b5c1636ad569bfabaf2855f5ce7dabd528` |
| tokenizer | `197d4cc5406ee12cc50c8b5511f2393cc32d9db321545979ce041c1199178356` |
| 新训练report | `efafe1e006e3dd7a3d8bec3a2327651406749fa0bf348c987783ec658ba51e4d` |
| 新head | `4205457556736AB07799B462D42C377710E765CB14EDB9AAE508C1558C5E9D64` |
| 新checkpoint | `4709E847A135265F71C092BB18C66CF291DE87D90970CD8932ADD6FBC7F4264F` |
| 独立validation | `1bfac6befbf5bb52cbcdd5107a031ae24438e1e9ddde0c9f0d37524e95c80784` |
| 远端结果tar | `07c97e181aa87ac9d8d8693a4ba62652452385a820ca8f2dcbcd69d1560b910d` |

新head/checkpoint自然与旧机确定性实验同SHA；实际新训练由新root、GPU UUID、Linux PID/starttick/argv/父链、CUDA报告、输入及报告hash共同证明，未以旧head/report代替新机执行。

## 实验结果与资源

| 阶段 | 实际内容 | CLI报告时间 |
| --- | --- | --- |
| 准入 | 原包与overlay SHA、数据合同、GPU短测 | 全部通过 |
| 唯一训练 | 24576updates、LR0.001、seed17、strict8192、零head、encoder冻结；231 forwards | 35.2160222秒 |
| 独立验证 | 新进程从磁盘加载新head，再GPU编码215原题+16换序；231 forwards，fresh_disk_outputs_matched=true | 28.9658498秒 |
| 原32回归 | 修正审计绑定、32/32实际forward | 8.6122972秒 |

固定训练顺序实际151完整周期+114前缀，每题访问151或152次，合计24576次；并非128完整epoch。训练教师一致118/162，开发集27/53，16题换序7/16、selected candidate ID改变6/16。训练CE从1.1844641080609075降到0.8185527480586812，开发CE从1.10233565996278降到1.0354540944099426。教师一致性不是正式准确率；换序变化显示候选顺序敏感性仍存在。

原32回归的修正审计一致性为Boolean8/10、Choice4/12、Score0/10；Score在本轮新增监督为0，不把其均匀输出称评分能力。原32属于已曝光回归，不能作为封存测试。

GPU并发1，owned上限3GiB、host上限4GiB，free至少5120MiB；训练owned峰1,325,572,580 bytes、host峰2,781,282,304 bytes。正式三阶段均device/model dispose返回，owned allocation、module及release failure归零，Linux runner最终live0/unknown0/unchecked0。21:18:00结束快照free32149MiB且compute列表为空。本机新pipeline PID83704守护Succeeded/exit0、live0/unknown0。

`Verify-Experiment.ps1`重新流式核完整模型与报告SHA、二进制head/checkpoint、逐题softmax/NLL/argmax/CE/分组、215题唯一ID原文绑定、训练访问顺序、独立进程证据和期限。10/10 PASS，0FAIL、0NOT_RUN；正式核验PID11912/exit0，内10.847秒/52188有界操作，守护Succeeded并完整收口。它不执行GPU或修改模型。

## 保留的失败与修复

初次pipeline `42feeb2e` 在合同阶段runner初始化exit64，无payload/GPU执行。原具体失败行未知；同一原runner后续`/usr/bin/true`成功，整数SECONDS的一秒初始化短窗仅为可疑因素。原冻结包/runner/失败目录保留，v2 overlay仅将btime读取改为单调3000ms、仍最多1024行，不改变训练或清理合同。新runner SHA为`b6dd14cfc9eab1d66d75293b2e5380340315902945a204a8aaa2450ed67bc1d8`，stage入口SHA为`282d8b8f169b902354537adc463d3185d487cfb8133bb48b9a850aeb5a9a4479`，overlay独立清单验证后再准入。

PowerShell scp参数拼接曾把两个路径合成一个参数，下载入口已修为两个明确数组元素。本机旧解包使用TrimStart去掉`.artifacts`的点，旧目录保留；相同SHA tar重新解包到`results-07c97e181aa8-verified-layout`，2文件tiny布局探针通过。正式验收读取新目录，未改原tar。独立reader曾按源行序与报告train→dev顺序比较，215行中82处错序；改为唯一record_id逐题核，固定训练顺序仍独立精确重放，1题及215题reader探针通过。所有修复仅涉及调度/读档，未重训、改模型或放宽数据合同。

旧下载监护原FinalCheckComplete=false/unknown1保留；追加精确核验21登记身份，20已退出、1 PID已复用为无关MCP，其子树未被终止。当前检查范围内旧owned live0/unknown0，不补造未观测短命中间进程的历史证明。

## 保存与停止边界

永久镜像[rental-training-20261009-final](rental-training-20261009-final/inventory.json)保存95份远端原字节及逐文件SHA、正式验收与本机摘要；原结果97文件中的2项外部模型asset只记录SHA，原件保留完整模型与ignored任务根。Linux目录镜像映射为`linux/`，原路径在inventory逐项记录。含连接标识的本机原SSH argv回执仅保留ignored任务根，未提交。

训练、下载和回归已停止；不自动加训、扩量、按dev调参、LoRA、encoder反传或教师调用。其他ROADMAP暂停，旧故障服务器不继续恢复或训练。本批只本地commit。正式质量、人审、许可发布、校准、封存、新head AOT和性能均为pending/NOT_RUN，未发布或部署。
