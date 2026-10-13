# 全局更新上限：独立源码合同修正

日期：2026-10-14。本项是工具迁移前的必要训练合同依赖，独立于未验收的GPU性能优化提交。

`FullEncoderUpdateLimit` 将完整encoder训练的整数游标显式约束为0..800,000,000。ApplyAdamW先验证当前游标小于最终上限，再取得安全的下一整数；恢复允许完整结束点800,000,000，拒绝负数、超界及long溢出。旧1,000,000硬上限不再截断14,592,720更新的不可变全局计划。

四个候选文件共47,811 bytes：新纯整数类、原训练器两处guard/next接线、27项纯CPU检查，以及在CudaDevice.Open之前的检查入口。训练器基于HEAD，只加入上述7行差异；保留原构造器、旧PTX/ABI、原GPU算子和同步策略，未夹带working tree的tiled/AdamWChecked/workspace/优化构造器等改动。受保护的工作区原始源码保持原字节。

独立索引导出目录 `.artifacts/tooling-migration-20261013-01/limit-cpu-candidate/source` 实际Release构建零warning/error。tiny 2项、完整27项均PASS；真实进程退出0、完整子树回收0。每个实际编译文件的raw bytes/SHA256/Git blob与索引一致，清单为候选目录 `compiled-source-manifest.json`；原始回执为 `build-result.json`、`tiny-result.json`、`full-result.json`。

检查覆盖0、1、999,999、1,000,000、1,000,001、14,592,719/720、799,999,999/800,000,000、负数、long.MinValue/MaxValue以及Next/Restore边界的不同语义。纯CPU分支在打开CUDA设备前返回，输出明确 `actual_gpu_training=false; optimizer_restore=NOT_RUN`。

本项证明整数合同和源码构建。真实跨百万步训练、optimizer数据加载与数值恢复、GPU profile/save/resume、全量覆盖、性能、质量和校准均未执行；不能将27项整数检查写成真实训练或GPU优化通过。
