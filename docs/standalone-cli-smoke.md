# 独立 CLI 真实模型运行记录

日期：2026-09-23。Windows x64、PowerShell 7.6.6、.NET SDK 10.0.401，Release 普通 .NET 运行。验证目标是独立加载本地固定模型，接受用户 JSON 并返回三种 typed decision。

使用已有模型包 `.artifacts/models/laya-mmbert`，没有下载新权重。模型为 `convaiinnovations/laya-multilingual@052592a15d198d9ad47da779604259b10b47b7aa`；加载器检查固定权重和 tokenizer SHA-256，以及逐张量 hash。原始响应、完整精度数值、输入 hash、构建产物 hash 与执行参数保存在 [JSON 证据](evidence/standalone-cli-2026-09-23.json)。

## 执行与结果

先执行 `dotnet build Sezika.slnx -c Release --no-restore --nologo --disable-build-servers -m:1 -nr:false -p:UseSharedCompilation=false`，结果为 0 警告/0 错误。核心测试 `Sezika.Tests.dll` 报告 23/23，其中包含 owning session、构造失败释放、session busy 和在途请求期间 Dispose 的确定性回归。

真实推理调用构建产物 `dotnet src/Sezika.Cli/bin/Release/net10.0/Sezika.Cli.dll predict --model .artifacts/models/laya-mmbert --deadline-seconds 300`：

| 请求 | 输入方式 | Choice | billing 概率 | Score（0–1） | Boolean P(true) | tokens |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| [英文](../samples/requests/decision.en.json) | `--input samples/requests/decision.en.json` | `billing` | 0.9981719656 | 0.4970742574 | 0.7912798104 | 95 |
| [中文](../samples/requests/decision.zh.json) | `--input -`，将文件原始 UTF-8 字节写入 stdin 并关闭输入 | `billing` | 0.9999694083 | 0.3048455155 | 0.9297814520 | 119 |

两次退出码均为 0，backend 均为 `cpu-modernbert-marker-head`，每次 3 个回答、3 个 micro-batch，状态均为 `answered`、校准状态均为 `uncalibrated`。检查了输出类型、候选概率归一化、Score 和 Boolean 的数值范围。约 21.02 / 27.40 秒为本机单次进程运行耗时，包含资产校验与加载，不是稳定推理延迟或性能基准。

CLI `inspect` 对固定包完成身份/schema/固定 hash 预检查；非法 JSON 在加载模型前返回 `decision_invalid_json`，缺模型返回 `decision_model_not_installed`，两者退出码均为 2。`predict --help` 退出码为 0。

证据 JSON 的源码和构建产物 hash 对应上述 smoke 运行时版本。运行后，`inspect` 的预检查字段改名为 `assets_verified`，并为 CLI 与核心加载器增加 1 MiB manifest 上限；这些后续改动未包含在该次构建与真实模型 smoke 中。

所有验证进程有 PID、启动时间、父 PID、执行参数和墙钟上限记录；构建上限 120 秒，核心测试 60 秒，每次真实 CLI 推理外部上限 360 秒。原始本机过程记录保留在 `.artifacts/standalone-cli-20260923`，CLI 子任务的检查记录在 `.artifacts/processes`。

## 证据边界

这两条真实请求证明独立 CPU 模型加载与决策路径可用；没有执行独立上游参考实现对齐、语言准确率评测、校准拟合、CUDA typed CLI 或 Native AOT CLI 发布。S4 profile 仍是 `pending_measurement`，上述概率不是正确率承诺。后续可修改请求内容和候选后直接调用 CLI，参见 [独立使用说明](standalone-usage.md)。
