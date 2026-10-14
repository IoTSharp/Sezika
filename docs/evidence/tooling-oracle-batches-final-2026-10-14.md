# T6 Oracle 调度的分项编码验收

2026-10-14。本项实现冻结plan顺序的参考→实际→比较调度、完整程序/源码/资产SHA与原生回执准入、数值差异继续、同任务恢复、失败/取消保留未处理分母，以及评分用独立index。生产实际路径固定CUDA；本次未启动该GPU路径。公共CLI不能选择测试注入的模拟执行器。

实际验收来自`tooling-final-combined-candidate-v1`：92文件manifest SHA256 `7123d23ce6b0993456e892e03a6e667550d88a7ac78581c6f4cab8356b07367c`，完整21运行文件。Windows FDD及Linux Native AOT分别先tiny1再full5，最终全部通过；所有模拟捕获、进程/批次回执与index均明确simulation-only，不用这些数值证明模型、独立参考、质量、校准、AOT模型推理或性能。

五个检查覆盖真实生产方法中的固定三阶段顺序、数值差异后继续、同任务完整恢复不重跑阶段、修改冻结输出时拒绝恢复、实际输出EOF不完整时不比较及不处理后续批次、取消时不启动阶段。注入的是明确内部协议fixture，其伪进程回执只标SIMULATION，不能充当原生资源证据；外层真实测试进程另行受native executor监督。

Linux初次/mnt/c完整5项在共享30秒预算内完成首轮2batch后，恢复检查触发取消，原native FAIL/134、SIMULATION_CANCELLED和后续未处理保留。唯一受控重跑使用同92源/21runtime/30秒预算，只将fixture输出移至新的Ubuntu ext4短目录，full5通过（suite0.146秒、native约0.867秒）。这是控制条件与结果，未证明具体IO因果，也没有增大生产时限或掩盖原失败。

最终验收摘要SHA256 `5616e8ba4c88c94b22d7c16d56b2be0ca688254e174e394dc79d78900b34ee3b`。组合5批资源审计的Linux16回执73身份和Windows30回执110身份全部缺失/复用、live0/unknown0；不是全部属于本套件。101个ext4文件120770B先逐SHA复制归档，再仅清理本任务准确路径。源码/运行文件前后保持不变。

Root提交四实现源、专属检查、模块说明及准确共享partial，保留已提交T3、Validation、Git、Windows和退役检查器。C#新参考生成和实际单条数值对齐有独立证据；本模拟切片不能代替它们。现场GPU批次、完整真实模型批次、正式训练和模型/数据发布保持NOT_RUN，全部迁移仍按根ROADMAP继续。

原始证据根：`C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\tooling-final-combined-candidate-v1`。Root静态独审文件`root-static-independent-review.json` SHA256 `78c5a8e3d6d080b2345315254f945f376ece4f8d0f65c6f81161aa1b30ec498c`；原失败与受控ext4证据分别保留，不在旧路径重跑。