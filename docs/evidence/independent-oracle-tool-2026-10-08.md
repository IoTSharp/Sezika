# 固定上游 oracle 导出工具工程证据

本次实现开发期离线 Python 工具，从原始 mmBERT 权重在未修改的上游 ModernBertModel 上导出25个层级checkpoint与marker。Python/PyTorch不进入C#核心或Native AOT运行时。**没有安装依赖、执行真实上游forward或取得数值parity结论**；真实执行仍缺NumPy并等待任务目录安装授权。

## 冻结范围

复用真实C# `independent-smoke-46-v1` capture，46 tokens、markers25/35；只读取实现输入的request/I32及267项metadata，不读取C#实际checkpoint或marker值制作reference。合同SHA-256为 `F709D760722E9FB4D3DCC8C609E59C849BFF39F8E9693AEF7B745793155776A5`；输入、25+marker选择及26项容差在真实测量前冻结，见[比较器证据](independent-numeric-compare-2026-10-08.md)。

固定源 `jhu-clsp/mmBERT-base@c5955035435e2bf121cde7f3c8863ef52ff35d82`，原权重1,231,188,142 bytes/SHA `8ea64ec1ea4eb8fca0fc14b69a2ae571de6bfbc25fd214bb932dd4aba6a3a04e`。计划路径为 `torch.load(weights_only=True,mmap=True)`、134个原生model.* CPU F32 tensor、严格未修改vendor state加载、eager/eval/4线程/noautocast/nocompile。原权重/config/tokenizer及输入保持只读lease；只有hash/bytes及严格state加载通过后original_weights_verified才为true。此真实模型路径尚未运行。

候选执行环境为Python3.14.3/Torch2.12.0+cpu/Transformers5.13.1/NumPy2.3.3。前三者存在，但NumPy缺失，Transformers导入失败；requirements只列版本，未提供全部wheel或transitive hash lock。已核ModernBERT源码SHA `1C03E59EBA7B59D206F12BC40B7030BF44C5D0E2686CF9F7AD849B6ACB6A38E7`。

## 实现与复核

CLI对外供合同及上游源码hash、模型/input身份、JSON重复/非有限值/深度、I32布局、输出F32形状/finite及顺序实施门禁；进程1 case/46 tokens/25checkpoint、180秒、观测resident6GiB、输出20MiB。native import/forward的外部总时限由Invoke-BoundedProcess负责，内部采样不是硬内存限制。

正式capture/无覆盖目录发布限Windows。唯一UUID partial先写exclusive文件并flush/fsync，Move前后核预算/取消，失败只按目录/file ID/SHA归属回滚、移除本次complete标记并保留partial/具体失败；初始I32/request写入也在异常路径内。外部强制kill可能跳过finally；没有合格complete参考不能通过比较器。

作者及只读子审已修复初始写入异常路径、Move后取消、整数/JSON溢出、path ancestry最多64/5秒、progress与fixture清理归属。root终审另发现先import再lease/hash的源码窗口，另开最多5分钟/3工具文件的修复阶段：首次backend import前通过distribution定位源码、先lease/hash，拒绝已加载backend，设置随机不存在的sys.pycache_prefix并禁写bytecode；import后核标准SourceFileLoader、实际源码路径及cache仍未创建，lease保持到捕获结束。root复核修改及新tiny/full回执；传递依赖、native库及父目录变更仍未全面hash-lock，不能扩大为整环境原子身份。

最终 `export_oracle.py` SHA-256为 `118E4CA7BB626F36BEEB009047A7AA42DB9E8A72AD64130F274A818F4766D39E`，README SHA为 `3C0F8045D6B73273AE99E2CFCBCADD7938534F7B296DC3EC92F0AD56C9795F1E`；真实capture时仍须外部重新核程序/合同身份。

## 标准库验证与资源

工具实现UTC20:57:14–21:17:14，3个工具文件，最终原阶段交付UTC21:14:10。固定自检解释器 `C:\Users\mysti\AppData\Roaming\uv\python\cpython-3.12-windows-x86_64-none\python.exe` 3.12.13，仅用标准库，不import backend；PowerShell7.6.6。tiny1再full16，外部30秒/内部20秒，日志 `.artifacts/independent-oracle-20261008/logs/`。

| 原阶段过程 | PID | 结果 |
| --- | ---: | --- |
| tiny1 | 99128 | exit0 |
| full16 | 70792 | exit0 |
| 审查后tiny1 | 52624 | exit0 |
| 审查后full16 | 86880 | exit0 |
| 最终JSON/程序lease修复full16 | 2584 | exit0、3.661秒 |
| 源码窗口修复tiny1 | 18296 | exit0、3.439秒 |
| 源码窗口修复full16 | 47240 | exit0、2.622秒 |

最终16项覆盖duplicate JSON、NaN/Inf/合法1e400溢出、非法整数、布局/path逃逸、checkpoint顺序、F32/预算、发布前取消、实际Move后取消回滚及临时文件归属，并含pre-import真实源文件lease拒绝写入、错hash/path及bytecode前缀保护。全部fixture_only=true、model_imported=false；result清理为空。root单次有界核原5个及最新2个PID不存在，另核9个准备/metadata/首次commit PID不存在；最终脚本SHA与作者一致。owned fixture已清理、隔离pycache前缀未创建，日志与失败保留；没有按名终止进程或清共享缓存。

准备阶段最后import探测实际7.376秒，略超UTC20:56:03截止；事实保留，不能称准备窗口严格通过。准备的 `py list` 意外自动把Python install manager更新至26.3，未主动请求install/download；没有未经授权回滚，后续仅直调固定解释器。原日志及readiness记录在 `.artifacts/independent-oracle-20261008/head-readiness.md`。

## 未关闭门槛

须取得依赖安装授权、核实际环境后，再另开单case真实reference与独立C#26项比较窗口。原模型/Tokenizer发布许可、全部267算子、独立tokenizer、长输入local窗、真实语言质量、训练、校准、AOT、性能与发布均不由本工具工程关闭。正式训练和批量教师/特征仍必须GPU。
