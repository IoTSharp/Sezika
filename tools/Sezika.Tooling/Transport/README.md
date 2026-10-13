# 资产运输与发布准备

本模块使用 C#/.NET 10 的文件、HTTP 和静态 JSON 接口，不启动解释器或进程。共享 CLI 的入口由主调度接线。

- `DownloadAsync` 每次要求可信完整长度与 SHA，使用 206 和精确 Content-Range；独占 partial 与恢复清单绑定原 URL、长度、SHA 与强 ETag。断线保留部分文件，完成后重读落盘字节，已有完成目标拒绝覆盖。GET 网络失败才逐次尝试本机代理。HTTPS 重定向仅接受明确平台存储域名，跨主机不转发 Bearer。
- `Prepare` / `VerifyPhysical` / `Reassemble` 与旧 full-corpus transport 的 snake_case 清单兼容。物理分块有独立 byte/SHA，mapping 精确覆盖逻辑文件，重组后再读原逻辑 SHA。每文件 64 GiB、总量 256 GiB、最多 4,096 文件、128 parts，fixture 与生产身份分开。失败保留专属输出，成功不覆盖已有目录。
- `PreparePublication` 迁移完整语料发布检验：冻结计划、来源/输入 SHA、更新分母、独立新验证进程、四文件保存点、完整 FP32 encoder/head 与 AdamW first/second 张量、来源许可通知、长度与 split、所有 Choice 旋转。接受旧执行 TSV 与 C# ExecutionResult 成功/完整回收证明。此准备包含私有来源与逐条验证记录，公开预览拒绝它；质量、校准、AOT 与性能保持未接受。
- `Preview` 校验精确白名单和平台单对象上限；public model JSON 逐字段拒绝私人记录与秘密，private dataset 没有 public 镜像入口。授权对象绑定预览 SHA、目标库、准确父 ref、visibility、批准人及时间。这里的授权对象只承载调用者已获的明确人工授权，不能替代人工确认。
- `UploadLfsObjectAsync` 实现 HF / MoArk 标准 LFS basic 与 HF multipart，零自动写重试，完成后另查 LFS server presence。`CommitHuggingFaceAsync` 使用 parentCommit 的 NDJSON 协议，先核 ref 与 visibility。MoArk 的 ref 更新由受控普通 Git/LFS 调度负责。对象存在、ref 发布、认证完整读回与匿名完整读回分别报告。

API 凭据和签名 URL 仅在请求内存中使用，不进入 manifest 或回执。回环测试使用 TcpListener 单并发、最多 32 个请求与 20 秒期限。外部真实上传、发布和公开可见性验收本轮均为 **NOT_RUN**。

原始合同来源：`.artifacts/repository-training-moark-full-20261013-001/{publisher,transport}`、既有 `Sezika.ModelTool`、HF basic/multipart 发布助手和项目技能 `references/local-mirror.md`。旧助手留作历史证据；本模块不运行它们。
