# T6：ModelTool 静态 JSON 合同

2026-10-13，北京时间。ModelTool 的模型清单、来源锁、tensor/layer/tokenizer/encoder/head/calibration 元数据均使用明确 DTO 和 source-generated System.Text.Json。移除项目内反射 JSON 开关；没有 blanket trim/AOT suppression，也没有改变模型、权重、tokenizer、校准状态或引用版本。

保留 schema1 的消费者字段名和类型，包括 `attention_norm:null`、tensor shape/offsets、分别管理的模型与底座许可，以及逐文件 SHA。序列化后的实际 UTF-8 字节按新工具生成身份管理；`manifest` 在已有 model.json/source_lock.json 时拒绝执行，不覆盖冻结资产。

最终源码独立 Windows Release 构建0 warning/0 error，实际执行 `serialization-check` 三项通过：独立 DOM 核消费者合同、source-generated 读写对齐、来源锁许可与资产 SHA。反射默认禁用在运行中实际检查。回执为 `.artifacts/tooling-migration-20261013-01/model-json-final-{build-result,check}.json`；执行器已登记根/子进程并核回收0。

此次检查使用明确小型合成元数据，不重下完整模型，不重算原模型推理或质量。完整 pinned 模型清单重新生成为 NOT_RUN；适用 Native AOT 发布及实际运行由 T8-code 单独验收。本子项完成不代表 T6 的校验迁移或整个工具迁移完成。
