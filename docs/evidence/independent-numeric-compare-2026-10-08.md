# 独立 mmBERT 数值比较器工程验收

本次完成独立 C# / .NET 10 比较器与测量前冻结合同。最终构建零警告/错误，先1项、再16个合成case/19个行为变体通过。**没有执行真实上游模型或取得参考数值**；全部合成输出为fixture_only，accepted_independent_reference=false。S4-15真实独立数值、质量、训练、AOT与性能验收继续开放。

## 合同与来源

复用已验收真实C#捕获 `independent-smoke-46-v1`：46 tokens、markers25/35、267项metadata。比较器只读取raw/norm embedding、22层hidden、encoder final的25个张量及marker，共26项、884,736个元素；其余242个算子点没有独立上游对比。

合同 `model-manifests/mmbert-independent/oracle-contract-v1.json` SHA-256为 `F709D760722E9FB4D3DCC8C609E59C849BFF39F8E9693AEF7B745793155776A5`。输入、模型、源码和误差门槛在任何参考数值测量前冻结；raw逐元素精确、embedding norm绝对/相对1e-5，layer/final/marker绝对1e-3、相对1e-4；公式为 `abs(actual-reference) <= absolute + relative * abs(reference)`。v1不接受增大容差。

初始环境建议采用历史Python3.12.10/Torch2.8.0/Transformers4.57.1；只读核查发现该环境缺失，故在测量前改为实存Python3.14.3/Torch2.12.0/Transformers5.13.1，NumPy2.3.3尚未安装。初始合同快照及环境失败保留在 `.artifacts/independent-oracle-20261008/`。已安装ModernBERT源码SHA `1C03E59EBA7B59D206F12BC40B7030BF44C5D0E2686CF9F7AD849B6ACB6A38E7` 已绑定到合同；此身份核查不表示该模块已能执行。标准库/编译自检不需要NumPy，真实参考仍待依赖安装授权与实际运行。

## 实现与审查修复

新工具 `tools/Sezika.IndependentNumericCompare` 已接solution，不引用core、权重或legacy决策JSON，不运行模型。source-generated typed JSON先拒绝重复属性、显式null、超过1MiB/32深度/20000节点输入；核模型/配置/请求快照、整数数组与I32 bytes、267项metadata顺序/shape、参考25项及两侧marker自己的final行。selected F32严格核bytes/hash/finite，每端不超过4MiB，使用只读句柄读取实际被hash的数据。

监督者外供合同、oracle脚本和上游源码hash；源码还必须匹配合同的冻结SHA，不能仅让文件和自报metadata互相一致。真实参考必须original_weights_verified=true；合成输入为false，并且正式compare拒绝fixture。两侧根及selected路径禁止重合，避免直接复用actual文件进行自比；文件复制/环境声明的真实性还需监督者核对真实执行回执，路径分离本身不证明来源独立。

比较结果保留全部26项和逐项失败元素计数，数值失败不丢分母；相对误差报告使用分母 `max(abs(reference),1e-12)`，该诊断规则不改变验收公式。报告写唯一同级partial，Move(false)不覆盖；写入或取消失败后最多5秒，仅核创建时间、字节及已登记路径的本任务文件回收，未知修改保留。

只读子审6文件/2轮识别源码合同绑定、直接自比、输出中断回收及null/非法整数拒绝缺口；作者修复后重新构建和tiny/full。root复核最终代码、真实构建日志、4份runner回执与合成报告，没有重跑已通过完整测试。源码来源与人工数据准入不因工具工程通过而改变。

## 有界执行

PowerShell7.6.6、.NET SDK10.0.401，均经 `Invoke-BoundedProcess.ps1`；本工具没有子进程、服务、GPU/HTTP或真实模型调用。实现阶段UTC20:54:00–21:14:00，最多7文件/16case；19变体如实单列，每组最多3项，共用60秒内部预算。

| 过程 | 根PID | runner结果 | 墙钟约秒 |
| --- | ---: | --- | ---: |
| 初次build | 77652 | exit0/零警告错误 | 9.47 |
| 修复后build | 60764 | exit0/零警告错误 | 8.68 |
| tiny1 | 31192 | 1项通过、fixture_only | 3.91 |
| full16 | 34744 | 16case/19变体通过、fixture_only | 4.90 |

完整自检覆盖合成通过但不准入、26项全数值失败/全部884,736元素保留、模型/marker/token/shape/缺项、duplicate name/JSON、NaN/Inf、raw hash、路径逃逸、合同hash、与冻结合同不符的上游源码hash、null、同目录自比、已有报告保护和正式模式拒绝fixture。

最终DLL SHA-256 `FB03F57F1F67A24C51BE9AC8A1D1011B963A7EECC6ABD4BC8AEBF5D01E7AA5E1`。日志在 `.artifacts/independent-oracle-20261008/compare-processes/`；tiny/full在 `compare-self-test-tiny-1`、`compare-self-test-full-16`，合成数据和完整报告保留，不把它们当真实tensor。

四份runner均Succeeded、cleanup为空。作者在UTC21:11:13冻结6个源文件，未超过21:14截止；`compare-processes/resource-closure.json`记录8个登记PID均不存在、owned partial为空及编译response文件已消失。root提交前单次有界复核这8个PID均不存在，并核DLL/合同SHA与上述身份一致；没有重复完整回归。full进程CIM命令行曾为null，runner argv/runtime command已保留，短寿命进程观测限制如实记录。CPU范围仅合成测试/离线比较，正式训练与批量特征按GPU规则。参考Python exporter为另一任务，未在本工程验收中宣称其真实模型路径通过。
