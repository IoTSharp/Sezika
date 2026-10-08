# 32题独立AI盲审（2026-10-08）

使用当前Codex sonnet.vip凭据，实际5次成功请求完成32题：gpt-6.1-sol / max。最初ultra请求被HTTP400拒绝，用户明确批准改用max；未修改全局配置。保留标签29题，建议改标3题，不确定0题，建议改题0题。每题原标签在请求中隐藏，返回结果重新通过严格本地合同校验。

本结果是AI审题侧车，human_review=pending、formal=false、sealed_test=false。原32题、24train/8已曝光development划分、旧教师来源与历史GPU报告保持原字节；没有改标训练、重跑GPU或把已曝光题称为封存测试。返回型号为网关声明，并非独立核实上游身份。

| 题ID | 类型/划分 | 原ID → 审核ID | 判定 | 理由 |
| --- | --- | --- | --- | --- |
| general-zh-choice-01 | choice/development | delivery → delivery | 保留 | 消息说明包裹已到服务点，并询问领取地点和营业时间，主要主题明确为“配送与领取”。 |
| general-zh-choice-02 | choice/development | renew → renew | 保留 | 发信人明确请求将归还日期延长一周，主要办事意图是申请延长借阅期限，对应索引2。 |
| general-zh-choice-03 | choice/train | food → food | 保留 | 通知涉及食堂素食窗口和午餐供应，属于餐饮服务，对应索引1。 |
| general-zh-choice-04 | choice/train | gardening → gardening | 保留 | 浇水和给幼苗遮阳都是植物养护活动，对应索引3。 |
| general-zh-choice-05 | choice/train | timetable → timetable | 保留 | 发信人查询周六7路车的首班发车时间，所需信息是公交发车时刻，对应索引0。 |
| general-zh-choice-06 | choice/train | resend-ticket → resend-ticket | 保留 | 文本明确请求再次发送入场凭证，主要求助事项是补发入场凭证，对应索引2。 |
| general-en-choice-01 | choice/development | reschedule → reschedule | 保留 | 发信人请求将客户通话从周二上午改到周三下午，主要行动是重新安排会议时间，对应索引0。 |
| general-en-choice-02 | choice/development | audio-guide → audio-guide | 保留 | 访客明确询问在哪里借用语音导览设备，所问服务对应语音导览租借，索引为1。 |
| general-en-choice-03 | choice/train | replacement → replacement | 保留 | 题目要求选择请求的解决方式；发信人明确要求换货而非退款，对应更换损坏商品，索引为2。 |
| general-en-choice-04 | choice/train | thanks → thanks | 保留 | 正文感谢对方就分析师职位进行交流并付出时间，主要目的是表达面试后的感谢，对应选项thanks。 |
| general-en-choice-05 | choice/train | flat → flat | 保留 | 骑行者明确优先选择爬升少的路线，可以接受更长距离，也不追求快速到达，因此对应flat。 |
| general-en-choice-06 | choice/train | no-peanut → no-peanut | 保留 | 请求明确要求去掉花生和花生油，并允许保留其他配料，主要约束是去除花生成分。 |
| general-zh-score-01 | score/development | 3 → 3 | 保留 | 现在为当天16点，要求18点开场前修好，距截止时间约2小时，符合等级3，无需推测维修耗时。 |
| general-zh-score-02 | score/train | 3 → 3 | 保留 | 发信人明确表达整体满意，同时提出包装更易打开的小建议，符合等级3；存在改进建议，不符合等级4。 |
| general-zh-score-03 | score/train | 4 → 4 | 保留 | 说话者明确确认会参加，符合等级4。该判断针对文本表达的意向确定程度，不预测实际出席情况。 |
| general-zh-score-04 | score/train | 4 → 4 | 保留 | 对象为志愿者，日期为10月12日，具体时刻为14:00，地点为社区二楼活动室。四项均明确给出，对应等级4。 |
| general-zh-score-05 | score/train | 1 → 1 | 保留 | 文本说明声音可以听见，但阅读无需额外集中注意力，正常对话也能听清，符合等级1。 |
| general-en-score-01 | score/development | 2 → 2 | 保留 | 练习日为周一、周三、周六，共3个不同日期，且明确没有其他练习日，唯一对应2级。 |
| general-en-score-02 | score/train | 1 → 1 | 保留 | 只有打印服务不可用，员工仍可继续日常工作；这一有限中断符合1级描述。 |
| general-en-score-03 | score/train | 2 → 3 | 建议改标 | 文本明确提供应用版本、复现步骤和观察到的结果，未提供具体错误文本，共具备四项中的三项，对应3级。截图不计入这四项。 |
| general-en-score-04 | score/train | 2 → 2 | 保留 | 说话者承认下雨的可能性，并明确表示不偏向下雨或不下雨，唯一对应2级。这是对表达的序数分类，无须推算实际降雨概率。 |
| general-en-score-05 | score/train | 4 → 4 | 保留 | 请求包含请托用语和感谢，并通过“when you have a moment”明确给予收件人时间上的灵活性，符合4级标准。 |
| general-zh-boolean-01 | boolean/development | true → true | 保留 | 记录明确先将包裹放进储物柜，最后发送取件码，陈述符合该顺序，应取true。 |
| general-zh-boolean-02 | boolean/train | true → false | 建议改标 | 剩余数量为12－5＝7个，且之后没有变化。陈述中的8个与明确数量不符，应取false。 |
| general-zh-boolean-03 | boolean/train | false → false | 保留 | 本周周三明确全天暂停营业，按题目标准该例外优先于平时安排，因此照常营业的陈述不成立，应取false。 |
| general-zh-boolean-04 | boolean/train | false → false | 保留 | 小组成员为甲、乙、丙，乙请假未到场，因此“所有成员都参加了”与明确到场情况不符，应选false。 |
| general-zh-boolean-05 | boolean/train | true → true | 保留 | 公告明确表示今晚不会安排第二次停机，支持“今晚不会再安排停机”的陈述，应选true。 |
| general-en-boolean-01 | boolean/development | true → false | 建议改标 | 同一天、同一时钟下，09:15早于10:00，因此绘画工作坊开始得更晚的陈述为假，应选false。 |
| general-en-boolean-02 | boolean/train | true → true | 保留 | 库存变化完整且无其他变动，最终数量为18−4＋6＝20，陈述与计算结果一致，应选true。 |
| general-en-boolean-03 | boolean/train | false → false | 保留 | 入场要求通行证有效，而Sam的通行证昨天已过期，因此今天不满足所述要求，应选false。 |
| general-en-boolean-04 | boolean/train | false → false | 保留 | 检查拼写的任务分配给Leo，Noor唯一被分配的任务是页面排版，因此陈述与任务分配不符，应选false。 |
| general-en-boolean-05 | boolean/train | true → true | 保留 | 钥匙被移到绿色盒子后无人再次移动，最终位置为绿色盒子，陈述与原文一致，应选true。 |

## 既有权重与新审核标签的事后比较

- train：原教师一致17/24，AI审核标签一致18/24。
- development：原教师一致1/8，AI审核标签一致2/8。

以上复用已保存GPU逐题预测，仅改变比较对象，没有训练或新增前向；不称人工金标正确率，旧CE及报告未改写。

## 拟合与质量的下一步

先把建议修正作为新数据版本及独立来源合同，保留原始32题。后续若启动新实验，固定24训练题与8开发题，先检查训练损失、梯度及更新是否有效，再验证训练24题能否全部拟合；Choice换序需文本与ID一起移动，并比较所选ID，避免学习位置。若线性head达到优化边界仍不能拟合，再单独评估可训练中间层/非线性head；encoder反传或LoRA需要另行实现和验收。每次只做有界且预先固定的实验，开发题不选步数。

32题可作为固定样本回归验收；拟合24/24、开发表现、换序稳定和正式泛化质量分别报告。审题完成或训练集满分本身不会使正式质量门槛通过。当前请求止于审题与分析，没有自动开始新训练。

使用量（网关报告）：input=28990、output=8121、total=37111；cached input=16896、reasoning output=2494。原始请求/响应、来源与hash见[audit-report.json](label-audit-20261008-final/audit-report.json)。ultra失败及CLI未发请求失败保留在本地任务目录。

首次报告生成因Md与PowerShell内置别名冲突失败，原失败回执保留；函数已更名为Format-AuditMarkdown。旧的部分归档目录以及本任务意外创建的两个空目录general-zh-choice-01、delivery保留：自动审批拒绝了归档/清理命令，仅返回blocked by policy，未绕过执行。后续成功归档使用独立final目录。

最终逐题独立审查通过：32题及49条引文核验、3改标理由成立、5份原响应hash一致，见[独立审查](label-audit-20261008-final/report-review.md)。审查总墙钟约257秒，超过所派4分钟约17秒，已停止且没有追加API或GPU；90秒局部数据核验实测340ms，这不抵销总预算超限。进程终核114登记记录/53唯一PID无匹配或未知存活，另独立核checker及其launcher、质量建议reader已absent；这仅是登记范围，reader局限见回执。保留目录意味着临时对象清理未完整通过，不能据进程关闭宣称全部资源已清理。
