# <模型名称与实验版本>

<用两三句介绍独立模型的任务、输入、输出和目标使用者。区分代码实现、已保存模型、公开下载及服务部署；采用自己的表述，不复制平台其他模型的性能、价格或 API 能力。>

正式产品/仓库名称和产物版本分开，例如 `Sezika-Decision-Base` / `v0.1-preview`。正式命名与组织public发布不构成正式质量、校准、AOT或性能验收；短程实验继续标明experimental。maikebing private内部预发布与IoTSharp public正式发布目标分别记录真实URL/ref及认证合同。

## 任务与架构

记录 Choice/Boolean/Score 等实际支持的题型、候选 ID/等级语义、分布和预测输出；概率校准状态单列。填底座/准确 revision、encoder/head 类型、层数/hidden/head/最大 token 配置和实际验收长度。说明 C#/.NET、构建期 PTX/ABI 与静态 CUDA Driver 路径，区分本模型的新权重 AOT/性能与旧引擎证据。

## 本次训练

填写 run-id、三个固定输入 commit、底座或续训起点、优化器/精度/超参/seed、计划步数与实际完成步数。列明实际使用的来源及原始 split、train/development/test/family 边界、准入/隔离/重复数量、未适配来源、私有数据可见性和 rights 状态。

若仅短程实验，写实际题目数和 train 覆盖比例，不能称完整一轮、收敛或质量改善。多个来源的镜像存在不等于已全部参与训练；teacher soft target 不等于人类正确率。

## 独立验证

| 子集 | 原题分母 | 指标及定义 | 数值 |
| --- | ---: | --- | --- |
| <train/development/test> | <实际题数> | <soft-target CE/teacher argmax 一致/人类正确率，分别填写> | <实测值> |

写独立进程身份、实际模型字节/SHA、原题与换序次数、候选 ID 稳定/改变数；不能把进度上限写成实际分母。是否有训练前同子集对照、正式封存测试、逐语言质量、校准、新权重 AOT 和性能分别写 PASS/FAIL/NOT_RUN/NOT_ACCEPTED，不能用一次 forward/退出0替代质量验收。

## 获取与 C# 消费

填写实际模型仓库 URL、准确已发布 commit、package 入口、完整权重/tokenizer/config/checkpoint/报告/来源许可清单及 SHA。说明实际已验收的 C# 加载/CLI 入口及所需合同，旧固定底座 loader 不自动等于新包支持；未经实测的公开 predict/API/AOT 接入标明待完成。只有实际服务已部署并验收才能列出可调用 Serverless API。

平台单对象超限时，分块作为传输层管理：准确清单绑定每块路径/顺序/大小/SHA、原完整文件大小/SHA，重组后完整核验才可恢复 checkpoint，不改写原 receipt 或丢弃 moments。保持失败提交及回执；新传输版本不能让超限旧 LFS 对象继续作为待推祖先。

模型可按当轮显式授权发布到组织；记录源/目标 commit 和各自按visibility进行的独立完整byte/SHA验收。本轮maikebing private源模型与private数据使用授权认证新checkout完整验收，注明authentication=true；IoTSharp public模型用匿名完整验收。不镜像private数据原件/derived/凭据/缓存，不在缺少当前明确授权时改变visibility。

## 许可与来源

代码、encoder、tokenizer、数据、校准和新输出逐组件填写来源/revision/许可/redistribution 状态，保留原始 notices 的 byte/SHA。整体 unknown 时如实填写，不以一个组件的 MIT/Apache 声明推导整体商用许可。

介绍结构可参考 [模力方舟 Decision Model 分类页面](https://moark.com/serverless-api#Decision-Model)。保存实际引用时间、页面/检索出处及本次 run 报告身份；参考页面仅说明产品分类/表达结构，不是本模型的实测证据。

## 平台短介绍

<概括任务、架构、配置上限、本次实测范围与质量/校准/AOT/服务状态；不得使用未验收的“更快”“更准确”“已提供 API”等描述。>
