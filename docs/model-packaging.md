# 模型发布目录与安装生命周期

状态：S1-04 已完成（2026-09-23）。本文记录 Sezika 核心库提供的本地模型包生命周期；验证使用受控 loopback fixture，不下载真实模型，也不把模型权重打进 NuGet 或可执行文件。

## 目录布局

`ModelPackageStore` 接收一个受宿主控制的模型根目录，并按 `<model-id>/<revision>/` 保存安装包。例如：

```text
models/
└── convaiinnovations/
    └── laya-multilingual/
        └── 052592a15d198d9ad47da779604259b10b47b7aa/
            ├── model.json
            ├── model.safetensors
            ├── tokenizer/tokenizer.json
            └── installation.json
```

安装先写入同级的随机 `.staging-<id>` 目录，所有文件完成长度和 SHA-256 校验、`model.json` 身份校验后再移动到目标目录。下载中的文件使用目标文件名加 `.part` 后缀，因此取消、进程退出或连接错误不会让不完整文件看起来像已安装资产。替换已有 revision 时先保留随机 `.previous-<id>` 目录，提交成功后才删除旧目录。

## 断点下载边界

`ModelAssetDownloader` 以 HTTP Range 请求读取固定大小的块。最多尝试次数、块数、块大小和总超时由 `ModelDownloadOptions` 限制，调用方的 `CancellationToken` 会在每次请求、读写和 hash 块之间生效。首次连接直连失败后可进行一次（受 `MaxAttempts` 限制的）本地代理重试，默认地址为 `http://127.0.0.1:7890`；不会修改全局代理配置。服务端忽略非零偏移的 Range 请求会被拒绝，最终长度和提供的 SHA-256 必须完全匹配。

S1-04 的离线验收由 `ModelPackageLifecycleChecks` 执行：实际 `HttpClient` 连接 loopback TCP fixture，验证已有 `.part` 从断点偏移继续请求、`Content-Range` 与 SHA-256 校验、完成后删除 `.part`；另验证空文件 hash、总字节预算拒绝、安装清单逐文件 hash 和 lease 持有期间的替换/卸载拒绝。测试返回 4 项生命周期检查通过，fixture 请求、文件大小、并发 lease 和临时目录均有界。该证据验证客户端实现与本地发布目录契约，不声称任意远端服务的可用性。

安装清单 `installation.json` 使用 source-generated JSON，记录 model id、revision、相对文件路径、长度、SHA-256、安装时间和相对包路径。`ListInstalled` 只读取该清单，不把目录中缺少清单的文件夹当作已安装模型；枚举的包数和每个包的文件数均有上限。

## 加载与卸载

宿主在加载前调用 `Acquire(modelId, revision)` 获取 `ModelPackageLease`，并将 lease 与实际推理 session 同生命周期保存。只要仍有 lease，`Uninstall` 和替换安装都会返回 `decision_session_busy`；释放最后一个 lease 后才能卸载目录。缺少清单或目录会返回 `decision_model_not_installed`。卸载只删除由当前 store 解析出的包目录，并在父目录为空时删除父目录。

这些 API 管理资产和生命周期，不执行推理、不自动下载未声明的文件，也不赋予模型预测以动作执行权限。真实模型发布仍需单独提供来源、许可、模型卡、数据卡和跨平台 hash 证据。
