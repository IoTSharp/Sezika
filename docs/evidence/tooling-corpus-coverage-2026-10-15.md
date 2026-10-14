# T5 原语料合同补验与 ToolACE family 修复

日期：2026-10-15（Asia/Shanghai）。

新增检查从冻结旧适配合同、独立源码审查和手写预期出发，不执行旧脚本，也不从新CLI输出反填golden。Windows/Ubuntu同一冻结候选的Native AOT分别完成20项真实C#命令/载体检查及1项内部Adapt检查；两平台都先通过tiny。覆盖类别/标签、目标概率、来源family、语言、分组、真实CSV/TAR载体、准入拒绝、研究/样本绑定及既有TeacherTool审阅布局。

ToolACE原family种子是首轮会话的单元素数组；C#此前误用单个对象，现恢复数组规范化哈希，保留独立预期。Open-Jev检查使用JSON编码的state_json标量；维度负例明确使用两个选项、三维合法概率，断言精确的`Target dimension.`，保证进入目标维度门。该项仅验证内部Adapt；没有把Parquet解码或CLI来源准入写成已支持。

直接源码、四份原始原生结果和20项检查列表见 [验收清单](tooling-corpus-coverage-2026-10-15/acceptance.json)。所有回执均退出0、EOF完整、清理通过且无剩余进程。旧失败保留。本次没有真实教师、模型训练、数据发布或服务器验收；完整调用者退役仍由T7/T8-code另行验收。
