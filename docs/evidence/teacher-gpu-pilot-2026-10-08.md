# 本地 GPU 教师首轮实测

固定教师为 `unsloth/Qwen3.5-9B-GGUF@3885219b6810b007914f3a7950a8d1b469d598a5`，Q4_K_M，实际5,680,522,464字节，SHA-256 `03b74727a860a56338e042c4420bb3f04b2fec5734175f4cb9fa853daf52b7e8`。Tomur catalog ID为`qwen35-9b-q4km`，HTTP model为`qwen3.5-9b-q4-k-m`；外部离线教师使用native CUDA，Sezika核心推理与训练仍遵守纯C#/静态Driver边界。条款只准本轮内部audit采集，人工许可审核、输出训练及再分发准入未获批准。

RTX4070 Laptop、8188MiB、driver596.08；CUDA13.2.51、MSVC19.51/VS18。固定llama revision为`1bc7a5af0d14b1fb72f266abbd1237b394187115`。初次native build PID66196达到1800秒上限，原cleanup三项错误保留；902个记录身份后核无匹配存活。唯一同缓存接续PID86640在509.594秒成功，900秒上限、cleanup空。8个DLL经过复制/hash核验与native prepare，任务bundle为`sezika.teacher.tomur.cuda132.20261008`。配置明确CUDA、gpu_layers=999、context2048；不能由配置本身推断实际GPU推理。

首题真实HTTP调用PID100448成功，runner90.85秒；HTTP200、严格三字段JSON、308输入/31输出tokens、选`delivery`。服务日志实际记录33/33层offload、CUDA0模型buffer4861.28MiB，CPU_Mapped token embedding545.62MiB。首响应448字节，SHA-256 `28A3460A22F070991CFAACAFB0842DB620B80547E5CF5A5D72B06A5EEF279693`；固定stderr快照SHA-256 `45998A2E6DC639406B06F5F6C783DF29C39D7C41DDEC50AC353A9A50FB96D288`。证据索引为`.artifacts/supervision-20261008/teacher-runtime/gpu-first-success-evidence.json`。

随后对v1候选包按原序串行调用32题、32次无重试。collector PID71484，内部480秒/外层520秒，实际128.9864921秒工作、132.078秒runner；exit3表示保留了失败题，不能报告全成功。32次HTTP200、finish_reason均stop，分母没有丢失。

| 题型 | 总数 | pending/格式通过 | failed |
| --- | ---: | ---: | ---: |
| Choice | 12 | 12 | 0 |
| Score | 10 | 0 | 10 |
| Boolean | 10 | 1 | 9 |
| 合计 | 32 | 13 | 19 |

Score的10个ID为JSON Number，Boolean的9个失败ID为JSON Boolean；严格合同要求JSON String，因此均为`invalid_teacher_response`。Boolean仅最后一题字符串ID通过。19条题目ID、三字段形状及候选字面值经审计匹配，只说明类型错误足以解释失败，不把诊断字符串转换作为修复或正确答案。32个请求均为独立单user message、content与存档prompt完全相同，没有session/history字段；不能据此证明服务内部绝无状态污染。

完整结果在`.artifacts/supervision-20261008/teacher-gpu-pilot32-result`。manifest SHA-256 `dc8b49504c2cbaaa0500090b8d8b84826cfa72444e6238a153963a2ff712a8cb`；records54,598字节，SHA-256 `f2733f6f4ebc5a6e2cdb0771cfe9140f2ce4d207bd07966ee9538c125e1bb078`。独立审计tiny PID101956/full70028通过，共133项引用逐hash/字节核验，无不匹配，审计JSON/MD及verification在`teacher-runtime/pilot32-audit*`。全部候选保持AI起草/audit/pilot，pending不等于人工金标，所有记录training_admission=false；没有训练、语言正确率或质量验收结论。

资源监控94个样本均WithinLimits，观测最高GPU5951MiB、最低free1998MiB、host约5.3GiB。第95轮CIM系统查询超时，monitor状态为MonitorFailed/exit2，内部935.496秒；没有把查询失败改为资源超限或正常完成。原summary和stop-request保留。POST session/unload PID47712成功，响应loaded=false/busy=false；之后核对server PID96756及launcher30560的创建时间、完整命令和父链，主动停止本任务server，外层runner负责后代回收。服务runner为Failed/exit-1/1117.207秒，这是主动关闭而非自然成功。关闭记录为`teacher-runtime/gpu-pilot-manual-shutdown.json`。后核nvidia-smi PID77244成功，GPU324MiB/free7625MiB。

监督者只读资源闭环审计先修正PowerShell数组拼接错误并保留原tiny失败PID34500；修正tiny39972/full9916均成功。full核六个明确日志目录、147份原结果、1337次身份观测，PID/创建时间/命令/父链没有匹配存活对象，审计未终止任何进程；结果为`teacher-runtime/gpu-closure-v1.json`。所有历史失败和cleanup错误仍保留，瞬时快照及Windows极短进程捕获限制不被忽略。新监控V2仅将CIM timeout2→5秒，真实单样本自检PID20224成功，但不能宣称已完成第二轮监控。

下一轮使用[独立v2提示](teacher-prompt-v2-2026-10-08.md)，先Score/Boolean各1题，再一次32题有界GPU采集。原v1输出与失败不覆盖，parser不放宽；v2的实际响应、资源和关闭另行验收。
