# Head 扩样容量工程验证

日期：2026-10-09，Asia/Shanghai。本项只完成容量及存储合同工程，不表示新256题已训练或质量通过。

用户已授权从原32题扩大公开样本并继续同一教师。共享C# head容量统一为最多256题、24576次更新；CUDA特征上传和完整CE遍历使用同一题数上限。训练按实际题数分配特征缓冲，清理上限同步为题数加7，旧调用默认39缓冲保持。checkpoint与head保存、加载均接受新的更新上限，仍拒绝超限及无效身份。

验证实物位于 `.artifacts/teacher-expansion-20261008/`：

- 既有核心247检查通过，实际进程exit0；固定tokenizer oracle因对应测试路径缺资产仍跳过，不算oracle通过。回执 `process-logs/20261008-113651-887-core-existing-regression.result.json`。
- 单个H4合成题CPU小参考完成24576次更新及checkpoint/head磁盘往返；257题和24577步拒绝。保存物为 `capacity-cpu-reference-fixed/cpu-boundary.{asset,checkpoint}`，每项450字节；源码 `capacity-check/Program.cs`。这不是数据集训练。
- 五项源码hash冻结于 `source-freeze.json`；与 `.artifacts/expansion-engineering-20261008/core-api-review.md` 的独立审查对应。Linux容量验证工具已构建，实际GPU容量检查尚未执行。

GPU新增上限、真实256题训练、正式金标质量、许可发布、校准、封存、新head AOT及性能各自另验。未重新运行旧72步或768步实验。
