# 本批文章的 C# 工具源码

这里保存 2026 年 10 月 13 日编写文章、生成配图和核对发布结果时实际使用的 C# / .NET 10 文件应用。原始执行记录保留在本机 `.artifacts/articles-20261013/`，不随文章提交浏览器会话、凭据配置或临时文件。

| 文件 | 用途 |
| --- | --- |
| `ArticleAssets.cs` | 绘制正文示意图，合成带中文标题的封面，生成 HTML 与文章清单 |
| `GenerateCover.cs` | 调用已配置的图片生成接口，一次生成一张封面原图 |
| `PlatformRead.cs` | 读取博客园配置，执行受限的账户、文章或公开页面检查 |
| `VerifyPublic.cs` | 检查公开 HTML 中的标题、介绍段与图片引用，保存每项真实结果 |
| `OsChinaReview.cs` | 从 Windows 凭据管理器读取既有会话，核对账户后为受阻的原文章申请复审 |

这些源码固定了本批文章目录和当时的本机配置位置。需要在其他环境复用时，应先核对目录与账号。配图生成需要当前进程中的 `OPENAI_API_KEY`；源码不保存密钥。复审程序使用既有 Windows 凭据，不输出 Cookie。

可使用 .NET 10 SDK 编译文件应用；`ArticleAssets.cs` 的绘图部分需要 Windows。

```text
dotnet build ArticleAssets.cs -c Release --disable-build-servers
dotnet run --file ArticleAssets.cs -- diagrams
```

运行 `GenerateCover.cs` 会调用外部图片接口；运行 `OsChinaReview.cs` 会提交平台复审申请。执行前需要明确本次目标与写入范围，并使用项目规定的有界执行方式。

`VerifyPublic.cs` 检查的是服务器直接返回的 HTML。开源中国页面采用客户端渲染，本批另通过真实浏览器核对公开正文和图片，结果见上级目录的 `oschina-public-verification.json`。静态 HTML 检查没有通过，不能据此断言文章未发布或重发原文章。

截至本批保存时：博客园 8 篇已公开，开源中国 5 篇已公开、3 篇已申请人工复审，微信公众号 8 篇草稿已保存。上述状态不是重新运行发布的授权。
