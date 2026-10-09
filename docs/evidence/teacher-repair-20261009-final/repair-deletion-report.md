# 修题与删除明细

原256题：保留原题178，修订并盲审通过37，删除41；新版本215题，train162 / development53。原始证据不删除；正式人审、模型质量与Native合同/长度验证仍待完成。

## massive-en-us-00004（train）：deleted

原文中的 quiet 既可表示要求完全静音，也可表示要求降低音量，没有足够信息区分这两个合理候选。补充区分条件会引入原文未提供的用户意图，因此无法忠实修复。

## massive-en-us-03201（train）：repaired_and_reaudited

原文明示请求在有其他事情发生时提醒自己，但原候选集缺少条件提醒或通知选项。保留原指令和全部原候选，仅增加对应候选即可表达该意图，无须推断用户要求创建日历事件。

## massive-en-us-15744（train）：deleted

email 可能指邮箱地址或邮件消息，add 也未明确是添加联系信息还是创建邮件。原文无法唯一确定操作，不能通过补写用户意图或指定分类规则忠实修复。

## massive-en-us-06510（train）：deleted

原文只有邮件主题词，没有说明读取、发送、查询联系人或添加联系人等操作。缺少确定主意图的必要信息，补充候选或调整指令也无法得到唯一答案。

## massive-en-us-10137（train）：repaired_and_reaudited

原文明示给母亲打电话，原候选集缺少拨打电话这一意图。补充对应候选即可保留原任务，无需推断邮件或联系人查询操作。

## massive-en-us-04539（train）：repaired_and_reaudited

原文明确要求暂缓发送，直到用户发出发送指令。补充包含这一时序要求的消息暂缓发送候选即可，不需要将消息推定为电子邮件。

## massive-en-us-00045（train）：deleted

该短语既可用于打招呼，也可用于询问近况。原文缺少区分这两种用法的上下文；指定其归入某一类别会强行消除原话的多义性，无法忠实修复。

## massive-en-us-00036（train）：repaired_and_reaudited

原文明确要求检查车辆是否已准备好，原候选集缺少对应的状态检查意图。补充该候选，并明确闲聊项不包含问候或具体任务请求，使候选范围互斥；无需推断车辆的具体准备事项。

## massive-en-us-00012（train）：repaired_and_reaudited

原话明确表达到了睡觉时间，现有候选缺少这一具体意图。补入直接对应原话的候选即可保留意图识别任务，无需推断设备操作，也无需添加泛化的“无法判断”选项。

## massive-en-us-00212（train）：deleted

原话是不完整的陈述，没有说明灯开始做什么，也未提出设备操作请求。缺失的动作无法从原文恢复，不能通过补充候选或调整指令唯一确定原意图。

## massive-en-us-06204（train）：deleted

finished既可能表示完成列表中的任务，也可能表示完成列表的编写；原话没有明确请求创建、删除或查询列表。原话本身存在多义，无法忠实确定具体列表操作意图。

## massive-en-us-00143（train）：deleted

原话表达了不喜欢，但it的指代缺失，无法确认对象是否为音乐。修复音乐意图题需要补充上下文；改为识别一般的否定态度会绕开原题所需的对象判断，因此删除。

## massive-en-us-03692（train）：repaired_and_reaudited

原话明确要求重复书中的最后一句，识别该动作无需确定书的具体身份或媒介。补入句子复述候选，并将有声书候选明确为开始播放，可保留原意图识别任务并区分两种动作。

## massive-en-us-06419（train）：deleted

原话只有主题词，没有说明要开始游戏、查询游戏还是列出游戏。所请求的动作依赖缺失的上下文，不能唯一确定；增加“提及游戏”候选会将行动意图识别改为简单主题识别，因此删除。

## massive-en-us-03891（train）：repaired_and_reaudited

原话明确表达返回请求，现有候选缺少这一意图。新增一般返回候选即可匹配原话，无须推断具体返回位置或任何播放请求。

## massive-en-us-06479（train）：deleted

原话可能是寒暄，也可能询问当前情况或正在发生的事件，缺少确定所问对象和请求类型的上下文。无法唯一确定主要意图，新增“其他或不明确”选项也不能解决这一歧义。

## massive-en-us-12995（train）：repaired_and_reaudited

原话明确要求在Facebook上发送消息，现有候选缺少社交网络消息发送类别。补入该候选即可保留原意图识别任务，并与发布帖子、查询帖子及邮件操作区分。

## massive-en-us-04444（train）：deleted

directions既可能指路线指引，也可能指一般指示；原话没有说明指引对象或交通方式。新增路线查询候选仍需猜测词义，合并不同含义则无法唯一识别原话的主要意图，因此不能忠实修复。

## massive-en-us-00005（train）：deleted

原话没有说明要停止的对象或操作，也没有提供与音量有关的证据。确定任何具体操作都依赖缺失的上下文，不能将其直接归为静音或其他音量请求。

## massive-zh-cn-00260（train）：deleted

原话表达听不清对方讲话，但没有确定希望采取哪种操作。将其解释为提高音量需要推断未提及的请求；新增仅复述听觉困难的候选也不能确定其操作意图，无法在保留原任务的前提下可靠修复。

## massive-zh-cn-03601（train）：repaired_and_reaudited

原话明确要求在指定时间提醒，原候选缺少对应操作。新增“设置定时提醒”并明确其与创建日历事项的边界后，可以唯一选择，且无需补充提醒内容。

## massive-zh-cn-09913（train）：repaired_and_reaudited

原话明确询问具体菜品的用料，歧义来自两类候选的范围重叠。补充通用分类边界即可保留原任务，并使“获取食谱”成为唯一匹配项。

## massive-zh-cn-00037（train）：repaired_and_reaudited

原话提出检查笔记本是否工作的任务，现有候选均未覆盖这一请求。新增“检查设备运行状态”即可准确表达其主要意图，无需推测检查结果或具体故障。

## massive-zh-cn-00013（train）：deleted

原话只表示该睡觉了，没有指定设备或操作。关闭灯光、调整灯光模式等解释均依赖缺失的上下文，无法在保留原任务的前提下恢复唯一答案。

## massive-zh-cn-00267（train）：deleted

原话描述环境明暗，未说明灯的当前状态，也未明确要求哪种操作。打开灯与调整已有灯光均是合理解释；仅凭原文无法排除其中任何一项，不能通过补设操作意图修复。

## massive-zh-cn-09665（train）：repaired_and_reaudited

原话明确要求回退有声书的播放进度。将有声书候选的范围补充为包含进度控制，即可准确覆盖该操作，保留原有媒体类别区分。

## massive-zh-cn-00913（train）：repaired_and_reaudited

原话明确给出了打开操作和对象名称。补充不限定对象类别的打开候选，即可对应该请求，无需推断 pandora 的含义或补入播放请求。

## massive-zh-cn-12939（train）：deleted

“怎么回事儿”缺少指代对象及前文，无法确定所问主题属于事实、定义、货币或数学。指定任何类别都需要补入未提供的内容，无法忠实修复。

## massive-zh-cn-02996（train）：repaired_and_reaudited

“股市”明确了主题，“有什么新鲜事”表达对最新消息的查询。补充股市新闻候选，并限定行情与一般事实知识的边界，可消除原候选之间的分类歧义。

## massive-zh-cn-06480（train）：deleted

原话未说明所问事情的对象、范围或背景，无法确定是在询问新闻、活动信息还是当前具体情境。补充宽泛的事件或近况查询候选仍不能确定其指代，缺失的上下文无法仅靠候选修订补足。

## massive-zh-cn-00052（train）：repaired_and_reaudited

原话给出了中餐外送这一对象及最高评分这一筛选条件，支持按评分查找外卖选项。补充评分查询或推荐候选即可覆盖该需求，无需推断下单或已有订单；同时明确烹饪知识与食谱候选的边界。

## massive-zh-cn-04445（train）：deleted

“方向”未限定为出行或导航方向，也可能指方案或思路。原话缺少能够确定交通意图的对象和语境，补充导航候选仍需要假设其含义，无法可靠修复。

## massive-zh-cn-12126（train）：deleted

原话仅表达出行意愿、日期和目的地，没有明确请求查询交通信息或购买、预订车票。区分这些意图需要补充语境，无法仅靠修改候选或分类规则忠实确定唯一答案。

## massive-zh-cn-00006（train）：repaired_and_reaudited

原话明确要求暂停十秒，缺陷是候选中没有暂停操作。补充独立的暂停候选即可覆盖明确意图，并保留原有音量候选。

## massive-zh-cn-01049（train）：repaired_and_reaudited

原话明确请求改变音频设置，但没有指定参数。增加一般音频设置候选，并将其他音量设置限定为明确针对音量的请求，可形成唯一匹配，无须推测具体调整目标。

## massive-en-us-00115（development）：repaired_and_reaudited

原话指定目标音量为低，未明确要求相对当前音量降低。明确相对升降与绝对目标设置的分类边界后，该请求唯一归入其他音量设置，无须假定当前音量。

## massive-en-us-06613（development）：deleted

原文只有主题词，没有表达具体请求，无法区分索要食谱、咨询烹饪信息或查询词义。指定某种分类规则会替原文补入请求意图，无法忠实修复。

## massive-en-us-00676（development）：repaired_and_reaudited

原话明确要求设置时区，原候选缺少这一操作。增加设置时区候选即可唯一匹配，且可与日期或时间转换区分。

## massive-zh-cn-00167（development）：deleted

“嘿嘿”既可表示笑声式闲聊，也可用作招呼，原文没有区分两者的语境。指定分类优先级会任意替代用户意图，无法忠实确定唯一答案。

## massive-zh-cn-00042（development）：repaired_and_reaudited

原话明确请求查询当前可用内存，补充对应候选即可覆盖该意图；同时限定闲聊候选不含打招呼，避免候选范围重叠。

## massive-zh-cn-00697（development）：repaired_and_reaudited

“点亮灯光”表达开启灯光的请求，补充开启候选，并限定其他设置候选不含开关及亮度调节，使开启、调亮与其他设置变更相互区分。

## massive-zh-cn-10449（development）：repaired_and_reaudited

原话明确要求取消指定旅行，补充“取消旅行安排”即可覆盖该意图，无须假定清单或日历归属。

## massive-zh-cn-01179（development）：repaired_and_reaudited

原话直接要求限制播放内容。补充播放设置候选的内容范围含义，并明确偏好表达与播放要求的分类边界，保留两类候选而不推断隐含好恶。

## boolq-train-row-003（train）：repaired_and_reaudited

将问题中的“confectionary sugar”纠正为原文使用的“confectioners' sugar”，保留两种名称是否指同一种糖的原问题。原文明示后者是 powdered sugar 的别名，答案为 Yes。

## boolq-train-row-008（train）：deleted

原文仅说明George Marshall朗读了Bixby letter，没有说明信的作者，无法判断是否由Abraham Lincoln撰写。将问题改为询问朗读者会改变原问题的主要含义，因此删除。

## boolq-train-row-012（train）：deleted

原文描述美国接受法院管辖及提交条约的立场，没有说明ICC在美国境内是否具有管辖权及其适用条件，不能据此确定Yes或No。改问是否向参议院提交条约会更换所问命题，因此删除。

## boolq-train-row-013（train）：repaired_and_reaudited

删除题目中与原文不一致的附加化学式，并将问题规范为明确的是非问句，保留询问碳化钙是否用于生产乙炔的主要含义。原文明示这一用途，支持Yes。

## boolq-train-row-020（train）：deleted

原文仅区分先于感官观察的推论与基于感官观察的推论，没有说明科学是否以首先通过观察获取知识为前提。缺少判定原问题所需的关键证据，不能将未提及当作No；改问两类推论的区别会改变主要含义，因此删除。

## boolq-train-row-027（train）：repaired_and_reaudited

将“part of”的参与标准明确为临时适用EEA协定，并限定到原文所述日期，消除正式缔约方身份与临时参与之间的歧义。原文明示克罗地亚自2014年4月12日起临时适用协定，支持Yes。

## boolq-train-row-032（train）：repaired_and_reaudited

将“built leaning”明确为在施工过程中开始倾斜，消除是否刻意按倾斜状态建造的歧义，保留对建造期间倾斜情况的询问。原文明示倾斜始于施工期间，支持Yes。

## boolq-train-row-033（train）：deleted

原文介绍影片类型和剧情，但未说明故事是否真实或根据真实事件改编，无法确定原问题的答案。改问离婚夫妇寻宝的剧情信息会改变原问题的主要含义，不能据此修复。

## boolq-train-row-040（train）：deleted

原问题未明确所指的是乐队还是某部作品，原文也未提供任何故事是否取材于真实事件的证据。即使限定询问对象，仍无法判断故事来源；改问乐队成立或角色扮演会改变原问题的主要含义。

## boolq-train-row-046（train）：deleted

原文只说明续作进入开发阶段且仍有拍摄的可能，不能确定最终是否会有续集，也不能据此回答否。改问是否宣布开发会将未来结果问题改成公告是否发生的问题，无法忠实保留原命题。

## boolq-train-row-047（train）：deleted

原文给出的粉丝数量均为下界，没有最高粉丝数的精确值或低于十亿的上界，因此无法确认或排除有账号达到十亿粉丝。限定到2018年10月仍无法补足证据；改问是否超过2.6亿会改变原题的核心阈值。

## boolq-train-row-048（train）：deleted

原文描述角色应具有星球统治者的可信形象，但未明确帕德梅拥有女王称号，无法确定原问题的答案。把女王身份改成统治者角色要求会改变所问属性，不能作为忠实修复。

## boolq-train-row-050（train）：repaired_and_reaudited

原文明确说明英语将欧元符号置于数值之前，并指出多数其他欧洲语言的规则不同。将问题限定为英语即可消除歧义，同时保留询问欧元符号位置的主要含义，答案为Yes。

## boolq-train-row-054（train）：deleted

原文只说明梵语是古老的有文献记载的语言之一，并可能源于共同祖语，不能确定它是否是世界第一种语言。改问是否源于原始印欧语会改变原问题的核心命题；在不能补充原文的条件下，应删除。

## boolq-train-row-056（train）：repaired_and_reaudited

将问题限定为美国境内通常是否安排战斗机护航，消除通常安排与曾经发生过护航之间的歧义，保留原题的护航主题。原文明确说通常不安排，答案为 No。

## boolq-train-row-057（train）：deleted

原文提供的是特定年份对特定厂商枪支的按名称禁令，没有说明持有行为的具体规则，也未涵盖所有 AR-15 型号的法律地位。即使限定年份或厂商，仍缺少判断持有是否合法的关键证据；改问是否被按名称禁止会替换原问题的核心命题，应删除。

## boolq-train-row-075（train）：repaired_and_reaudited

保留车内携带手枪是否违法的原命题，明确适用原文所述 H.B. 1815，并限定为满足全部四项条件的得州居民及其拥有或控制的车辆。原文明确允许这种情形，答案为 No。

## boolq-train-row-077（train）：repaired_and_reaudited

将含混的 several 及神经计数口径明确为舌下神经和迷走神经是否都支配舌肌，保留原题关于多个神经参与舌结构支配的主要含义。原文说明舌下神经支配舌肌，而腭舌肌由迷走神经支配，答案为 Yes。

## boolq-train-row-086（train）：deleted

原文没有定义 wheat flour，也没有说明它与 white flour 的关系，缺少同一性判断所需的关键证据。不能把 wheat flour 擅自替换为 wholemeal flour，或因原文未提及就回答 No；保持原文和原问题主要含义时无法可靠修复，应删除。

## boolq-train-row-094（train）：deleted

将角色是否有姓名替换成段落有没有记载其他姓名，改变原命题；原文未提及不能证明原问题的否定答案，按用户授权从新版本排除。

## boolq-validation-row-000（development）：repaired_and_reaudited

修正原问句不完整的比较结构，并限定依据原文报告的数值判断。文中玉米乙醇和甘蔗乙醇的能量产出均高于对应投入，因此答案为No。

## boolq-validation-row-001（development）：repaired_and_reaudited

修正重复助动词造成的语法错误，并明确按本段用法判断。原文直接将“house tax”作为“property tax”的另一名称，因此答案为Yes。

## boolq-validation-row-006（development）：repaired_and_reaudited

将“be divided by”明确为允许余数的除法，消除其与无余数整除之间的歧义，保留奇数能否除以偶数的原任务。原文给出奇数除以2余1的例子，足以支持Yes。

## boolq-validation-row-008（development）：deleted

原文仅说明车辆登记文件和保险证明的要求，没有说明美国驾照是否被加拿大认可，无法支持Yes或No。保持原文不变无法补足关键证据；改问车辆文件要求会改变原问题，故删除。

## boolq-validation-row-011（development）：deleted

原文介绍制作背景、主创和剧情梗概，未说明是否根据真实故事改编。未提及不能推出No，也没有依据回答Yes；保持原文和原问题主要含义无法可靠修复，故删除。

## boolq-validation-row-013（development）：deleted

原文描述肝脏的功能，但未说明排泄系统的组成标准，也未明确肝脏的系统归属。仅凭功能描述无法回答原问题；改问解毒或转化氨的功能会改变问题主旨，因此删除。

## boolq-validation-row-016（development）：repaired_and_reaudited

将“based on a true story”明确为受到真实事件启发，保留询问电影与真实事件关系的主要含义，消除“改编”与“启发”的措辞歧义。原文明确说明该片编剧兼导演受到真实事件启发。

## boolq-validation-row-018（development）：repaired_and_reaudited

将所问季数限定为第一季，并将时间限定为2018年5月14日，保留询问该季是否结束的主要含义。原文明确第一季于当日结束，可以唯一回答。

## boolq-validation-row-020（development）：repaired_and_reaudited

将“come before”明确为原文 Street Addressing 示例中的书写顺序，并将对象限定为与 P.O. Box 号码一致的 Unit 编号，保留地址中先后位置的问意。示例中街道地址在前，Unit 编号在后，因此答案为否。

## boolq-validation-row-038（development）：deleted

原问题询问形成极性碳氢共价键的可能性，原文只说明碳氢键通常被视为非极性。“通常”不能排除例外，原文也未提供极性实例。改问通常如何分类会改变原命题，不能以此伪装修复，因此删除。

## boolq-validation-row-039（development）：deleted

原文未明确“the first film”指《The Golden Compass》，且只报道2011年关于未来不会制作续集的表态，无法据此确定续集是否实际存在。改问 Pullman 当年的表态会改变原问题的主要命题，因此删除。

## boolq-validation-row-052（development）：deleted

原问题询问英格兰是否仍实际举行猎狐活动，原文只提供历史及法律禁令，没有禁令后的实际活动信息。禁令不能证明活动不再发生；改问是否合法会改变原问题的主要含义，因此删除。

## boolq-validation-row-055（development）：deleted

“get together”未明确指接吻、感情发展还是正式交往。原文记载了主动接吻及季终选择，但没有明确两人是否确立恋爱关系，无法唯一回答原问题。改问是否接吻会将关系状态问题替换成具体行为问题，因此删除。

## boolq-validation-row-058（development）：deleted

将组合是否存在改成电影内是否有虚构组合，预设了待辨别的现实/虚构范围并改变原问题命题，不能用此获得确定标签，按用户授权从新版本排除。

## boolq-validation-row-061（development）：deleted

原问题询问一号球场实际是否已有屋顶，原文仅提供建造意向和预计投入使用时间，没有确认完工状态，也没有足以确定答案的参考时点。改问是否预计建成会将实际状态问题替换成预期问题，因此删除。
