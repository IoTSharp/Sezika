# 源码、数据、模型身份

三个仓库分别固定commit与逐文件SHA；不把某个默认分支或历史ref称为当前latest。实际读写权限和visibility按当轮核实；历史仓库/ID/发布结果见 [原文快照](../../../../docs/history/tooling-skills-20261014/sezika-repository-training/references/repositories.md)。

- 源码使用既有IoTSharp/Sezika origin；完整工具包还绑定工程输入清单和PTX/ABI。dirty源码字节单独冻结，不能声称source commit已包含它们。
- 原始及派生训练数据留在private。源DatasetCommit与完整派生目录实际PublishedDatasetCommit分开，后者须独立认证fresh内容读回；原provenance不变。
- maikebing模型通道为private内部预发布；IoTSharp公开正式模型使用Sezika-Decision-Base。模型卡、名称或公开状态不能提高质量等级，发布须当前准确payload授权。

逐项管理代码、权重、tokenizer、数据、校准、教师与notices许可。读取真实RIGHTS_MANIFEST和来源声明，unknown保持unknown，不能将encoder MIT或单个数据Apache声明套到全部包。

使用C# `git run`/HTTP publication与独立读回；凭据不进URL/argv/仓库配置/日志/发布材料。private匿名401/403是权限状态；public完整验收另用独立匿名路径，不复用private缓存。callback或存储HTTP200不等于发布ref或完整可见性通过。

仓库loader/helper不是数据读取入口。完整模型与数据资产不嵌入程序集、NuGet或源码提交；代码上传授权不包含模型/数据发布。
