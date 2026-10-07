# 通用文本人工审核包

这 32 条中英文本题覆盖分类、程度评分、真假判断；它们来自 AI 草拟题和真实教师回答，全部仍待人工审核。本包只提供审核入口，没有训练导入或自动准入功能。教师答案已在包内曝光（teacher_answer_exposed=true），即使界面折叠也不能认定盲评或独立金标；eligible_as_sealed_test=false。

1. 打开 review.md，逐题阅读完整原文、说明和候选，先写自己的候选 ID，再展开教师建议核对。若已看到建议，请如实备注，不能声称做过独立盲审。
2. 复制 review-template.jsonl 后填写每题 human：真实 reviewer、reviewer_id、reviewed_at_utc、independent_label、decision、correction 和 notes。不得把教师答案直接当作人工答案。Boolean ID 只能是字符串 "false" / "true"；Score ID 是从零开始的字符串，例如 "0"，不能把第一个等级写成 "1"。不明确或题目有问题时如实记录，不强行选择。
3. 保留 record_id、family_id、source 和 teacher_suggestion 原样；先在副本记录审核，避免破坏本包 manifest 绑定的原始待审核产物。完成审核不等于训练获准，training_admission 和 quality_evidence 仍为 false。

教师模型标称 Apache-2.0，但模型许可不自动代表输出训练许可；当前来源证据 output_training_permitted=false、independently_verified=false。正式训练仍需要真实逐条复核和许可条件确认，不能由脚本填写批准。已在本包曝光的题及其 family 不能再充当封存测试；封存测试应另建、独立保管且不提供给教师或训练流程。

manifest.json 绑定原始 records/manifest、全部 request/response 原字节身份、生成器及本包输出 SHA。源文件不随包复制，其路径以 source.directory 为根。原始采集结果保持只读。

在 Windows PowerShell 7 中运行（目标目录必须不存在；先创建一个明确的空父目录）：

```powershell
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -File .\Build-Review.ps1 -OutputDirectory D:\source\Sezika\.artifacts\my-review-parent\probe -MaximumRecords 1 -TimeoutSeconds 120
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -File .\Build-Review.ps1 -OutputDirectory D:\source\Sezika\.artifacts\my-review-parent\complete -MaximumRecords 32 -TimeoutSeconds 120
```

运行前确认 $PSVersionTable.PSVersion 为 7 或更高。生成器固定核对已验收源身份，最多 32 题/64 个引用、120 秒，支持 CancellationFile 和 CancellationToken；Ctrl+C 也可终止。存在的目标一律拒绝覆盖。输出使用 UTF-8 无 BOM、LF；失败仅清理自己创建的唯一临时目录。没有网络请求、教师/GPU 调用或标签猜测。
