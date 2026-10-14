# Native Git 的本地预览与仅提交

`GitRequest.Operation` 新增 `preview`、`commit-only`。已有 `clone`、`readback`、`publish` 的默认行为和位置构造参数保持；新增成员均可选。当前改动只完成源码与静态审查，构建、source-generated JSON、Native AOT 和真实 fixture 由 Root 在新冻结树验证，均不能沿用旧树结果。

`preview` 核对所属文件的完整字节/SHA、全部过滤器、干净 index、确切本地父提交和远端分支快照。输出必须位于仓库外；进程和预览回执写入该新输出根。它禁用 Git 的可选锁刷新，不 stage、commit、push、fetch、hydrate，不改属性文件或对象库。预览结果仅是准入证据。

`commit-only` 先完成同样的全部预检，再以无过滤器的原始 Git blob 或规范 LFS pointer 暂存精确所属路径，核暂存对象、父提交及提交树。它只创建本地提交，不 push、不调用 LFS pre-push、不上传对象。回执的 `LocalCommit` 保存本地提交；`PublishedCommit` 保持 null。`IndependentReadback` 仍为 `FULL_FRESH_CONTENT_READBACK_NOT_RUN`，`ModelQuality` 仍为 `NOT_ACCEPTED`。

两个新模式的所属路径使用正斜杠和有限 ASCII 字母、数字、`_ . / -`，长度不超过512；不接受 Git 元数据或直接把 `.gitattributes` 当普通 payload。所有过滤器必须在第一次仓库写入之前通过；大于等于10MiB的文件必须已有或明确计划使用 LFS。总文件数、native 调用数、字节、预算和取消均有限制，达到边界会保留失败证据。

可选 `AttributeChange` 使用 `GitAttributeAppend`：

```json
{
  "BeforeSha256": "<64 lowercase hex>",
  "AfterSha256": "<64 lowercase hex>",
  "LfsPaths": ["runs/example/weights.safetensors"],
  "VerbatimPaths": ["runs/example/report.json"]
}
```

属性文件须已存在，HEAD、index、worktree 原始字节相同，且为普通100644/stage-zero条目。追加路径必须与所属文件精确匹配、去重且两组互斥；该 root 属性追加合同拒绝所属文件祖先中的嵌套 `.gitattributes`，避免后者覆盖计划规则。所有所属文件的当前 worktree/index 过滤器还须一致。保留旧字节并追加一个 LF，再逐条追加 `path filter=lfs diff=lfs merge=lfs -text` 或 `path -text` 及 LF；after 字节必须匹配 AfterSha256。预览只构造和验证这些字节；仅提交模式才应用。创建全新属性文件、重排/删除旧规则及任意属性编辑不在该合同内。

提交后会核远端仍是原快照；网络未知或并发变化不能成为“远端未变”的成功证据。写入开始后失败会保留本地属性/index/提交和 `partial-local-state.json`，不 reset、stash、clean 或自动回滚。仅清理本请求成功创建、尚未应用的确切属性临时文件。

这是一层通用 Git 能力。run/kind、独立业务验收、冻结清单及发布授权仍须上层准入；新增操作不能自动授权发布模型或数据，也不代表旧发布脚本已完整替代。外部 CLI 接线和新树总装由 Root 负责。

`NativeGitChecks.cs` 保留原1–8例，9–11覆盖只读快照、仅本地提交及预检拒绝；case10现含真实 N20/N128 文件请求和合成认证负控。声明仍11例；已知完整操作分母18，有限操作上限20、组合 native/CGI 上限600、仓库fixture上限7、总墙钟240秒。单operation仍最多160次native Git调用。可分区运行，单例或资源审计不能写成全11例或平台业务通过。Windows 与 Ubuntu WSL 的 v17 最终回环均已通过：每平台13个实际客户端覆盖11类检查，包括case10的三个独立场景；实际原始回执、完整分母与历史失败见[最终验收](../../../docs/evidence/tooling-final-v17-2026-10-15/native-git.md)。真实托管平台仍 NOT_RUN。

## 批量原生调用预算

每个请求在任何 Git 写入前输出 `native-call-plan.json`；预测超过160直接拒绝。属性使用 NUL stdin 查询，index/tree 查询每块最多64路径且20,000字符；LFS指针按 OID 用 `cat-file --batch` 读取，写对象用 `hash-object --stdin-paths`，暂存用一次 `update-index -z --index-info`。普通 publish 文件仍使用批量 native add，保留既有 text/EOL 行为。

令 C 为所属路径查询块数，S 为含可选属性文件后的查询块数，A 为是否有属性追加（0/1）。preview 最坏预算为 `11+3A+C`，commit-only为 `21+4A+C+2S`，publish为`14+C`，clone为`10+ceil(N/16)+2C`，readback为`8+2C`。短路径 N20带属性为28，N128带属性为33；这些是静态最坏预算，运行回执须独立核对。超过路径/字符等合同而使预算过界仍在写入前拒绝，不盲升160。

## 合成 stdin 认证

GitRequest 可显式选择 `CredentialSource=stdin` 和 `AuthenticationProvider=moark|huggingface|bearer`。选择stdin且未提供Stream时读取Console，fixture重载传明确MemoryStream，统一复用HttpCredentials的16KiB/18块/10秒、重复JSON及取消合同。旧auto+CredentialTarget保留Windows Basic语义，不默默变Bearer；冲突来源拒绝。

认证只在精确remote HTTP作用域的原生ENV配置传递；fixture的显式LFS服务还须同源。秘密不放argv、Git配置文件、请求或broker JSON、回执与原始流；broker配置没有Environment字段。字节缓冲清零，.NET字符串副本不声称全部擦除。合成fixture在服务内核真实Authorization，只计数、不保存值；跨域LFS action负控断言服务秘密未转发并保留失败。该合成负控在两平台 v17 回环已实际通过，认证仅在固定同源/跨域场景范围成立；不能由它宣称任意远端平台策略已验收。
