# T4 资产运输与发布准备切片

日期：2026-10-14，Asia/Shanghai。候选以 `5286f54e5d87f9e78cc06e9e90a0addf3ed9d135` 的准确索引导出为基础，增加 Transport、两份凭据读取实现、专属检查及本切片的两个共享入口。实际编译来源为 **12 文件 / 191,563 bytes**，完整 SHA 表在独立候选 `compiled-source-manifest.json`。原其他未验接线与训练优化保留。

候选根：`C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\transport-candidate-v1`。

| 检查 | 结果及候选根内回执 |
| --- | --- |
| 独立 Windows 构建 | PASS，0 警告/错误；`win-build-result.json` |
| Windows tiny / 完整运输检查 | PASS 1 / 25；`win-checks-1-result.json`、`win-checks-25-v2-result.json` |
| 既有 CLI / 基础回归 | PASS 32；`baseline32-result.json` |
| Linux 两份实际发布物 | PASS；`linux-tests-publish-result.json`、`linux-fixture-publish-result.json` |
| Ubuntu WSL tiny / 完整运输检查 | PASS 1 / 25；每项分别核 `linux-checks-N-inner-result.json` 与 `linux-checks-N-outer-result.json` |
| Windows Native AOT | 实际发布 PASS；`aot-win-publish-result.json` |
| 原生命令及独立原字节读回 | `transport prepare`、`transport reassemble` PASS；`native-transport-prepare-execution-result.json`、`native-transport-reassemble-v2-execution-result.json` |

执行回执核退出码 0、输入/输出 EOF、清理 PASS 与 remaining 0。首次 Windows 全检查缺少 fixture 环境，保留 CLR 失败原回执；补编候选自己的 C# fixture、在新输出目录重跑后完整通过。首次原生重组请求误用了 snake_case 输出的 PascalCase 属性，产生 null-SHA/JsonException/exit1；失败保留，正确请求与新输出目录随后通过。Native AOT 的 SDK CMD 编译助手保留在实际构建进程记录中。

25 项覆盖精确长度/SHA、分块映射/原逻辑重组、变化/重复/路径/取消拒绝、完整语料发布准备合同、原生独立执行回执、HTTP Range/ETag/断线恢复、跨主机认证限制、授权预览及实际回环 MoArk LFS basic / HF multipart / parentCommit 协议。完整训练回执和 tiny 权重在测试中明确为合成夹具；回环协议使用本任务服务，不调用平台真实写接口。凭据用固定安全存储目标或有界 stdin；不在回执中输出秘密。

原生读回是两个逻辑文件 / 40 bytes 的明确夹具，只证明真实运输链。**外部上传、ref 更新、认证/匿名平台读回、模型/数据发布、正式训练、质量及校准均为 NOT_RUN。** T3 完整 SDK bundle、T2 全部 Git/远程以及 T7 全量退役仍单独验收，不据本切片宣布 T8-code 完成。
