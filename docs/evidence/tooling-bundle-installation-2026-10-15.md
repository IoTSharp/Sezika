# T3 完整归档安装与依赖探针

日期：2026-10-15（Asia/Shanghai）。

`bundle install` 使用C#校验完整TAR及SHA清单，在新目录解包，核对所有成员与native权限，再对声明的原生程序执行限定的help/version探针。已有安装的verify模式只核字节与权限，明确不冒充依赖重新执行。安装目录、证据目录及外层回执分离；失败保留marker、部分输出和原始执行状态。

Windows/Ubuntu Native AOT均完成tiny1后full10。完整检查含缺项/额外项/字节漂移、脚本路径及遍历拒绝、严格非零stderr、未知清理/参数不符/marker失败、取消及CLI错误回执保护，最后一项真实执行解包后的native fixture help。synthetic测试适配器仅在测试代码内注入，不由JSON启用；真实native探针有独立执行回执及SHA。此验收不接受服务器、训练或模型质量。

三份直接源码仍与已验冻结候选逐字节相同；[验收清单](tooling-bundle-installation-2026-10-15/acceptance.json)绑定两平台原始结果、检查列表及SHA。本提交仅接入安装与fixture的help/version，GPU依赖已由前一独立提交提供；Git和新增语料检查另行交付。
