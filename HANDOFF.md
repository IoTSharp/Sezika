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

UTC23:15接续已独立验收 native adoption 五源码，待本项单独提交：三build最终零警告/错误，CPUtiny1/full7、GPUtiny1/full11、training21与cleanup9case通过，最终context资源归零。作者于23:06:18结束，正式证据SHA `8C16358EE6DF01A7D1712947DAF125402CD16992F6408F984F91012FFC24CA35`；root十一回执/37身份终核无匹配存活，四DLL及五源freeze匹配。详见[登记证据](docs/evidence/cuda-native-adoption-2026-10-08.md)。原adoption窗口已释放，不再重跑GPU/全套。

32题审核包作者已在23:08:03完成生成、23:09:03资源核，五包文件保留，八个人工字段全部null/pending。`/root/head_checkpoint`独立只读审查窗口23:10:29–23:16:29正收口，child对README/review/template的tiny/full紧凑断言通过；root待最终证据后单独提交五包文件和.gitattributes，不混入adoption。下一实现为训练Native AOT Windows切片，复用已读acceptance-plan，Linux随后另开独立窗口；Git/shared docs仍root独占，Sezika不push，heartbeat继续。

当前新阶段：native adoption `/root/head_checkpoint` 窗口UTC22:50:30–23:15:30，23:12:30停止新增验证；其子审/测试agent只拥有Program和AdoptionChecks，主agent拥有CudaDevice/Handles/ResourceAdoption，总5文件，root已初审3核心并指出release exception缓存与primitive-last不匹配风险，作者build前已置缓存null修正，仍待最终freeze/实卡。GPU仅此任务。`/root/teacher_prompt_v2`的6文件只读AOT设计已UTC22:55:43提前结束，计划`.artifacts/independent-training-aot-20261008/acceptance-plan.md` SHA `95ED067CD47899F0C15337D29AAEB844954C149D921AB68D9E64073ECDC8C509`，没有AOTbuild/GPU；root已完整读计划。拟新3测试文件变更、H4三题/3→6磁盘恢复与连续6，每环境tiny1/full11最多16实际GPU更新，cleanup6/adoption3、native flags早于Open；Windows后Linux新二进制分别验收，不复用旧ELF，Linux用既有WSL边界而非SSH。

人工审核入口独立工程 `/root/teacher_prompt_v2` UTC22:58:25–23:10:25，23:08:25停止新增生成：最多5新文件在`datasets/teacher-general-v2-review/`、唯一artifact `.artifacts/teacher-review-20261008/verification.json`；固定已验收result-all-32 records54198bytes/hash `deb5d8ec8e2c8f1d3f772c42cde1bde9f6141e975d4b20e942b52e1ab09a08e4`与manifest `5151fa98f0496e76c4a9b6cd64f6230792f078e695aae7311e41300b556093c2`。只离线生成真实题文/所有候选/教师答案和空人审字段，≤32记录/64request-response引用、先1再32、不覆盖用户填写文件，不调用教师/GPU/训练/安装；output_training_permitted=false及所有人审/准入pending保持。root另负责该目录.gitattributes、共享文档与单独Git提交，不能混入adoption尚未验收代码。

清理工程已提交`2800cbe`，stage100132/commit77492（最终JSON3.333秒/cleanup空）均退出。作者UTC22:49:41最终报告`.artifacts/trainer-cleanup-20261008/execution-evidence.json` SHA `B93547A866A46E09742F95E24256746530C6EB28D51A7BBF992DD2660CB99AE8`，复用root11/41终核；8个compiler argv确切rsp/3明确MSBuildTemp目录全不存在，无TEMP枚举或删除。GPU与原清理源窗口已释放。`/root/head_checkpoint`已接收到起25分钟/最多5tracked文件的独立native adoption工程（CudaDevice/Handles、新ResourceAdoption、tests Program/新AdoptionChecks），仅该任务使用GPU；最多3build/CPU8/新GPU12及既有trainingtiny+cleanup9共10回归case。不publish/正式训练/教师/encoderforward/PTX重导出。目标涵盖buffer/module及Open/context/profiling event的同类登记空隙，完成后新代码freeze/审核/commit，再单独训练AOT。Shared docs/Git/root仍独占；旧DLL两RID验收不自动继承。

UTC22:36本轮接续：Linux独立诊断AOT已单独提交`86099ee`，原Git PID83504/3.832秒/Succeeded/cleanup空，stage PID82112与commit PID已核退出。训练器清理七源码已UTC22:40:07冻结，最终第三build PID93232/12.137秒/零警告错误，CPUtiny1/full3、GPUtiny1/full8及唯一既有21项smoke全部通过。原数量为最多3build、CPU≤10/GPU清理≤10；累计CPU8/GPU10，未重跑整套/教师/正式训练/AOT。GPU已UTC22:43:01释放；root UTC22:43:32精确核11结果/41身份全部无匹配存活，7源码freeze与实际DLL hash一致，见[清理工程证据](docs/evidence/trainer-cleanup-2026-10-08.md)。root自己的tiny/full审计PID77476/64988亦已退出。

`/root/head_checkpoint`完成清理静态审查并发现原始栈断言缺口，作者已修入最终build/实卡；该审查保存返回晚原期限25秒，原事实保留，不能称按期完成。现另开UTC22:43:00–22:48:00/4明确文件/1artifact只读设计窗口，针对native成功后wrapper/登记OOM缺口，不build/GPU/改源。`/root/teacher_prompt_v2`清理实现已停止新增负载与改源，只在原22:52:50截止前做最终资源/证据收口。下一独立任务先分配归属加固，再冻结新代码做GPU训练Native AOT（H4/三题型、3→6恢复与连续6、CE/gradient/SGD、runtime flags/binary/UUID/PTX/hash及真实资源）；Open/context/event同类路径另核，不能把managed counters完备性当既已保证。Git、shared docs仍root独占，Sezika不push，heartbeat继续。

UTC20:47 heartbeat接续S4-15单case原始权重上游oracle。先在ROADMAP登记25个层级张量+marker的26项合同，输入/容差在参考测量前冻结；现有267项C#捕获不重跑。root独占合同、solution/shared docs和Git。

- `/root/teacher_prompt_v2` 的独立静态类型C#比较器在UTC21:11:13冻结，早于21:14截止；6工具文件、tiny1/full16共19变体通过，4份runner清理为空、8个登记PID及partial为空，root复验DLL/合同SHA及PID。工程证据见[比较器](docs/evidence/independent-numeric-compare-2026-10-08.md)，提交`f469b27`，Git PID77184/3.218秒/cleanup为空且已退出。Python终审发现源码import窗口并审修复方案；当前另有6文件/5分钟及追加3文件/3分钟AOT只读范围核查，不build/模型/GPU。
- `/root/head_checkpoint` 的开发期离线Python oracle工具在UTC21:14:10原阶段交付，早于21:17:14截止；随后另开最多5分钟源码窗口修复，首次backend import前lease/hash及bytecode隔离已补。最终脚本SHA `118E4CA7BB626F36BEEB009047A7AA42DB9E8A72AD64130F274A818F4766D39E`；tiny PID18296/full16 PID47240成功/cleanup为空且已退出。root核最终源码/SHA/回执，owned fixture清理及pycache未创建；见[工具证据](docs/evidence/independent-oracle-tool-2026-10-08.md)。没有安装/下载/import真实backend/forward，传递环境未全面hash-lock；已冻结停止。旧准备import越界与py manager意外更新26.3保留。
- 实存Python3.14.3、torch2.12.0+cpu、Transformers5.13.1因NumPy缺失不能导入；历史3.12.10/2.8.0/4.57.1环境已不在。合同测量前改用实存版本，初始环境快照保留，输入/26项/容差不变。NumPy2.3.3 cp314官方wheel约13MB已核source metadata，task-local deps安装授权已通过异步问题请求，未回复不能安装或运行依赖它的oracle。

具体方案 `.artifacts/independent-oracle-20261008/numpy-install-plan.md`；readiness/mapping及所有失败原样保留。缺失工具安装需要用户明确授权，不能把等待视为同意。当前没有真实oracle/训练/教师/GPU进程。新阶段CPU限单case诊断与合成自检，不执行正式训练、批量特征或GPU查询；Python工程验收和真实参考运行分别记录。

Oracle提交`01d23f3`的Git PID14344/3.256秒/cleanup为空且root已核退出。接续Windows独立AOT诊断工程已通过，见[证据](docs/evidence/independent-aot-smoke-2026-10-08.md)。历史head-v2.asset/report缺失，scope为真实原始encoder+固定内存诊断head，全部head训练/文件/质量/parity声明false，不造正式metadata。`/root/head_checkpoint`的UTC21:30:49–21:50:49、6文件窗口已在截止前冻结停止；managed/native各1→8、唯一win-x64 publish、driver probe和唯一真实46-token CUDAcase通过。source freeze SHA `8F8D7683DD20408C75D69ADD697B0E20E979231C479D7989ED82A1B589F5BBD9`，exe SHA `ED19B03C00C3BAA9E75966019899FFEAFA1F57E51E0BE384A16050831759BA4C`，真实报告SHA `A529790C5003AF58E68ABFE714CD5C8727868F42A9C26B59E946596BC7E7C680`。311launch/552-byte D2H、预取消零新增launch、释放后资源全部归零；首次CS1061保留。root核6源码freeze及12份runner/45去重进程与launcher身份无匹配存活、cleanup空/专属顶层tmp空，tiny审计PID35604/full30112成功。没有真实CPUforward/训练/Linux；profiling时间不是benchmark。该样例入solution，root独占共享docs和Git，Sezika不push。

`/root/teacher_prompt_v2`此前三段Windows只读审查已停止，最后截断的Budget和ValidateResponse由root补读；没有未解决的Windows实现发现。最多8明确文件/1轮/5分钟Linux准备只读审查也已完成，没有先改源码的已确认障碍；明确Windows runner不覆盖Linux子树，GNU timeout不能单独证明成功后资源回收。当前另开最多6明确文件/1轮/4分钟的GPU训练AOT只读准备，不build/GPU/install/编辑。WindowsGPU窗口已释放；Linux发布和probe/真实case另开数量/墙钟预算，复用冻结六文件，不与Windows共用obj。下一监督窗口不重跑Windows或32题教师采集。

Windows提交`b837be7`的Git PID75216、4.394秒、cleanup空；stage PID94956和commit PID已精确核不存在。root从45身份明确argv提取5个MSBuildTemp绝对目录，UTC22:00有限存在性核均已不在，未删除或扫描全TEMP；证据`root-processes/compiler-temp-observation.json`。两root终审PID35604/30112已退出，无需要回收的任务服务。

Linux边界准备由`/root/head_checkpoint`在UTC21:59:21–22:07:21单独执行，专属`.artifacts/independent-aot-20261008/linux-smoke-v1/`、最多3artifact脚本/说明、8明确输入文件、最多3极小试跑/每次15秒；未授权该阶段build/GPU/install。已用一次有界command-v核`/usr/bin/timeout`及`/usr/bin/setsid`。先审Linux PID/starttick/UTC/argv/PPID/父链及归属token、有限轮询/超时/trap/成功后清理证据，再另派最多一次publish及唯一真实GPUcase，当前不以准备结果宣称Linux已支持。

该准备先tiny正常/超时/取消通过，随后补supervisor身份/unknown depth。root新tiny发现合法退出期间env不可读被误标cleanup_incomplete：timeout/cancel的survivors虽0，状态仍不合格，旧失败保留。另开UTC22:09:35–22:15:35/两artifact修改/最多三tiny修复窗口，token或identity refresh失败后再核同PID/starttick的live/absent-reused-zombie/unknown；unknown不误当退出。fix-v2三tiny均cleanup_incomplete0/survivors0，root已读完整关键分支及回执。两脚本冻结SHA：bash `AC979A61F98FBA71F5D7181FA904B008642FF60A269E947E64844C9FBBD3F255`，PS `E030FBA561FF3C700EB33666E2EAAB89BB0F58B55895838CE3950FBEADFD1ED0`。协作型payload必须保留token、不daemonize；Bash/proc不是pidfd/cgroup隔离，已保留竞态和快照限制。Linux发布先用确为空的任务local restore source，不能把空RestoreSources属性说成已禁远端。

root另以12明确结果/15秒精确核Windows原root PID/start全部无匹配存活，记录`root-processes/windows-root-pid-followup.json`。`/root/teacher_prompt_v2`的GPU训练AOT准备及清理方案只读任务已停止；计划`trainer-cleanup-plan.md`8217 bytes/SHA `C7A4C5DB345CAD3A4B038C1DD2D2B19A5CF0564D4A0FA555AC2B56E690B5C808`，真实release false/SafeHandle/context语义由root补核。S4-07/S4-12已登记修复及单独训练AOT范围；尚未修改core/CUDA，不与Linux冻结依赖并发编辑，不把计划称实现或训练支持。后续先审最小源码变更及实际故障验证，再单独commit。

Linux执行窗口UTC22:16:08–22:32:08的唯一publish已成功：源基线b837be7，ELF5,084,552 bytes/SHA `F355C79A2B98402F670617E46BA00DE4BF8226BB69F090BE4658326C0FC828F4`；compiler无warning、local source/cache三个restore各334ms。native1/8、probe/真实46-token CUDA已PASS，实际Linux92698/Windows54268，311launch/552-byte D2H、预取消零launch、释放全部归零；报告SHA `6D36644B29FE67AFE20BCD8C677C20006C67B8B4880E49DE54AE910FF220A7A8`。root核真实报告/ELF hash、原publish/full8输出，固定输入/head匹配Windows；Linux第一轮26个登记身份后核全absent，后续终核证据由作者收口。冻结执行两源码已按相同bytes/hash封存到`docs/evidence/independent-aot-linux-2026-10-08/`，代码与[Linux证据](docs/evidence/independent-aot-linux-2026-10-08.md)随本任务提交。不是正式head/训练/质量/parity/benchmark证据，GPU已释放。

Linux编译完成后只运行固定ELF，允许训练器源码独占修改，不自动继承旧ELF验收。`/root/teacher_prompt_v2`现获收到起30分钟/最多8tracked文件+1artifact清理加固窗口，owner为Cuda trainer/Kernels/Device、新内部cleanup/friend辅助和Cuda.Diagnostics.Tests的Program/新cleanup checks；shared docs/solution/Git仍root独占。最多3有界build、CPU合同tiny/full≤10、新GPU故障tiny/full≤10及1现有成功smoke；先tiny，GPU窗口已单独转交此任务，不重跑61/31/59整套，不publish/真实CPUforward/教师/正式数据。fault接缝内部、单次、生产默认关闭，原false release/caller context与历史错误必须如实区分；任务完成审源码、实际故障/资源证据后单独commit。当前实现中，不能把方案写成已修复或训练AOT支持。

Linux作者已于约UTC22:28停止：21Windows记录/18PID及26LinuxPID/starttick全退出；第二核root26864/launcher50408也不存在。root UTC22:31另核六明确执行回执/22PID-start记录（20PID）无匹配存活，记录`root-processes/linux-execution-windows-closure.json`。作者最终证据SHA `D4265A494D105CCD1F52EA49BA811E2D1D66024513BBCAA59E0B67D4FCC28B56`，来源/binary绑定SHA `4DD5C2A0D3DDD7D9248086D90533A1AF5541E142C40137B2A6F801AE3B5A5A55`；两封存执行源码与artifact原始hash相同，root审查后单独提交Linux任务，不夹正在实现的Cuda源码/测试或用户微信文件。仅清理加固子智能体仍运行，GPU窗口归其独占。

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
