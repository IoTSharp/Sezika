# CUDA 原生资源取得与登记加固证据

2026-10-08，基线 `2800cbe`。独立工程窗口为 UTC 22:50:30–23:15:30，实际于 23:06:18 收口。修改五个源码文件；未修改 PTX、正式数据或模型。

## 变更与审查

CudaDevice 的 buffer、module、context 与 profiling event 路径在 native 调用前准备空 wrapper、集合容量、checked 计数和 pending 归属。成功后立即执行无需分配的 adopt/commit，资源摘要排除 pending；异常仍保留原对象和原抛出帧。rollback release 返回失败时保留 owner，以 context 最终回收兜底。caller 必须串行调用设备操作、snapshot 和 Dispose；没有并发 Dispose 支持声明。

release 先保存 primitive CUresult、operation 和失败计数，再尝试诊断对象分配。清空旧缓存异常，避免诊断分配失败后误报上一次 release 的 operation/code；新增回归覆盖先 module code 2、后 buffer code 1 与诊断 OOM 的次序。内部单次 fault 默认关闭，没有新增公共 fault API。

作者独立审查完整读三核心文件，零未解决发现；监督者补读测试和最后缓存回归。五个最终源码 hash 与 UTC 23:02:45 freeze 匹配。监督者另于 UTC 23:15:58 核四个实际运行 DLL hash，全部匹配作者记录。

## 实际验证

- 三次构建，最终零警告、零错误。
- CPU tiny 1 / full 7，共八合同、九断言；GPU tiny 1 / full 11，共十二执行 case、136 断言。
- 既有 GPU 训练 tiny 21 断言，清理回归 tiny 1 / full 8，共九 case、96 断言，均通过。
- 自然失败实际返回：超大 MemAlloc 为 CUresult 2，非法 PTX 为 200；不是注入的 native 返回码。
- fault 后 pending 为零。case 10 在 context 回收前保留 4 bytes、一个 buffer、一个 module、两次 release failure，最终报告最新 Buffer/code 1。case 11 保留一个 module、一个 event、两次失败；context 实际销毁后归零。
- `context_pending` 在创建 context 前失败，DestroySucceeded=false；其他已创建 context 的最终销毁成功。最终 bytes/buffers/modules/events 全零，不抹去历史 release 错误。

fault 使用预建异常，验证 managed 异常路径及真实 native 资源归属，未制造真实 heap OOM 或 native hang。Driver 调用不可抢占，本任务不证明任意 hang 的进程内恢复。

## 身份与资源收口

作者正式证据：`.artifacts/cuda-adoption-20261008/completion-evidence.json`，SHA-256 `8C16358EE6DF01A7D1712947DAF125402CD16992F6408F984F91012FFC24CA35`。源码冻结为同目录 `sourcefreeze.json`。

| 源码 | SHA-256 |
| --- | --- |
| CudaDevice.cs | `8CD72F697E1FF6A2A8E9AD78C6A8164A82569297465ED1F945AE9D0FE4E067C2` |
| CudaHandles.cs | `3B8DB264F802CFF032C6B2B1A26623D5841B218831957DEFC82ED97D7EAFE707` |
| ResourceAdoption.cs | `990AE23B1924EBF3C62468E4AF9810A0CD005D9806DC0E959E896B9B326DD24C` |
| Program.cs | `44AA06502038E9F08AB7C85BCAF2BE99008F04FF59812CB0F89A1F082F47EA64` |
| IndependentCudaAdoptionChecks.cs | `CFCCB61DB107A7194C5C9580922F67483B3AB70B95402C5548E1E4B78A8AC438` |

实际测试 DLL SHA-256 `644ADFCD07AD2E5B3E2F6F5510A34665C666D7385B54EC5A73C5E9112E337EDC`；Cuda DLL `7FEA22173FD98A5843EA8A2399D75EDA26B1E04BC2538EAE77C2F1F7A8D276D1`。另外两个依赖 DLL 见监督者 `root-processes/binary-identity.json`。

使用 PowerShell 7.6.6、.NET SDK 10.0.401 和既有 bounded runner。作者十九登记身份全部退出，专属 temp 核绝对路径和 reparse 后删除；三个 workload 日志转存保留。监督者 tiny/full 回执审计于 UTC 23:11:34 检查十一结果、37 个身份，无匹配存活，所有 runner 成功且 cleanup 为空；两个监督审计 PID 102028/71208 后核不存在。未创建服务、下载或独占 MCP，GPU 已释放。Windows 快照可能漏过极短中间进程，保留这一观测限制。

本项关闭原生取得后的包装/登记工程缺口，不关闭 Native AOT、正式训练、encoder 反传、真实上游数值、语言质量、校准或性能门槛；新源码的训练 Native AOT 另行验收。
