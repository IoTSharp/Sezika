# Sezika 监督交接

更新：2026-10-08，Asia/Shanghai。工作区：`D:\source\Sezika`；分支：`main`；监督起点：`716130e`。

## 授权与接续

用户授权监督者创建子智能体推进 ROADMAP 所有待完成任务，每个独立任务验收后单独提交，每30分钟检查并接续已完成子任务。同一聊天 heartbeat `sezika` 已启用，周期30分钟，未完成总路线前保持启用。ROADMAP是唯一计划；本文件记录交接。没有push、对外发布或部署授权。

## 本批任务闭环

- S4-07/S4-12 checkpoint/恢复/导出工程任务已提交 `acce79f`：2→4与连续4步fixture权重及文件字节一致；v2完整identity适配缺陷已修复，真实训练/质量仍待验。
- S4-15/S6-02 显式独立CLI工程任务已提交 `601a865`：managed41项、Windows Native AOT tiny fixture42项通过；正式默认切换仍依赖S4-08。
- S4-08/S4-15 独立预测导出工程任务已验收并随本交接单独提交（见Git最新记录）：极小1项/完整11项、真实CalibrationTool联调通过，4条fixture保持exit3/not_eligible；原子发布漏洞与nullable编译错误已修复。

上述首批三个子任务均完成。接续 S4-15 静态独立 CPU/CUDA pipeline 工程子任务也已验收并随本次提交：核心190项、CUDA59项、CLI41项、预测11项，Windows AOT 独立 CPU18项/CUDA实卡17项通过。真实模型数值、Linux AOT、质量、性能及产品 CUDA 命令接线另验；证据见 `docs/evidence/independent-pipeline-2026-10-08.md`。主智能体独占共享文档、测试接线与 Git。

## 下一次接续

当前接续状态：

- TeacherTool API model key修复已验收并单独提交：candidate Id/API Model分别记录，旧alias前置拒绝；build13988零警告/错误、synthetic1→10与CLI正反通过。新DLL SHA `CCF65714E4D3E3EE853FBE806ECE3D2E44F12E4C18EC6BC442428D285B7D3557`。32题之前需使用新GPU input快照，不覆盖旧首题失败。
- Tomur `32675f8` ABI修复已提交并推送，远端/本地main一致；`1aa3fa7`整数辅助修复一并推送。Windows x64 6结构80行oracle通过，完整host构建PID104664成功（1既有warning）。ABI独占临时删除被自动审核拒绝，准确保留路径见 `.artifacts/llama-abi-20261008-head-checkpoint/cleanup-result.json`，无存活任务进程。
- RTX4070 Laptop/8GiB、CUDA13.2.51、VS18匹配；新CUDA配置PID101656通过，文本target build PID66196/session98415正在最多1800秒、parallel2预算内。只用新build配套DLL；GPU layers用999（-1会归零），2048ctx；fit与真实推理尚未验收。监督heartbeat已保存用户GPU要求与仅Tomur的push授权。

- 独立数值capture工程任务已验收并单独提交：OS resident采样过频触发tiny3秒deadline后修复，1→8项自检通过；真实46tokens/267张量、51,720,192字节payload全部hash复核通过。原失败与partial保留，Move后取消/持续合成peak恢复已验。上游oracle继续开放。正式训练/批量任务必须GPU，CPU仅小核验。
- 教师首题真实调用失败：collector PID24100 exit3/http_transport_error，Tomur PID8488 native崩溃0xC0000409，全分母及原始日志保留，未有答案。固定llama.h与C#结构ABI漂移已修复，6结构/80大小偏移oracle通过；Tomur固定ABI完整主程序重建PID104664成功（1个既有CS8600）。CUDA后端正在准备，32题未运行。
- 用户授权Tomur核最新/拉取/提交推送；2026-10-08已fetch origin，远端main仍5cd90dd，本地1aa3fa7包含最新远端。ABI修复验证后单独提交并推送Tomur；Sezika仍未获push/发布授权。

- 原始资产恢复已验收并随本次单独提交：固定4源文件与134 tensor转换身份通过，真实CPU smoke为46 tokens/35,328个有限hidden，执行305.15秒≤400秒；实际peak未测。MIT全文事实纠正及失败日志保留，最终无登记存活PID/partial/cleanup错误。见 `docs/evidence/independent-assets-restore-2026-10-08.md`。
- `/root/independent_cli`：单GGUF恢复已完成并随本次提交，实际5,680,522,464字节/hash `03b74727a860a56338e042c4420bb3f04b2fec5734175f4cb9fa853daf52b7e8`匹配后原子发布到 `.artifacts/teacher-tomur-data/models/text/qwen3.5-9b-q4-k-m/Qwen3.5-9B-Q4_K_M.gguf`；含准备668.64秒，最终5份runner无登记存活PID/partial/cleanup错误。直连Cancelled误标保留并纠正、小状态回归通过，不重下大文件；当前已释放该子任务资源。
- 教师采集器已提交 `85ccf17`；接续context_size=2048成功/取消请求及manifest预算已验收并随本次提交，重建PID87004零警告/错误、1→10检查PID51236/97132通过，资源回收通过；新DLL SHA为 `0C0FC3F80DFE60F0DB4472AD4B714556E4A37A6C6E4E990661FE6180E84CECEB`。尚无实际教师HTTP或答案，见 `docs/teacher-collection-s4-10.md`。
- `/root/closure_audit`：capture工具初次实现及root构建PID51732零警告/错误通过；head只读审查发现post-Move取消、恢复内存peak闸门及输入重新打开快照缺口，接续≤10分钟修复，未验收提交。1题teacher输入已在 `.artifacts/supervision-20261008/teacher-pilot-inputs` 生成并核11份snapshot，manifest16CE1C...、catalogFA8F0A...；全部AI工程核查/audit/pending，无教师调用。capture固定46tokens/267个CPU张量，不替代上游oracle。
- 主线程：4文本库target PID57664通过，CPU-only任务bundle准备PID50408及已有模型登记PID51732通过/无下载。Tomur两处CS0177已最小修复、重建PID64428成功并单独提交Tomur `1aa3fa7`；其2项警告保留。首次serve PID104120因禁webasset导致缺embedded manifest失败，进程已回收；npm ci PID50048/v24.15.0与真实webbuild PID51500通过，正在180秒预算重建含资源host，输出 `.artifacts/supervision-20261008/teacher-runtime/host`。未有真实教师答案，下一步1→32题pilot，源/权重/产物与所有失败分别登记。

用户已确认首批用途为通用中英分类、程度评分、真假判断，并希望用教师模型作答。32题是第一轮格式试运行；600训练+各120开发/校准/测试仅监督者建议的起点，未采集/训练或获得正式数据准入。用户对人工审核/独立保管问题表示不理解，已用具体含义解释，不能将该回复视为批准或封存证据。

1. 核对固定原始encoder/tokenizer资产路径与登记身份，按已审核revision/hash/许可使用范围恢复缺失资产，并准备独立可信上游数值oracle；必要下载在既有任务授权范围内，不因缺权重永久停工。
2. 按互不冲突文件推进独立线性marker head CUDA前向及独立画像入口、版本化人工审核切片标签工具；先tiny/工具证据，真实模型与性能另验。已查看PAWS/Nimble继续仅audit。
3. 等待用户指定实际人工审核人、独立封存保管人和首批业务任务范围；不得伪造人工准入或公开候选的未见封存资格。可选S4-09/S4-13只有必要性证据才启用。
4. 逐工程任务审查、有界验证、同步证据与共享文档后单独commit；真实数据/质量/校准/CPU-CUDA/两RID AOT/性能与发布分别验收。正式对外动作等待具体产物的明确授权。

## 验证与资源

PowerShell固定 `C:\Program Files\PowerShell\7\pwsh.exe` 7.6.6；dotnet固定 `C:\Program Files\dotnet\dotnet.exe` 10.0.401。长命令经 `tools/Invoke-BoundedProcess.ps1`，保存PID、创建时间、argv/完整命令行、父链、墙钟、退出码及清理；构建 `--disable-build-servers -m:1`，关闭共享编译/节点复用，串行运行。

最终首批核心173项、CLI managed41项和Windows AOT42项；新增预测最终solution PID100308零警告/错误，pilot PID65840、full PID43276及消费者PID36308通过。初次预测编译失败和主线程外层gate字段读取错误均已修正并保留证据。见 `docs/evidence/supervision-2026-10-07.md` 与其执行索引。

提交前19份runner记录/118次进程身份观测，无匹配存活身份或清理错误。最后commit仍用runner独立记录；未创建独占MCP服务。测试自建GUID临时模型通过finally回收；日志、Windows原生产物和fixture输出保留于本任务 `.artifacts/supervision-20261007`。没有广域工具发现、Graphify、按名字杀进程或清空共享缓存。Windows采样可能漏过极短中间进程，此限制保留。

## 保留与未验收

既有未跟踪 `docs/wechat-introduction.html`、`docs/wechat-introduction.md` 属于其他工作，未修改/暂存。固定原始资产、转换与真实CPU encoder smoke已恢复；尚未实际教师采集、读取真实封存题、采样新性能或Linux AOT。pipeline验证15份runner/69次身份观测无匹配存活进程和cleanup错误；资产与教师执行独立跟踪。工具/fixture闭环不关闭整项路线门槛；总路线尚未完成，heartbeat继续接续可执行任务并只在有实质变化/失败/需要用户操作时通知。
