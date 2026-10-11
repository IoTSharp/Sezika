# 模力方舟个人模型完整发布验收

2026-10-11，北京时间。用户明确授权继续上传和登记。[maikebing/Sezika-mmBERT-8192-experimental](https://moark.com/maikebing/Sezika-mmBERT-8192-experimental) 已完成原始模型发布、平台登记和匿名完整下载验收。模型ID28817、Gitee仓库ID50738359；远端master为 `06ebe82325e58c0ae723fb9014a38cdb2f87b22a`。18文件共1,245,737,297 bytes，包含完整encoder、tokenizer、训练head/checkpoint、配置、模型卡及组件许可。公开布局保持原18文件，无需分片重组。机器可读实证见[JSON](moark-personal-publication-complete-2026-10-11.json)。

## 登记和上传的实证

原gai路径完整BOS对象独立流式读回1,227,772,549 bytes，SHA256精确为 `6168c84d2498cdacdcafee16b71a69b5c1636ad569bfabaf2855f5ce7dabd528`；这一步仅证明存储内容。正确路径长等待和官方ai.gitee.com替代入口的callback均真实HTTP200/JSON true，但六次后查仍列encoder缺失。备用19片运输包仅本地准备并完整重组SHA通过；只上传末片19,812,997 bytes测试gai登记，仍缺失，因此没有上传其余分片或发布运输变体。

原生Gitee凭据实际身份maikebing/73119、UTF16LE及旧读取一致均真实HTTP200通过，未发现编码问题。底层标准download batch对encoder和测试片真实报object404，对tokenizer提供真实download action，排除把callback200当登记完成。

此前普通LFS的paid/trial enterprise失败来自IoTSharp组织，不能推定个人库同样拒绝。个人库标准upload batch实际提供上传/verify授权。由真实Git LFS basic上传测试片，服务端下载batch提供对象，完整下载SHA与源一致且目标missing-lfs为空，之后才执行原模型普通Git push。仅本次命令临时清空gai standaloneagent，强制basic、并发1、maxretries0；固定真实pre-push钩子和源提交，无force/no-verify/悬空pointer绕过。

原模型push真实exit0，远端master由f33889013d3c2d60bb25edf5e725681d370f2fd2更新为固定准备提交；后查引用独立确认。最终平台两对象missing=[]，模型和仓库身份保持准确。

## 匿名完整验收

独立新进程、空HOME、禁系统/全局Git配置、禁helper/askpass/extraHeader、无凭据HTTP流式请求。准确19 Git路径（18发布文件加.gitattributes）、16小文件bytes/SHA、两个LFS pointer分别通过；tokenizer完整17,525,329 bytes和encoder完整1,227,772,549 bytes实际匿名GET/SHA通过，绑定固定提交。不能以包页面、HTTP200或首尾Range替代该完整内容证明。

训练及HF既有发布保持，本轮没有训练、教师调用、GPU/旧服务器修改或源码push。概率校准、正式质量、第三方权利补证及新head AOT/性能继续各自pending；模型卡和组件许可原字节保留。

## 历史、凭据与进程

前次完整存储、空callback路径、ReadTimeout及8次后查的[历史回执](moark-maikebing-model-publishing-2026-10-11.md)和JSON保持原快照。本次长等待/替代入口/运输tiny失败分别保存，未将失败回执改成成功。官方文档只有Git创建推送与gai两种流程；旧“三种”研究已纠正。没有证明具体服务端bug或必需未知metadata。

标准tiny脚本的一次timeout位置参数误用引发Python后台stdin writer TypeError，原执行源码及stderr已保存；实际上传、backend完整GET和平台missing[]均独立通过。已改为明确timeout参数，不重复成功上传。进程监护真实状态与后核见JSON及最终收口证据；历史短进程unknown/CleanupFailed保持，后核absent不补造历史argv。

令牌保持Windows凭据管理器，BOS临时授权仅内存，Git配置无真实令牌；实际值扫描以最终回执为准，不宣称Python字符串全内存擦除。完整文件保留于 `D:\source\Sezika\.artifacts\model-publishing-20261009-01a12099\moark-maikebing-20261010\model-repository`。原上传和分片备用交付物保留，不清共享TEMP或用户文件。

文档生成短进程的原监护为CleanupFailed/exit0/unknown1，未改写；随后本轮18份监护回执/61登记身份的只读后核matching live0/current unknown0。95个明确文本文件/567,748 bytes的当前凭据真实值泄漏扫描通过。该后核不补造过去未观察argv，最终提交进程由单独监护收口。
