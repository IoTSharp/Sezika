# 通用文本教师建议：逐题人工审核

状态：全部 pending；教师建议不等于人工答案。先自行阅读原文并写下候选 ID，再展开教师建议。没有任何题已获得训练准入；本包已曝光，不能充当封存测试。

## 01. general-zh-choice-01（zh / choice）

Family：`general-zh-parcel-routing-01`；问题 ID：`topic`；状态：pending。

原文 state.text：
~~~~text
我的包裹已到附近的服务点，请告诉我领取地点和营业时间。
~~~~

任务说明 instruction：
~~~~text
选择这段消息的主要主题，只依据文本。
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`billing`
~~~~text
账单与付款
~~~~

候选 ID：`delivery`
~~~~text
配送与领取
~~~~

候选 ID：`account`
~~~~text
账号与登录
~~~~

候选 ID：`event`
~~~~text
活动与报名
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`delivery`；零起始索引：1
~~~~text
配送与领取
~~~~

</details>

来源 request：`items/00.decision.json`，SHA-256 `e070c0eae9b9509cba8ecbadd84382826fce9c5130c8912f0792db5683457fb2`，bytes=466
来源 response：`items/00.http-response.bin`，SHA-256 `503a9db40b4f0979be909a940741900864a5d167d0f14e4f1eb93be2099f954d`，bytes=431

## 02. general-zh-choice-02（zh / choice）

Family：`general-zh-library-renewal-02`；问题 ID：`intent`；状态：pending。

原文 state.text：
~~~~text
这本书我还没看完，原定明天归还，能把归还日期延长一周吗？
~~~~

任务说明 instruction：
~~~~text
选择消息中明确提出的主要办事意图。
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`return`
~~~~text
立即归还图书
~~~~

候选 ID：`buy`
~~~~text
购买图书
~~~~

候选 ID：`renew`
~~~~text
申请延长借阅期限
~~~~

候选 ID：`report`
~~~~text
报告图书遗失
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`renew`；零起始索引：2
~~~~text
申请延长借阅期限
~~~~

</details>

来源 request：`items/01.decision.json`，SHA-256 `20e3aa603f32b1305a8f740df1a8b72d772c2df31af6dca0267c457fe6d79c54`，bytes=472
来源 response：`items/01.http-response.bin`，SHA-256 `adbc9c4fd60a5138f6a68e19f03af7f48c60694863ae1ac2629a3be0990a3011`，bytes=429

## 03. general-zh-choice-03（zh / choice）

Family：`general-zh-cafeteria-menu-03`；问题 ID：`category`；状态：pending。

原文 state.text：
~~~~text
食堂从下周起增设素食窗口，午餐供应时间保持不变。
~~~~

任务说明 instruction：
~~~~text
选择这则通知最合适的类别。
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`transport`
~~~~text
交通服务
~~~~

候选 ID：`food`
~~~~text
餐饮服务
~~~~

候选 ID：`network`
~~~~text
网络维护
~~~~

候选 ID：`sport`
~~~~text
体育赛事
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`food`；零起始索引：1
~~~~text
餐饮服务
~~~~

</details>

来源 request：`items/02.decision.json`，SHA-256 `edb4c34eb88e46fb8e5610d958814def72e40346dc358f860c7c78520275201a`，bytes=431
来源 response：`items/02.http-response.bin`，SHA-256 `e14ca39d2f8ededcb73340d2c38a772c10d458c49d3a88f0f5dd5d5d12de7141`，bytes=430

## 04. general-zh-choice-04（zh / choice）

Family：`general-zh-garden-watering-04`；问题 ID：`topic`；状态：pending。

原文 state.text：
~~~~text
连续晴天使花坛土壤变干。园丁决定傍晚浇水，并给新栽的幼苗加上遮阳网。
~~~~

任务说明 instruction：
~~~~text
选择该段落主要描述的活动。
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`repair`
~~~~text
家电维修
~~~~

候选 ID：`cooking`
~~~~text
餐点制作
~~~~

候选 ID：`finance`
~~~~text
财务结算
~~~~

候选 ID：`gardening`
~~~~text
植物养护
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`gardening`；零起始索引：3
~~~~text
植物养护
~~~~

</details>

来源 request：`items/03.decision.json`，SHA-256 `f9b06aeb235e3617ae263bdee0dab6f1e5a4977bb4023741c3cf65c642b24265`，bytes=462
来源 response：`items/03.http-response.bin`，SHA-256 `98009dbc33dfca4ae5dbb8986bdbf4c7fbd647ba0c6c13aab0d0025a738fef3f`，bytes=432

## 05. general-zh-choice-05（zh / choice）

Family：`general-zh-bus-timetable-05`；问题 ID：`information`；状态：pending。

原文 state.text：
~~~~text
我周六需要搭乘7路车，想确认当天第一班车几点发出。
~~~~

任务说明 instruction：
~~~~text
选择发信人需要查询的信息类型。
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`timetable`
~~~~text
公交发车时刻
~~~~

候选 ID：`weather`
~~~~text
天气预报
~~~~

候选 ID：`fare-refund`
~~~~text
车费退款
~~~~

候选 ID：`seat`
~~~~text
座位预订
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`timetable`；零起始索引：0
~~~~text
公交发车时刻
~~~~

</details>

来源 request：`items/04.decision.json`，SHA-256 `52d31de2bd82518e12206e5f806733764b6e0810fc866de7ffa6a289bbae0503`，bytes=453
来源 response：`items/04.http-response.bin`，SHA-256 `6b2bd1e6a6a3cc30425887f3085c3216b300f1ee17b1161b5210fec6d128a8f3`，bytes=438

## 06. general-zh-choice-06（zh / choice）

Family：`general-zh-event-ticket-06`；问题 ID：`help`；状态：pending。

原文 state.text：
~~~~text
我已报名周日的讲座，但找不到入场二维码。请把入场凭证再发给我一次。
~~~~

任务说明 instruction：
~~~~text
选择文本中的主要求助事项。
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`cancel`
~~~~text
取消报名
~~~~

候选 ID：`change-date`
~~~~text
更改讲座日期
~~~~

候选 ID：`resend-ticket`
~~~~text
补发入场凭证
~~~~

候选 ID：`venue-feedback`
~~~~text
评价场地设施
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`resend-ticket`；零起始索引：2
~~~~text
补发入场凭证
~~~~

</details>

来源 request：`items/05.decision.json`，SHA-256 `682cace510ae41106e5adb14ec34a8d38106401e095e400f2a40adaa94b22d50`，bytes=491
来源 response：`items/05.http-response.bin`，SHA-256 `9bcf9eaedc66a7131317862566a71fa9f1b7d553370618d29e8d136ed7ebfed1`，bytes=435

## 07. general-en-choice-01（en / choice）

Family：`general-en-meeting-move-01`；问题 ID：`intent`；状态：pending。

原文 state.text：
~~~~text
The client call overlaps with my dentist appointment. Could we move the call from Tuesday morning to Wednesday afternoon?
~~~~

任务说明 instruction：
~~~~text
Choose the main action requested in the message. Use only the text.
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`reschedule`
~~~~text
Reschedule a meeting
~~~~

候选 ID：`cancel-project`
~~~~text
Cancel the project
~~~~

候选 ID：`share-notes`
~~~~text
Share meeting notes
~~~~

候选 ID：`book-venue`
~~~~text
Book a venue
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`reschedule`；零起始索引：0
~~~~text
Reschedule a meeting
~~~~

</details>

来源 request：`items/06.decision.json`，SHA-256 `9664b101cc99123516b6b77ed35646b1d2a45e49e13e3fb086fa37727d0e7767`，bytes=547
来源 response：`items/06.http-response.bin`，SHA-256 `4eba5f93df9156d95995cfa04c9d6d72ea7721a075083bdf555ea30a4180d2a6`，bytes=434

## 08. general-en-choice-02（en / choice）

Family：`general-en-museum-guide-02`；问题 ID：`service`；状态：pending。

原文 state.text：
~~~~text
I would like to listen to explanations of the paintings while I walk through the museum. Where can I borrow an audio guide?
~~~~

任务说明 instruction：
~~~~text
Choose the museum service the visitor is asking about.
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`cloakroom`
~~~~text
Coat storage
~~~~

候选 ID：`audio-guide`
~~~~text
Audio guide rental
~~~~

候选 ID：`cafe`
~~~~text
Cafe service
~~~~

候选 ID：`parking`
~~~~text
Car parking
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`audio-guide`；零起始索引：1
~~~~text
Audio guide rental
~~~~

</details>

来源 request：`items/07.decision.json`，SHA-256 `a5293642e880c4d0c8a1146dfdb291aff24b658d4befdac717736d8d773c31a1`，bytes=507
来源 response：`items/07.http-response.bin`，SHA-256 `57c5551263951981c170a3b1d8248117b2497f5698a2a2fa27f2e4e13ba01dba`，bytes=436

## 09. general-en-choice-03（en / choice）

Family：`general-en-headphones-damage-03`；问题 ID：`issue`；状态：pending。

原文 state.text：
~~~~text
The headphones arrived with a cracked headband. I have not used them and would like a replacement rather than a refund.
~~~~

任务说明 instruction：
~~~~text
Choose the requested resolution, rather than the cause of the issue.
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`refund`
~~~~text
Return the payment
~~~~

候选 ID：`instructions`
~~~~text
Explain how to operate the device
~~~~

候选 ID：`replacement`
~~~~text
Replace the damaged item
~~~~

候选 ID：`upgrade`
~~~~text
Purchase a more expensive model
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`replacement`；零起始索引：2
~~~~text
Replace the damaged item
~~~~

</details>

来源 request：`items/08.decision.json`，SHA-256 `4ff8704341deae3bc7a05d04f165ea8260defb618526ca747c963f56edaab2ff`，bytes=573
来源 response：`items/08.http-response.bin`，SHA-256 `aac156d463030dd15aa9e7dde13628ed91b4b7f8f20b397839fda2d3cd387fbf`，bytes=434

## 10. general-en-choice-04（en / choice）

Family：`general-en-interview-thanks-04`；问题 ID：`purpose`；状态：pending。

原文 state.text：
~~~~text
Thank you for speaking with me about the analyst role today. I enjoyed learning about the team and appreciate the time you gave me.
~~~~

任务说明 instruction：
~~~~text
Choose the primary purpose of this email.
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`complaint`
~~~~text
Complain about working conditions
~~~~

候选 ID：`withdrawal`
~~~~text
Withdraw a job application
~~~~

候选 ID：`salary-request`
~~~~text
Request a salary payment
~~~~

候选 ID：`thanks`
~~~~text
Express appreciation after an interview
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`thanks`；零起始索引：3
~~~~text
Express appreciation after an interview
~~~~

</details>

来源 request：`items/09.decision.json`，SHA-256 `9af73d42d1a7795dce6e82c379627b412ab38f5319485184c31b25a0715917ef`，bytes=579
来源 response：`items/09.http-response.bin`，SHA-256 `5ae0906d03e2847998bfa7c650ed0a41ae425888a57005f30a4b2be574da7a16`，bytes=431

## 11. general-en-choice-05（en / choice）

Family：`general-en-bike-route-05`；问题 ID：`preference`；状态：pending。

原文 state.text：
~~~~text
For my weekend ride, I want a route with little climbing, even if it is longer. I do not need to reach a destination quickly.
~~~~

任务说明 instruction：
~~~~text
Choose the rider's stated priority.
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`flat`
~~~~text
Avoid steep climbs
~~~~

候选 ID：`short`
~~~~text
Minimize total distance
~~~~

候选 ID：`fast`
~~~~text
Arrive as quickly as possible
~~~~

候选 ID：`traffic`
~~~~text
Ride on busy roads
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`flat`；零起始索引：0
~~~~text
Avoid steep climbs
~~~~

</details>

来源 request：`items/10.decision.json`，SHA-256 `8673393eeea498a65010cfdc1f3f2a73c90ac199dc6b5ff42a61d3d29f80422c`，bytes=517
来源 response：`items/10.http-response.bin`，SHA-256 `07ff2c402660a86508ce3fb9406f71988a2cc419c6fd3870d99f04bc842c1a78`，bytes=432

## 12. general-en-choice-06（en / choice）

Family：`general-en-recipe-allergen-06`；问题 ID：`constraint`；状态：pending。

原文 state.text：
~~~~text
I am making snacks for a guest who cannot eat peanuts. Please suggest a version without peanuts or peanut oil; the other ingredients can stay.
~~~~

任务说明 instruction：
~~~~text
Choose the main constraint on the requested recipe.
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`low-salt`
~~~~text
Reduce salt
~~~~

候选 ID：`no-peanut`
~~~~text
Remove peanut ingredients
~~~~

候选 ID：`no-oven`
~~~~text
Avoid using an oven
~~~~

候选 ID：`larger-batch`
~~~~text
Increase the number of servings
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`no-peanut`；零起始索引：1
~~~~text
Remove peanut ingredients
~~~~

</details>

来源 request：`items/11.decision.json`，SHA-256 `625c2291faf67059bc9a98f83031a73989ace83d46f8bc67c871a5117caf9b10`，bytes=564
来源 response：`items/11.http-response.bin`，SHA-256 `239b0ae84b21847131c1b543ef04fc15b770c925ae5b0184ac91423ec4ef8770`，bytes=437

## 13. general-zh-score-01（zh / score）

Family：`general-zh-deadline-urgency-01`；问题 ID：`urgency`；状态：pending。

原文 state.text：
~~~~text
展台的照明坏了，今天18点开始接待观众。现在是今天16点，负责人要求开场前修好。
~~~~

任务说明 instruction：
~~~~text
按距离明确截止时间的时长评定紧迫程度，取与文本匹配的一个等级；不推测修理所需时间。
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`0`
~~~~text
0：没有明确截止时间
~~~~

候选 ID：`1`
~~~~text
1：距离截止时间超过24小时
~~~~

候选 ID：`2`
~~~~text
2：距离截止时间超过4小时且不超过24小时
~~~~

候选 ID：`3`
~~~~text
3：距离截止时间超过0小时且不超过4小时
~~~~

候选 ID：`4`
~~~~text
4：截止时间已到或已经超过
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`3`；零起始索引：3
~~~~text
3：距离截止时间超过0小时且不超过4小时
~~~~

</details>

来源 request：`items/12.decision.json`，SHA-256 `e86af7f1ce270198767c667f635767bdf5ebd3f108fb65ba402cf37c3c2a9d54`，bytes=621
来源 response：`items/12.http-response.bin`，SHA-256 `4efe389ceefb101fe2fc6c9e2ada3897a7645eb636df1be249c76b89c4602bf3`，bytes=426

## 14. general-zh-score-02（zh / score）

Family：`general-zh-satisfaction-text-02`；问题 ID：`satisfaction`；状态：pending。

原文 state.text：
~~~~text
这次送来的餐食准时，口味也合适。我整体很满意，只希望下次包装更容易打开。
~~~~

任务说明 instruction：
~~~~text
根据发信人明确表达的整体评价选择满意程度，小建议不自动等于整体不满意。
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`0`
~~~~text
0：明确表达整体非常不满意
~~~~

候选 ID：`1`
~~~~text
1：明确表达整体不满意
~~~~

候选 ID：`2`
~~~~text
2：明确表达整体一般或中立
~~~~

候选 ID：`3`
~~~~text
3：明确表达整体满意，但仍有小建议
~~~~

候选 ID：`4`
~~~~text
4：明确表达非常满意，且没有提出改进建议
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`3`；零起始索引：3
~~~~text
3：明确表达整体满意，但仍有小建议
~~~~

</details>

来源 request：`items/13.decision.json`，SHA-256 `648f3e56d1c56986b5f8708a63b8bc7b547c57a5e1db168184f3f36af85275f3`，bytes=606
来源 response：`items/13.http-response.bin`，SHA-256 `1c1d4033b41a1773bba4d68b836abeaf00f41c168d1722fe68bfff80c279b22c`，bytes=431

## 15. general-zh-score-03（zh / score）

Family：`general-zh-attendance-certainty-03`；问题 ID：`certainty`；状态：pending。

原文 state.text：
~~~~text
我已经确认周五有空，也订好了车票；我确定会参加这次交流会。
~~~~

任务说明 instruction：
~~~~text
仅评定说话者在文本中表达的参加意向确定程度，不预测实际是否出席。
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`0`
~~~~text
0：明确表示不会参加
~~~~

候选 ID：`1`
~~~~text
1：表示大概不会参加
~~~~

候选 ID：`2`
~~~~text
2：表示尚未决定是否参加
~~~~

候选 ID：`3`
~~~~text
3：表示大概会参加，但仍未确认
~~~~

候选 ID：`4`
~~~~text
4：明确确认会参加
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`4`；零起始索引：4
~~~~text
4：明确确认会参加
~~~~

</details>

来源 request：`items/14.decision.json`，SHA-256 `ada2ba00c95face7fb2ff9cc7c035cf5082f98a13881d9a707d22b6ad86dc63e`，bytes=519
来源 response：`items/14.http-response.bin`，SHA-256 `c4d3bb4271b043c4b4d5a4ce1edea87802eb978ed9a9f38a006797f8304928ea`，bytes=428

## 16. general-zh-score-04（zh / score）

Family：`general-zh-notice-completeness-04`；问题 ID：`completeness`；状态：pending。

原文 state.text：
~~~~text
请各位志愿者于10月12日14:00到社区二楼活动室集合。
~~~~

任务说明 instruction：
~~~~text
检查通知是否明确给出四项信息：对象、日期、具体时刻、地点。等级表示明确给出的项目数量；不补充外部信息。
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`0`
~~~~text
0：没有任何一项
~~~~

候选 ID：`1`
~~~~text
1：明确给出一项
~~~~

候选 ID：`2`
~~~~text
2：明确给出两项
~~~~

候选 ID：`3`
~~~~text
3：明确给出三项
~~~~

候选 ID：`4`
~~~~text
4：明确给出四项
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`4`；零起始索引：4
~~~~text
4：明确给出四项
~~~~

</details>

来源 request：`items/15.decision.json`，SHA-256 `efab031dacf605597c6c7f2700408367b5b72a38accc84672f0bb2c077f0ea48`，bytes=513
来源 response：`items/15.http-response.bin`，SHA-256 `29d24fa8f37d0b104c7e1bc8090c5e6c2d68c78fc53ba61e04cc15d95bed1a8e`，bytes=431

## 17. general-zh-score-05（zh / score）

Family：`general-zh-room-noise-05`；问题 ID：`noise`；状态：pending。

原文 state.text：
~~~~text
房间里有人轻声交谈，可以听见说话声，但坐在桌边阅读时不需要提高注意力，也不会听不清对面的正常说话。
~~~~

任务说明 instruction：
~~~~text
按文本描述选择噪声程度，不把没有测量的数据当作分贝数。
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`0`
~~~~text
0：文本明确描述完全听不见声音
~~~~

候选 ID：`1`
~~~~text
1：能听见声音，但不妨碍阅读或正常对话
~~~~

候选 ID：`2`
~~~~text
2：已妨碍阅读，但正常对话仍能听清
~~~~

候选 ID：`3`
~~~~text
3：正常对话需要提高音量才能听清
~~~~

候选 ID：`4`
~~~~text
4：提高音量后仍无法进行对话
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`1`；零起始索引：1
~~~~text
1：能听见声音，但不妨碍阅读或正常对话
~~~~

</details>

来源 request：`items/16.decision.json`，SHA-256 `956112e750b739384229cc04fb5ab91ccaf1124c0931cdd3ebcd5b8dee505d0e`，bytes=635
来源 response：`items/16.http-response.bin`，SHA-256 `4d682a019dde135460c4a160ec0aef1c69e7e2a8a6e401ae5df185ecca9f1246`，bytes=424

## 18. general-en-score-01（en / score）

Family：`general-en-weekly-practice-01`；问题 ID：`frequency`；状态：pending。

原文 state.text：
~~~~text
During the seven-day week described here, Maya practised the piano on Monday, Wednesday, and Saturday, and on no other days.
~~~~

任务说明 instruction：
~~~~text
Rate the stated practice frequency for this week. Count distinct days in the text only.
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`0`
~~~~text
0: zero days
~~~~

候选 ID：`1`
~~~~text
1: one day
~~~~

候选 ID：`2`
~~~~text
2: two or three days
~~~~

候选 ID：`3`
~~~~text
3: four or five days
~~~~

候选 ID：`4`
~~~~text
4: six or seven days
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`2`；零起始索引：2
~~~~text
2: two or three days
~~~~

</details>

来源 request：`items/17.decision.json`，SHA-256 `c71ef8ba3952ad6d228133f79fe350ea9963bade649e7b62f6ee65d09fa7ad9c`，bytes=471
来源 response：`items/17.http-response.bin`，SHA-256 `a004a43f1d3436426101a6dbeeb3053d15b5a921e8d737452a0b5ccfb24376c2`，bytes=428

## 19. general-en-score-02（en / score）

Family：`general-en-maintenance-disruption-02`；问题 ID：`disruption`；状态：pending。

原文 state.text：
~~~~text
During maintenance, all desks and meeting rooms remain available. Only the printer is unavailable; staff can continue their normal work except for printing.
~~~~

任务说明 instruction：
~~~~text
Choose the level of disruption explicitly described. Do not infer failures of other services.
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`0`
~~~~text
0: no service is unavailable
~~~~

候选 ID：`1`
~~~~text
1: one optional service is unavailable, while ordinary work continues
~~~~

候选 ID：`2`
~~~~text
2: several services are unavailable, but ordinary work continues
~~~~

候选 ID：`3`
~~~~text
3: ordinary work must pause for part of the day
~~~~

候选 ID：`4`
~~~~text
4: the whole workplace is closed for the day
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`1`；零起始索引：1
~~~~text
1: one optional service is unavailable, while ordinary work continues
~~~~

</details>

来源 request：`items/18.decision.json`，SHA-256 `e2a508fa409034d04dde2ee86b03bf32ff12885fe23a3012a5e92b35e910cf84`，bytes=680
来源 response：`items/18.http-response.bin`，SHA-256 `acc6bc5914180d1e74a468aa7439595c0b6b54bf41dd7cb300bebf4d46f8d9de`，bytes=429

## 20. general-en-score-03（en / score）

Family：`general-en-bug-report-detail-03`；问题 ID：`detail`；状态：pending。

原文 state.text：
~~~~text
On app version 2.4, opening Settings and selecting Export shows an error message instead of saving a file. The report does not include the exact error text or a screenshot.
~~~~

任务说明 instruction：
~~~~text
Count which of these four details are explicitly present: app version, steps to reproduce, observed outcome, exact error text. A screenshot is not one of the four details.
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`0`
~~~~text
0: none of the four details
~~~~

候选 ID：`1`
~~~~text
1: one of the four details
~~~~

候选 ID：`2`
~~~~text
2: two of the four details
~~~~

候选 ID：`3`
~~~~text
3: three of the four details
~~~~

候选 ID：`4`
~~~~text
4: all four details
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`2`；零起始索引：2
~~~~text
2: two of the four details
~~~~

</details>

来源 request：`items/19.decision.json`，SHA-256 `4f63ada25c892cc2c422df4364e6636cf0a491ece3a2becf8576aa42f5b27e48`，bytes=644
来源 response：`items/19.http-response.bin`，SHA-256 `99c10e0f93ece615498df3ed80967478b5e5bc5d42b6dedb2f08a994875353ab`，bytes=425

## 21. general-en-score-04（en / score）

Family：`general-en-rain-expectation-04`；问题 ID：`expectation`；状态：pending。

原文 state.text：
~~~~text
The speaker says: 'Rain is possible this afternoon, but I am not leaning either way.' No forecast numbers are provided.
~~~~

任务说明 instruction：
~~~~text
Rate the speaker's expressed expectation of rain. This is an ordinal reading of the words, not a calibrated probability or a weather forecast.
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`0`
~~~~text
0: explicitly certain that it will not rain
~~~~

候选 ID：`1`
~~~~text
1: expects no rain, while allowing uncertainty
~~~~

候选 ID：`2`
~~~~text
2: expresses possibility without leaning toward rain or no rain
~~~~

候选 ID：`3`
~~~~text
3: expects rain, while allowing uncertainty
~~~~

候选 ID：`4`
~~~~text
4: explicitly certain that it will rain
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`2`；零起始索引：2
~~~~text
2: expresses possibility without leaning toward rain or no rain
~~~~

</details>

来源 request：`items/20.decision.json`，SHA-256 `304aaf9eb946b6ac18507b252f59923b10ea50b6b27cb51fc835946b45f283ff`，bytes=675
来源 response：`items/20.http-response.bin`，SHA-256 `1920782eb13e7678ab12bb20e56dd842545e45f35829287b9f59c0c40b821f80`，bytes=430

## 22. general-en-score-05（en / score）

Family：`general-en-request-politeness-05`；问题 ID：`courtesy`；状态：pending。

原文 state.text：
~~~~text
Could you please send the updated schedule when you have a moment? Thank you for your help.
~~~~

任务说明 instruction：
~~~~text
Use the following explicit courtesy rubric. Rate wording only, without inferring the speaker's personal attitude.
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`0`
~~~~text
0: insults or threats directed at the recipient
~~~~

候选 ID：`1`
~~~~text
1: a bare command, with neither please nor thanks
~~~~

候选 ID：`2`
~~~~text
2: a request containing please but no thanks
~~~~

候选 ID：`3`
~~~~text
3: a request containing both please and thanks, without allowing the recipient flexibility
~~~~

候选 ID：`4`
~~~~text
4: a request containing both please and thanks and explicitly allowing the recipient flexibility
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`4`；零起始索引：4
~~~~text
4: a request containing both please and thanks and explicitly allowing the recipient flexibility
~~~~

</details>

来源 request：`items/21.decision.json`，SHA-256 `d6b7d917abfaf3a7f732df02b575e65a6a406533e7f493aa1a68add3d59f75c7`，bytes=707
来源 response：`items/21.http-response.bin`，SHA-256 `5e0792b5a49ae0905561858b226170937735b85536ff2800c53c27d6abd0b424`，bytes=427

## 23. general-zh-boolean-01（zh / boolean）

Family：`general-zh-parcel-sequence-01`；问题 ID：`claim`；状态：pending。

原文 state.text：
~~~~text
记录显示：快递员先联系收件人，随后把包裹放进储物柜，最后发送了取件码。
~~~~

任务说明 instruction：
~~~~text
仅判断陈述是否符合记录中的明确顺序；符合取true，不符合取false。
~~~~

待判断陈述 statement：
~~~~text
取件码是在包裹放进储物柜之后发送的。
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`false`
~~~~text
陈述不符合记录
~~~~

候选 ID：`true`
~~~~text
陈述符合记录
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`true`；零起始索引：1
~~~~text
陈述符合记录
~~~~

</details>

来源 request：`items/22.decision.json`，SHA-256 `03e2be750f87555d8626c416207846608cab7bd8f70140d0367a9b0d6e4693f6`，bytes=484
来源 response：`items/22.http-response.bin`，SHA-256 `105b69061fc54d6caa81b9d388f34d6286aea888dfe5c2a5b57c0841a21f3c38`，bytes=427

## 24. general-zh-boolean-02（zh / boolean）

Family：`general-zh-box-count-02`；问题 ID：`claim`；状态：pending。

原文 state.text：
~~~~text
桌上原有12个纸盒，工作人员拿走5个，之后没有再放上或拿走纸盒。
~~~~

任务说明 instruction：
~~~~text
依据文本中的数量变化判断陈述，不假设未提及的变化。
~~~~

待判断陈述 statement：
~~~~text
桌上现在还剩8个纸盒。
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`false`
~~~~text
陈述与数量变化不符
~~~~

候选 ID：`true`
~~~~text
陈述与数量变化相符
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`true`；零起始索引：1
~~~~text
陈述与数量变化相符
~~~~

</details>

来源 request：`items/23.decision.json`，SHA-256 `fed6773213f5a9918e0fd0629ef815c3cfaa6bf53f56c7aa7d2f9316dbc085f2`，bytes=446
来源 response：`items/23.http-response.bin`，SHA-256 `bd696e47dc3f505223bc2b3527218f03ca5feb8c2c5d03792954509907b385b7`，bytes=427

## 25. general-zh-boolean-03（zh / boolean）

Family：`general-zh-cafe-exception-03`；问题 ID：`claim`；状态：pending。

原文 state.text：
~~~~text
咖啡店平时周一到周日营业。本周因为设备检修，周三全天暂停营业，其他日期照常营业。
~~~~

任务说明 instruction：
~~~~text
判断陈述是否符合本周的营业安排；本周明确例外优先于平时安排。
~~~~

待判断陈述 statement：
~~~~text
这家咖啡店本周周三照常营业。
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`false`
~~~~text
陈述不符合本周安排
~~~~

候选 ID：`true`
~~~~text
陈述符合本周安排
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`false`；零起始索引：0
~~~~text
陈述不符合本周安排
~~~~

</details>

来源 request：`items/24.decision.json`，SHA-256 `2390ed591dee40bc8eb65fc78c3d9625fa5c2cc798f822af3bbee21307b5a85c`，bytes=499
来源 response：`items/24.http-response.bin`，SHA-256 `b43f496a0e87f39075c2c4ec89de842a391573f15ff06bfefc507ec8aa5efcec`，bytes=428

## 26. general-zh-boolean-04（zh / boolean）

Family：`general-zh-club-attendance-04`；问题 ID：`claim`；状态：pending。

原文 state.text：
~~~~text
摄影小组共有甲、乙、丙三位成员。本次活动甲和丙到场，乙请假，没有其他成员。
~~~~

任务说明 instruction：
~~~~text
根据明确列出的成员和到场情况判断陈述。
~~~~

待判断陈述 statement：
~~~~text
摄影小组的所有成员都参加了本次活动。
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`false`
~~~~text
陈述与到场情况不符
~~~~

候选 ID：`true`
~~~~text
陈述与到场情况相符
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`false`；零起始索引：0
~~~~text
陈述与到场情况不符
~~~~

</details>

来源 request：`items/25.decision.json`，SHA-256 `3cdca287bcfc8b80a7044030f1a7333848b43824c1bfe0233499d73e65bf6cd8`，bytes=472
来源 response：`items/25.http-response.bin`，SHA-256 `c3b11dff705032ee1303903a4dd9a2539c4d9bf637237b9bc62cde08fc5e4279`，bytes=428

## 27. general-zh-boolean-05（zh / boolean）

Family：`general-zh-notice-negation-05`；问题 ID：`claim`；状态：pending。

原文 state.text：
~~~~text
公告写明：系统升级已经完成，今晚不会安排第二次停机；用户现在可以正常登录。
~~~~

任务说明 instruction：
~~~~text
只判断陈述是否受到公告明确支持，不把历史停机当作未来安排。
~~~~

待判断陈述 statement：
~~~~text
公告说今晚不会再安排停机。
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`false`
~~~~text
公告不支持该陈述
~~~~

候选 ID：`true`
~~~~text
公告支持该陈述
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`true`；零起始索引：1
~~~~text
公告支持该陈述
~~~~

</details>

来源 request：`items/26.decision.json`，SHA-256 `4f1ce0547cb4c7f11cf81f6ffd39999c902d3b7e507262e66f951edef6e8d1a6`，bytes=478
来源 response：`items/26.http-response.bin`，SHA-256 `5cf77e7f00f0a98bf2698fe796f5ad71aa0a78e52098cef45bee76163b2e1490`，bytes=427

## 28. general-en-boolean-01（en / boolean）

Family：`general-en-workshop-time-01`；问题 ID：`claim`；状态：pending。

原文 state.text：
~~~~text
The drawing workshop starts at 09:15. The clay workshop starts at 10:00 on the same day. Both times use the same local clock.
~~~~

任务说明 instruction：
~~~~text
Judge the statement using the two stated start times only.
~~~~

待判断陈述 statement：
~~~~text
The drawing workshop starts later than the clay workshop.
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`false`
~~~~text
The statement conflicts with the stated times
~~~~

候选 ID：`true`
~~~~text
The statement agrees with the stated times
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`true`；零起始索引：1
~~~~text
The statement agrees with the stated times
~~~~

</details>

来源 request：`items/27.decision.json`，SHA-256 `7e3b27e3f28c3628ef46d97b2265148ecafb4c018236a050499861513cec177b`，bytes=523
来源 response：`items/27.http-response.bin`，SHA-256 `11a435d7a67b6eb50a356e2c4e8d17e65a9f59ab281f396f61996847a8cffbe8`，bytes=427

## 29. general-en-boolean-02（en / boolean）

Family：`general-en-stock-change-02`；问题 ID：`claim`；状态：pending。

原文 state.text：
~~~~text
A store had 18 notebooks at opening. It sold 4 and then received 6 more. There were no other inventory changes.
~~~~

任务说明 instruction：
~~~~text
Judge the statement from the complete inventory changes given in the text.
~~~~

待判断陈述 statement：
~~~~text
The store has 20 notebooks after these changes.
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`false`
~~~~text
The statement conflicts with the inventory changes
~~~~

候选 ID：`true`
~~~~text
The statement agrees with the inventory changes
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`true`；零起始索引：1
~~~~text
The statement agrees with the inventory changes
~~~~

</details>

来源 request：`items/28.decision.json`，SHA-256 `a8bc42922990bff20d745de35abd28b78b64ace4f0bcae608c25cc1e77c664fc`，bytes=525
来源 response：`items/28.http-response.bin`，SHA-256 `0441e3bfab6369c8ec95b681398d0067f11f5393dadceba82e8a1aa624862638`，bytes=427

## 30. general-en-boolean-03（en / boolean）

Family：`general-en-reading-room-rule-03`；问题 ID：`claim`；状态：pending。

原文 state.text：
~~~~text
Entry to the reading room requires a valid visitor pass. Sam has a visitor pass, but it expired yesterday. No exception to the rule is stated.
~~~~

任务说明 instruction：
~~~~text
Judge only whether the stated requirement is met, without inventing an exception.
~~~~

待判断陈述 statement：
~~~~text
Sam's pass meets the stated requirement for entry today.
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`false`
~~~~text
The stated requirement is not met
~~~~

候选 ID：`true`
~~~~text
The stated requirement is met
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`false`；零起始索引：0
~~~~text
The stated requirement is not met
~~~~

</details>

来源 request：`items/29.decision.json`，SHA-256 `d7020718a3c5196fc2ba1accfbca18330c6874371c04f90c148217922f510f76`，bytes=537
来源 response：`items/29.http-response.bin`，SHA-256 `aa03950d33e205f108a0155d5b3bb8e3e41ec51fa0bff5530338b7e4e95611f9`，bytes=428

## 31. general-en-boolean-04（en / boolean）

Family：`general-en-team-role-04`；问题 ID：`claim`；状态：pending。

原文 state.text：
~~~~text
For the newsletter, Priya writes the articles, Leo checks spelling, and Noor arranges the page layout. These are their only assigned tasks in this note.
~~~~

任务说明 instruction：
~~~~text
Judge the statement from the explicit assignment of tasks.
~~~~

待判断陈述 statement：
~~~~text
Noor is assigned to check the spelling.
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`false`
~~~~text
The statement conflicts with the task assignment
~~~~

候选 ID：`true`
~~~~text
The statement agrees with the task assignment
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`false`；零起始索引：0
~~~~text
The statement conflicts with the task assignment
~~~~

</details>

来源 request：`items/30.decision.json`，SHA-256 `46a0e28b7cdce246d28a3214c7e5271c68f4b7268a189a616769beb84132b6f8`，bytes=538
来源 response：`items/30.http-response.bin`，SHA-256 `7fbfb0f2953ceb03810621fa9cf2fbadd77bed4f1e088cd2840101feb7428f15`，bytes=428

## 32. general-en-boolean-05（en / boolean）

Family：`general-en-key-location-05`；问题 ID：`claim`；状态：pending。

原文 state.text：
~~~~text
A key was in the blue drawer. Elena moved it to the green box, then closed the blue drawer. Nobody moved the key again.
~~~~

任务说明 instruction：
~~~~text
Use the final location explicitly described, without treating the closed drawer as evidence of the key's location.
~~~~

待判断陈述 statement：
~~~~text
The key is now in the green box.
~~~~

完整候选（ID 按原请求顺序；Score 的 ID 是从零开始的字符串）：

候选 ID：`false`
~~~~text
The statement conflicts with the final location
~~~~

候选 ID：`true`
~~~~text
The statement agrees with the final location
~~~~

人工独立答案（候选 ID）：________
审核人 / 身份 / 审核时间：________
人工判定（同意 / 纠正 / 不明确 / 题目问题）：________
纠正后的候选 ID（如需）：________
备注（包括是否已看过教师建议）：________

<details><summary>填完独立答案后展开：实际教师建议</summary>

教师选中的 ID：`true`；零起始索引：1
~~~~text
The statement agrees with the final location
~~~~

</details>

来源 request：`items/31.decision.json`，SHA-256 `bce577eae336b049d3d666c14c2d55b127c7026bfc86839eb7a15aaff2712536`，bytes=552
来源 response：`items/31.http-response.bin`，SHA-256 `4ced641407940e015d80f77c8298c98bf73ddad7d8f772eb02e4ea3283ccd460`，bytes=427

