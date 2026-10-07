# 通用中英教师 pilot 候选包 v1

本包包含 32 条 AI 起草的通用文本题，仅用于离线教师试运行，全部 `review_status: pending`、`training_admission: pending`。没有正确答案、人工金标、审核批准、教师身份或教师响应。它不是训练集、校准集或封存测试集，`quality_evidence: false`。

| 语言 | Choice 分类 | Score 程度 | Boolean 文本真假 | 合计 |
| --- | ---: | ---: | ---: | ---: |
| 中文 zh | 6 | 5 | 5 | 16 |
| 英文 en | 6 | 5 | 5 | 16 |
| 合计 | 12 | 10 | 10 | 32 |

Choice 包括消息主题、办事意图、通知类别、活动主题、信息需求与偏好分类。Score 每题给出明确有序等级，覆盖紧迫程度、满意表达、参加意向、信息完整程度、噪声描述，以及频率、服务干扰、报告细节、表达预期、礼貌用语。Boolean 只判断短文本明确支持或冲突的陈述，覆盖顺序、数量、例外、全称、否定、时间、规则、职责与位置变化，不需要外部知识。

`candidates.jsonl` 是采集索引，每行对应一题，含 `record_id/language/type/family_id/source/origin/purpose/split/review_status/training_admission/teacher_status/topic` 与请求、prompt 相对路径和 SHA-256。每条 `origin` 为 `AI drafted`、`purpose` 为 `pilot`、`split` 为 `audit`；source 是 `sezika-ai-drafted-general-pilot-20261008`。这些标记表示来源与待审核状态，不代表人工批准。

`requests/` 保存 32 个严格 `decision-v1` JSON，请求只有 `schema_version/model_id/state/questions` 四项，每次一题。`model_id` 是学生身份 `sezika/mmbert-base-independent-s4`，不是教师模型名称。`prompts/` 保存原样 UTF-8 教师提示；Choice 使用定义好的候选 ID，Score 使用 `levels` 的零起始索引字符串，Boolean 使用 `false/true`。拟定教师输出为：

```json
{"question_id":"与请求题目id完全一致","status":"answered或abstained","selected_candidate_id":"候选ID或null"}
```

`answered` 必须返回一个合法 ID；`abstained` 必须返回 JSON `null`。这只是采集协议，示例没有实际回答。采集器须验证实际响应、保留原始字节/hash、失败及拒答分母，随后由人工复核；不得将输出直接当作正确答案，也不得把自报置信度作为概率标签。

`manifest.json` 绑定索引、64 个请求/prompt、起草源和生成脚本的 hash，记录分配、许可审核待定、未调用教师、未含响应与金标等状态。文件 hash 按实际完整 UTF-8 字节计算：请求 JSON 无 BOM、无结尾换行；prompt 使用 LF 并保留一个结尾换行；JSONL 使用 LF。`drafts.jsonl` 是 AI 起草的生成源，不是教师记录；现有 `TeacherTool validate-records` 检查的是调用后、具有真实教师身份的采集记录，不能直接把本索引作为该格式传入。

每个基础题的 `family_id` 唯一。今后的翻译、改写、反事实或实体替换必须继承基础 family，同一个 family 只能处于一个 split。题文已在起草和组装中曝光，本包固定为 audit/pilot，任何后续副本或教师标签都不能充当未曝光的 `sealed_test`。

可在 PowerShell 7 中复核生成源结构，操作不联网、不调用教师、不写文件：

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -File './Build-Pilot.ps1' -MaximumRecords 1 -TimeoutSeconds 10 -ValidateOnly
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -File './Build-Pilot.ps1' -MaximumRecords 32 -TimeoutSeconds 10 -ValidateOnly
```

完整生成只用于新目录，使用 `CreateNew` 拒绝覆盖已有交付物。循环最多 32 条、Choice 32 候选、Score 10 等级，共享最长 60 秒取消/墙钟界限，实际组装使用 10 秒。失败时仅回收本次记录的至多 67 个已创建文件，成功保留交付物。人工题目审核、教师来源与条款审核，以及任何训练准入均待另行完成。
