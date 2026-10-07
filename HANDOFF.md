# Sezika 监督交接

更新：2026-10-08，Asia/Shanghai。规范工作区 `D:\source\Sezika`，分支 `main`，监督起点 `716130e`。ROADMAP.md 是唯一阶段计划；本文件只记录当前交接。此前交接的过程状态保留在 Git 历史及下列证据中。

## 授权与定时接续

用户授权监督者创建并接续子智能体推进全部 ROADMAP，每项独立工程任务审查、验收后单独 commit。同一聊天 heartbeat `sezika` 已核 ACTIVE，每30分钟检查；无变化安静，实质完成、失败或需用户处理时通知。总路线尚未完成，不能关闭监督。

正式训练、批量教师采集和批量特征计算使用 GPU；CPU 仅小规模兼容性、数值参考与诊断。GPU 前向、head 反向、正式数据训练、质量、AOT 和性能分别验收。教师答案不能自动成为人工审核或封存真值。

仅 `D:\source\Tomur` 已获核最新、拉取、必要修改提交及推送授权。Sezika 只 commit；发布、部署及正式默认切换仍按各自门槛处理。用户未跟踪的 Sezika `docs/wechat-introduction.md`、`.html` 及 Tomur `docs/articles/` 不修改或暂存。

## 当前结果

| 独立任务 | 提交 | 已验收范围 |
| --- | --- | --- |
| 教师提示 v2 | `3ac5ead` | 保持32条请求字节、题序、family、候选顺序，只明确所有候选ID为JSON字符串；源67/新69项绑定复验通过 |
| 首轮GPU教师实测 | `087bfab` | 32 HTTP200、13格式通过/19类型失败；133引用核验，失败和全部分母保留；服务/资源已关闭 |
| GPU schema3准入合同 A | `6504d84` | 1→64合同、190核心回归，pending/许可/身份不符输入在前向前拒绝；没有正式训练 |
| GPU workflow/恢复 B | `cbd6790` | 真实UUID、17静态PTX/manifest身份、三文件发布与恢复；CPU3、GPU5→31、stamp正负通过 |
| GPU CLI/特征导出 C | `d2ccb5f` | 显式train-original-gpu、五项恢复输入、完整GPU encoder特征及GPU SGD、schema3评价适配；CPU2→22/GPU8→12/旧CUDA59通过 |
| 第二轮GPU教师实测 | `e26d9e7` | Score/Boolean tiny后full32，32条全部格式有效；34 HTTP、32不同题、无retry，151引用核验通过；标签全部pending |
| 独立数值比较器 | `f469b27` | 25层级tensor+marker的冻结合同，零警告/错误构建，tiny1/full16共19变体；26项完整分母、fixture/自比/源码及路径门禁；没有真实参考数值 |
| 独立上游oracle工具 | `01d23f3` | 原权重/源码lease、首次import前hash及bytecode隔离、25hook+marker、Windows原子发布/实际Move后取消回滚，最终tiny1/full16通过；没有真实forward |
| Windows独立诊断AOT样例 | `b837be7` | 真实固定encoder+未训练内存head，managed/native各1→8、唯一publish/probe/46-token CUDAcase；311launch、552-byte D2H、预取消零launch、释放归零，12份结果/45身份终审通过 |
| Linux独立诊断AOT | `86099ee` | Ubuntu10.0.112/cache唯一发布、native1→8/fstat、probe/唯一46-token CUDA；311launch/552-byte D2H、预取消零launch、资源归零；正式trained-head/训练AOT/质量另验 |
| GPU训练器异常清理 | `2800cbe` | 最终build零警告/错误、CPU1→3/GPUtiny1/full8与既有21项训练smoke通过；独立5秒/40owner/17module、主异常原帧、副本清零/caller基线/false release与context回收分别核验 |
| CUDA原生取得与登记 | `ef1288d` | 五源码、四实际DLL冻结匹配；CPU1→7/GPU1→11、training21/cleanup9case、十一runner/37身份终核，实际context回收；AOT/正式训练另验 |
| 32题人工审核入口 | `c386d29` | 完整题文/候选/实际教师建议、八null人工字段，tiny1/full32及64引用核验、独立映射与root五文件身份通过；全部pending，曝光题不可封存 |

固定真实 mmBERT 的 C 切片 encoder smoke 已通过：PID75592、48 tokens、markers27/37、hidden768、310 kernel launches；runner17.299秒，资源归零。marker SHA-256 `59194DCFF642F4790DBF55F58D38EFBAB200C0528BEE25894DE38D71296B1CCD`。该 smoke 没有训练 head；GPU head 的梯度/更新与恢复工程另有 fixture 实卡证据，没有正式数据训练结论。

B 重新 export 存在 PTX 字节漂移，未替换既有17 PTX/manifest；stamp只核既有来源/ABI/hash并补manifest身份。原失败构建、CIM超时和清理限制保留在各证据中，不能声称重新导出已复现。

## 教师当前状态与资源

v2 固定教师为 `unsloth/Qwen3.5-9B-GGUF@3885219b6810b007914f3a7950a8d1b469d598a5`；catalog `qwen35-9b-q4km`、API model `qwen3.5-9b-q4-k-m`，Q4_K_M 5,680,522,464 bytes，SHA-256 `03b74727a860a56338e042c4420bb3f04b2fec5734175f4cb9fa853daf52b7e8`。完整32题115.6265689秒、16213 prompt/562 completion tokens；不是正确率或正式训练吞吐。

原始结果根 `.artifacts/teacher-gpu-prompt-v2-20261008/simple-run-20261007-2019/`：

- `full-audit.json`：34次HTTP、32不同题、34严格字符串标签、151引用通过；training_admission/quality_evidence均false、human_review=pending。full32 manifest SHA `5151fa98f0496e76c4a9b6cd64f6230792f078e695aae7311e41300b556093c2`；records54,198 bytes/SHA `deb5d8ec8e2c8f1d3f772c42cde1bde9f6141e975d4b20e942b52e1ab09a08e4`。
- `process-and-resource-closure.json`：58份runner清理为空，75身份无匹配存活；monitor49个样本全部WithinLimits，最终Deadline/538.410秒、StopRequested=false。峰值GPU5953/free最小1996MiB；卸载确认loaded=false/busy=false，server主动停止的Failed/exit-1保留；后核used324/free7625MiB。
- `cpu-closure-followup.json`：全部58个原始root PID/start无匹配存活，UTC20:36:47完成。原GPU阶段closure20:34:04在20:34:24截止内；文档写至20:35:15超原界，另批20:35:15–20:40:15纯CPU窗口补核，没有新增GPU/HTTP/build。

**第二轮教师执行结束时，没有遗留教师服务、采集、训练或本任务GPU进程，原教师GPU窗口已释放；新GPU窗口见当前接续任务。** 上批实现子智能体均已完成切片；v2独立只读验收UTC20:43:47通过，最多12文件/5分钟、没有重跑GPU/HTTP/build/hash或进程查询，记录在 `.artifacts/teacher-gpu-prompt-v2-20261008/independent-review-20261008.md`。root负责共享文档及Git。复杂harness因三项生命周期缺口未验收，Run已禁用；Prepare tiny不代表完整生命周期支持，后续不复用Run。

Tomur在UTC20:17已fetch/ff-only，当前HEAD及origin/main均为 `b07ae41450a5832e790eaac1bcdad61531a6a0e0`，ahead/behind=0/0；整数helper、固定llama ABI和9B字节大小修复已提交推送。本轮教师复用host仍为`32675f8`构建，不能称最新源码构建。只剩用户`docs/articles/`未跟踪，无必要修改待推送。

## 当前接续任务

UTC23:22最新状态：原生取得/登记已单独提交`ef1288d`，32题人工审核入口已单独提交`c386d29`。审核包仍全部pending、八个人工字段null、曝光=true/封存资格=false；[审核入口证据](docs/evidence/teacher-review-entry-2026-10-08.md)保留固定源、64引用及五包文件身份。独立审查23:16:02保存早于截止，SHA `38E00DA969C5AFE377CEB4F3C40F8EDB129E3A9842F85CFF5579A417D992B226`；root tiny1/full5原字节核验全部匹配，暂存五Git blob与hash-object一致。没有重新生成、教师或GPU调用。

两任务Git与监督复验七份回执成功、cleanup为空；root UTC23:22:02精确核14身份无匹配存活，见`.artifacts/teacher-review-20261008/root-receipt-closure-7.json`。adoption stage52896/commit95916、review stage32248/83476及commit23032均退出；root最后两审计PID25000/9820亦后核不存在。未创建服务/下载/待回收临时对象，user微信与Tomur文章文件保持。adoption五源码/四DLL已独立freeze/hash复核，CPU1→7/GPU1→11、training21与cleanup9case全部通过，详见[登记证据](docs/evidence/cuda-native-adoption-2026-10-08.md)。不重复旧GPU/全套。

Windows GPU训练Native AOT已验收、待本项单独提交：作者UTC23:39:11结束，最终证据SHA `C68F8F7F857BA0B7D5741E208D46B20DFB391B0256E04E01923187D7A2D48A05`。原23:18:04–23:43:04窗口提前收口；五源码最终freeze23:30:16/SHA `CD3946F0C5C8313DAA9150E0E23BF0D7E22A3D24DA1F555B4F1F78FB8CA6CCBF`，两处末尾预算P2由root补读修复diff、六源hash核。旧managed16/五CPUcase绑定初始freeze，新native16绑定修复版；主3→6恢复/连续6、48launch/480H2D/240D2H、tiny4/80/56、PE和原生flags均通过。root四workflow原字节/1→11case复核及十作者runner/63身份终核通过，最后两审计26920/52220退出，GPU/源码窗口已释放。见[Windows训练AOT证据](docs/evidence/gpu-training-aot-windows-2026-10-08.md)。Linux随后用最终冻结源码新obj/cache/ELF、新窗口≤25分钟、最多16GPU更新，不重复Windows或复用旧ELF。

两个独立只读审查均已停止，按期保存：workflow审查SHA `D2A7F89B61DDE1936FDFB6F17A2605AFC8F290F5ABE5E97A54A761C5B4CFDEED`；三文件fault/project审查SHA `E06D2FD59B4406B74A8028C3980BC89A62DB4890BF0B8A3B390F30BC18D456B5`，后者明确绑定初版、P2当时未修，不冒充新源码审查。root随后补已知diff/真实native及资源证据；没有并发源码编辑。计划acceptance-plan原SHA `95ED067CD47899F0C15337D29AAEB844954C149D921AB68D9E64073ECDC8C509`仅作范围记录，Linux使用WSL而非其中SSH建议。Git/shared docs/root独占。

上游数值oracle仍缺NumPy：实存Python3.14.3/Torch2.12.0+cpu/Transformers5.13.1，NumPy安装问题已异步提出且未回复；不重复问、不把等待当许可。已冻结26项输入/容差和离线导出/比较器工程，未import真实backend或forward。具体task-local安装方案在`.artifacts/independent-oracle-20261008/numpy-install-plan.md`；若收到明确授权再按原official wheel hash、单case180秒预算执行，不擅自安装。此前Python安装管理器意外更新26.3/import探测越界及清理审查晚25秒事实见各永久证据，不能改写为按期完成。

正式逐条人工审核、许可用途审签、独立未见封存、可信上游数值、正式训练收益、语言质量/校准、正式trained-head两RID和新性能/发布仍开放。32题只是pilot/audit；不复用曝光family当封存，不凭教师格式通过准入。旧窗口全过程已保留在Git及证据，不再当作活跃任务；heartbeat `sezika`继续30分钟接续，Sezika不push、不发布/部署。

## 下一次派发

1. 先核AGENTS、ROADMAP、git status、heartbeat与子智能体状态；复用以上已通过证据，不重复下载、32题采集或完整回归。新GPU任务独占窗口，先tiny再有界完整范围。
2. 优先推进S4-15可信固定上游数值oracle：对已有真实46-token/267项capture冻结独立参考及容差，再接原始模型CPU/CUDA比较。参考Python仅限离线oracle，不进入核心；正式批量特征继续GPU。
3. 按依赖推进真实独立模型/自有head的两RID AOT及独立GPU性能画像，明确fixture、真实推理、数值、质量与性能范围。既有Laya证据不能替代独立模型验收，禁止用已查看PAWS/Nimble选参。
4. 正式数据训练需实际许可用途审签及逐条人工复核；校准/封存还需独立未见数据与真实保管声明。用户已选通用中英分类、程度评分、真假判断；32题仍AI/audit/pilot/pending，不能自动转train或金标。600训练+各120开发/校准/测试仅历史建议，未获正式准入。继续不依赖这些外部事实的工程任务；不重复询问含糊的“指定审核人”问题。
5. S4-09/S4-13依据实际开发收益启用，S5-07按数值/质量及瓶颈证据优化；正式发布/部署等具体产物满足门槛后按授权执行。每项实现验收后单独commit并更新交接。

## 固定工具与执行边界

PowerShell `C:\Program Files\PowerShell\7\pwsh.exe` 7.6.6；dotnet `C:\Program Files\dotnet\dotnet.exe` 10.0.401；git `C:\Program Files\Git\cmd\git.exe`。工具发现限PATH、明确配置和固定路径；禁止广域扫描、Graphify及按名称杀进程。本机无原生GCC，不重复扫描；GNU需求仅在已知Ubuntu WSL通过有界command-v核实，PCCT没有确认路径。

UTC21:32–21:33 root用3次有限Ubuntu timeout核发现有AOT工具：`/usr/bin/dotnet`10.0.112、`/usr/bin/gcc`13.3.0、`/usr/bin/clang`18.1.3、`/usr/bin/ld`Binutils2.42。准备阶段没有安装/build/模型/GPU；Windows PID75496/26572/26736均成功/cleanup为空且已退出，Linux timeout/bash PID与start/argv由ps记录，前两组后核不存在。细节`.artifacts/independent-aot-20261008/root-processes/ubuntu-tool-readiness.json`。其后Linux诊断AOT/probe/真实46-token CUDA已在`86099ee`范围通过；新训练代码/ELF仍须独立发布与验证。

长命令经`tools/Invoke-BoundedProcess.ps1`记录PID/start/argv/父链、墙钟、退出码及清理；构建`--disable-build-servers -m:1`，关闭共享编译/节点复用，控制并发。每次委派重复数量/墙钟双边界、取消/进度、极小试跑、进程完整树回收和专属临时文件归属核验。精确PID/start先筛，只有身份匹配再查CIM；禁止反复对全部历史PID广域查询。

保留日志、成功交付物与原始失败；只删除核实绝对路径及本任务归属的临时对象，不清用户文件或共享缓存。B21结果/70身份及C31身份已核零存活；v2另按上节75身份和58root补核。没有创建独占MCP服务。Windows采样可能漏过极短中间进程，保持此限制，不伪造清理或观测范围。

## 关键历史证据索引

- [首批checkpoint/CLI/预测](docs/evidence/supervision-2026-10-07.md)：`acce79f`、`601a865`、`3a7435e`；managed/Windows AOT及CalibrationTool fixture互操作通过。
- [独立pipeline](docs/evidence/independent-pipeline-2026-10-08.md)：`239ef59`；CPU190/CUDA59、Windows AOT CPU18/CUDA17 tiny通过。
- [原始资产恢复](docs/evidence/independent-assets-restore-2026-10-08.md)：`95fe133`；4源文件、134 tensors及真实CPU smoke305.15秒；[267项capture](docs/evidence/independent-numeric-capture-2026-10-08.md)：`252d087`；51,720,192 bytes/hash核验通过，上游oracle仍开放。
- [教师来源/运行准备](docs/evidence/teacher-runtime-readiness-2026-10-08.md)、[GGUF恢复](docs/evidence/teacher-weight-restore-2026-10-08.md)、[采集合同](docs/teacher-collection-s4-10.md)：`7fcb49a`、`da41af6`、`85ccf17`、`3229232`、`9f94be0`；许可工程用途与正式人审区分。
- [GPU head训练工程](docs/evidence/gpu-training-design-2026-10-08.md)：`6d36c70`；CPU中央差分27/GPU21→61，旧14PTX保持；[CLI设计](docs/evidence/gpu-training-cli-design-2026-10-08.md)、[A合同](docs/evidence/gpu-training-contract-2026-10-08.md)、[B workflow](docs/evidence/gpu-training-workflow-2026-10-08.md)、[C CLI](docs/evidence/gpu-training-cli-2026-10-08.md)分别验收。
- [首轮GPU教师](docs/evidence/teacher-gpu-pilot-2026-10-08.md)、[v2提示](docs/evidence/teacher-prompt-v2-2026-10-08.md)、[第二轮GPU教师](docs/evidence/teacher-gpu-prompt-v2-run-2026-10-08.md)：失败、格式通过与训练准入分开记录。

总路线仍缺真实人工数据准入/封存、独立上游数值、正式训练收益、质量/校准、独立真实两RID AOT及新增性能/发布验收。仅当前切片闭环；heartbeat继续。
