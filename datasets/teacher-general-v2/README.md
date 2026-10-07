# 通用中英教师 pilot 提示包 v2

本包沿用 v1 的 32 题，只修订教师提示中的 JSON 候选 ID 编码说明。32 个 request 文件与 v1 逐字节相同，题文、`record_id`、`family_id`、题序、候选顺序及中英分配保持一致：Choice 各 6 题、Score 各 5 题、Boolean 各 5 题。它是已曝光的 `AI drafted`、`audit/pilot` 候选包，人工审核、许可审核和训练准入均为 `pending`。

所有 `answered` 输出的 `selected_candidate_id` 必须为 JSON 字符串：Choice 使用原 ID，Score 使用零起始等级索引字符串（如 `"3"`），Boolean 使用 `"false"` 或 `"true"`。提示明确禁止将 ID 写成 JSON number 或 boolean。`abstained` 仍使用 JSON `null`。输出恰好包含 `question_id/status/selected_candidate_id` 三个字段。格式模板使用占位符，并明确说明占位符不是答案、候选、标签或金标，不能照抄。

[manifest.json](manifest.json) 绑定 64 个 request/prompt 的路径、SHA-256 与字节数，以及候选索引、[prompt_contract.json](prompt_contract.json) 和固定的 v1 来源快照。[candidates.jsonl](candidates.jsonl) 逐条记录新 prompt 的 hash/bytes、旧 prompt 的 hash/bytes、v1 manifest hash 与提示合同 ID/hash。`provenance/` 保存原 v1 manifest 和索引的精确字节，属于历史来源，不代表 v2 采用旧提示合同。

本阶段只准备文件，没有调用教师、运行 GPU、解析真实教师答案或训练；`teacher_called:false`、`teacher_responses_present:false`、`gold_labels_present:false`、`quality_evidence:false`。教师输出的格式有效性、题目质量、答案正确性和人工准入须分别验收，结构与 hash 通过不代表标签正确。

生成器使用固定 v1 hash，`CreateNew` 拒绝覆盖交付物，最多 32 题、100 个自建文件、120 秒内部墙钟上限，并支持取消文件。循环条件已核对为比较条件，先完成 1 题新目录试运行，再生成 32 题。[file-verification.json](file-verification.json) 记录生成阶段检查；[独立复验](independent-verification.json) 另核源字段、67 项 v1 绑定、69 项 v2 绑定及 v2 manifest、32 个唯一 family、原请求精确字节和候选映射顺序。

可在 PowerShell 7 中生成新的复验副本；默认输出指向本目录，因此本目录已有交付物时应指定新的任务目录：

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -File './Build-PromptV2.ps1' -MaximumRecords 1 -TimeoutSeconds 20 -OutputDirectory 'D:\source\Sezika\.artifacts\teacher-prompt-v2-new-trial' -CancelFile 'D:\source\Sezika\.artifacts\teacher-prompt-v2-new-trial.cancel'
```

运行时还须使用外层有界进程工具记录进程身份和清理结果。成功交付物及日志保留；失败回滚只处理本轮 `CreateNew` 记录的精确路径与 hash/bytes 匹配文件，不回写 v1、不删除用户文件或共享缓存。
