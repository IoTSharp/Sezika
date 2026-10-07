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
| Windows独立诊断AOT样例 | 随本文件单独提交 | 真实固定encoder+未训练内存head，managed/native各1→8、唯一publish/probe/46-token CUDAcase；311launch、552-byte D2H、预取消零launch、释放归零，12份结果/45身份终审通过 |

固定真实 mmBERT 的 C 切片 encoder smoke 已通过：PID75592、48 tokens、markers27/37、hidden768、310 kernel launches；runner17.299秒，资源归零。marker SHA-256 `59194DCFF642F4790DBF55F58D38EFBAB200C0528BEE25894DE38D71296B1CCD`。该 smoke 没有训练 head；GPU head 的梯度/更新与恢复工程另有 fixture 实卡证据，没有正式数据训练结论。

B 重新 export 存在 PTX 字节漂移，未替换既有17 PTX/manifest；stamp只核既有来源/ABI/hash并补manifest身份。原失败构建、CIM超时和清理限制保留在各证据中，不能声称重新导出已复现。

## 教师当前状态与资源

v2 固定教师为 `unsloth/Qwen3.5-9B-GGUF@3885219b6810b007914f3a7950a8d1b469d598a5`；catalog `qwen35-9b-q4km`、API model `qwen3.5-9b-q4-k-m`，Q4_K_M 5,680,522,464 bytes，SHA-256 `03b74727a860a56338e042c4420bb3f04b2fec5734175f4cb9fa853daf52b7e8`。完整32题115.6265689秒、16213 prompt/562 completion tokens；不是正确率或正式训练吞吐。

原始结果根 `.artifacts/teacher-gpu-prompt-v2-20261008/simple-run-20261007-2019/`：

- `full-audit.json`：34次HTTP、32不同题、34严格字符串标签、151引用通过；training_admission/quality_evidence均false、human_review=pending。full32 manifest SHA `5151fa98f0496e76c4a9b6cd64f6230792f078e695aae7311e41300b556093c2`；records54,198 bytes/SHA `deb5d8ec8e2c8f1d3f772c42cde1bde9f6141e975d4b20e942b52e1ab09a08e4`。
- `process-and-resource-closure.json`：58份runner清理为空，75身份无匹配存活；monitor49个样本全部WithinLimits，最终Deadline/538.410秒、StopRequested=false。峰值GPU5953/free最小1996MiB；卸载确认loaded=false/busy=false，server主动停止的Failed/exit-1保留；后核used324/free7625MiB。
- `cpu-closure-followup.json`：全部58个原始root PID/start无匹配存活，UTC20:36:47完成。原GPU阶段closure20:34:04在20:34:24截止内；文档写至20:35:15超原界，另批20:35:15–20:40:15纯CPU窗口补核，没有新增GPU/HTTP/build。

**当前没有教师服务、采集、训练或本任务GPU进程运行，GPU窗口已释放。** 上批实现子智能体均已完成切片；v2独立只读验收UTC20:43:47通过，最多12文件/5分钟、没有重跑GPU/HTTP/build/hash或进程查询，记录在 `.artifacts/teacher-gpu-prompt-v2-20261008/independent-review-20261008.md`。root负责共享文档及Git。复杂harness因三项生命周期缺口未验收，Run已禁用；Prepare tiny不代表完整生命周期支持，后续不复用Run。

Tomur在UTC20:17已fetch/ff-only，当前HEAD及origin/main均为 `b07ae41450a5832e790eaac1bcdad61531a6a0e0`，ahead/behind=0/0；整数helper、固定llama ABI和9B字节大小修复已提交推送。本轮教师复用host仍为`32675f8`构建，不能称最新源码构建。只剩用户`docs/articles/`未跟踪，无必要修改待推送。

## 当前接续任务

UTC20:47 heartbeat接续S4-15单case原始权重上游oracle。先在ROADMAP登记25个层级张量+marker的26项合同，输入/容差在参考测量前冻结；现有267项C#捕获不重跑。root独占合同、solution/shared docs和Git。

- `/root/teacher_prompt_v2` 的独立静态类型C#比较器在UTC21:11:13冻结，早于21:14截止；6工具文件、tiny1/full16共19变体通过，4份runner清理为空、8个登记PID及partial为空，root复验DLL/合同SHA及PID。工程证据见[比较器](docs/evidence/independent-numeric-compare-2026-10-08.md)，提交`f469b27`，Git PID77184/3.218秒/cleanup为空且已退出。Python终审发现源码import窗口并审修复方案；当前另有6文件/5分钟及追加3文件/3分钟AOT只读范围核查，不build/模型/GPU。
- `/root/head_checkpoint` 的开发期离线Python oracle工具在UTC21:14:10原阶段交付，早于21:17:14截止；随后另开最多5分钟源码窗口修复，首次backend import前lease/hash及bytecode隔离已补。最终脚本SHA `118E4CA7BB626F36BEEB009047A7AA42DB9E8A72AD64130F274A818F4766D39E`；tiny PID18296/full16 PID47240成功/cleanup为空且已退出。root核最终源码/SHA/回执，owned fixture清理及pycache未创建；见[工具证据](docs/evidence/independent-oracle-tool-2026-10-08.md)。没有安装/下载/import真实backend/forward，传递环境未全面hash-lock；已冻结停止。旧准备import越界与py manager意外更新26.3保留。
- 实存Python3.14.3、torch2.12.0+cpu、Transformers5.13.1因NumPy缺失不能导入；历史3.12.10/2.8.0/4.57.1环境已不在。合同测量前改用实存版本，初始环境快照保留，输入/26项/容差不变。NumPy2.3.3 cp314官方wheel约13MB已核source metadata，task-local deps安装授权已通过异步问题请求，未回复不能安装或运行依赖它的oracle。

具体方案 `.artifacts/independent-oracle-20261008/numpy-install-plan.md`；readiness/mapping及所有失败原样保留。缺失工具安装需要用户明确授权，不能把等待视为同意。当前没有真实oracle/训练/教师/GPU进程。新阶段CPU限单case诊断与合成自检，不执行正式训练、批量特征或GPU查询；Python工程验收和真实参考运行分别记录。

Oracle提交`01d23f3`的Git PID14344/3.256秒/cleanup为空且root已核退出。接续Windows独立AOT诊断工程已通过，见[证据](docs/evidence/independent-aot-smoke-2026-10-08.md)。历史head-v2.asset/report缺失，scope为真实原始encoder+固定内存诊断head，全部head训练/文件/质量/parity声明false，不造正式metadata。`/root/head_checkpoint`的UTC21:30:49–21:50:49、6文件窗口已在截止前冻结停止；managed/native各1→8、唯一win-x64 publish、driver probe和唯一真实46-token CUDAcase通过。source freeze SHA `8F8D7683DD20408C75D69ADD697B0E20E979231C479D7989ED82A1B589F5BBD9`，exe SHA `ED19B03C00C3BAA9E75966019899FFEAFA1F57E51E0BE384A16050831759BA4C`，真实报告SHA `A529790C5003AF58E68ABFE714CD5C8727868F42A9C26B59E946596BC7E7C680`。311launch/552-byte D2H、预取消零新增launch、释放后资源全部归零；首次CS1061保留。root核6源码freeze及12份runner/45去重进程与launcher身份无匹配存活、cleanup空/专属顶层tmp空，tiny审计PID35604/full30112成功。没有真实CPUforward/训练/Linux；profiling时间不是benchmark。该样例入solution，root独占共享docs和Git，Sezika不push。

`/root/teacher_prompt_v2`此前三段Windows只读审查已停止，最后截断的Budget和ValidateResponse由root补读；没有未解决的Windows实现发现。当前新开最多8明确文件/1轮/5分钟只读Linux准备审查，不build/GPU/install/编辑。WindowsGPU窗口已释放；Linux发布和probe/真实case另开数量/墙钟预算，复用冻结六文件，不与Windows共用obj。下一监督窗口不重跑Windows或32题教师采集。

## 下一次派发

1. 先核AGENTS、ROADMAP、git status、heartbeat与子智能体状态；复用以上已通过证据，不重复下载、32题采集或完整回归。新GPU任务独占窗口，先tiny再有界完整范围。
2. 优先推进S4-15可信固定上游数值oracle：对已有真实46-token/267项capture冻结独立参考及容差，再接原始模型CPU/CUDA比较。参考Python仅限离线oracle，不进入核心；正式批量特征继续GPU。
3. 按依赖推进真实独立模型/自有head的两RID AOT及独立GPU性能画像，明确fixture、真实推理、数值、质量与性能范围。既有Laya证据不能替代独立模型验收，禁止用已查看PAWS/Nimble选参。
4. 正式数据训练需实际许可用途审签及逐条人工复核；校准/封存还需独立未见数据与真实保管声明。用户已选通用中英分类、程度评分、真假判断；32题仍AI/audit/pilot/pending，不能自动转train或金标。600训练+各120开发/校准/测试仅历史建议，未获正式准入。继续不依赖这些外部事实的工程任务；不重复询问含糊的“指定审核人”问题。
5. S4-09/S4-13依据实际开发收益启用，S5-07按数值/质量及瓶颈证据优化；正式发布/部署等具体产物满足门槛后按授权执行。每项实现验收后单独commit并更新交接。

## 固定工具与执行边界

PowerShell `C:\Program Files\PowerShell\7\pwsh.exe` 7.6.6；dotnet `C:\Program Files\dotnet\dotnet.exe` 10.0.401；git `C:\Program Files\Git\cmd\git.exe`。工具发现限PATH、明确配置和固定路径；禁止广域扫描、Graphify及按名称杀进程。本机无原生GCC，不重复扫描；GNU需求仅在已知Ubuntu WSL通过有界command-v核实，PCCT没有确认路径。

UTC21:32–21:33 root用3次有限Ubuntu timeout核发现有AOT工具：`/usr/bin/dotnet`10.0.112、`/usr/bin/gcc`13.3.0、`/usr/bin/clang`18.1.3、`/usr/bin/ld`Binutils2.42。无安装/build/模型/GPU；Windows PID75496/26572/26736均成功/cleanup为空且已退出，Linux timeout/bash PID与start/argv由ps记录，前两组后核不存在。细节`.artifacts/independent-aot-20261008/root-processes/ubuntu-tool-readiness.json`；native CUDA Driver在Ubuntu尚未探测，不声称Linux AOT支持已通过。

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
