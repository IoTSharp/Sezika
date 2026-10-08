# S4-12 独立 head 保存工程验收

2026-10-08，D:\source\Sezika，监督者独占Git。仅加固SZMH1推理head保存；没有训练、GPU、模型加载/下载、网络、AOT发布或正式数据准入。

## 实现与边界

SaveNew公开签名、v1序列化字段/payload checksum/整文件SHA及Load保持兼容；root相对HEAD独立核Load/tail及序列化/hash正文一致。CreateNew前预备owner，文件取得后以赋值登记；真实ReadWrite独占handle捕获实际prefix，写失败不使用完整预期payload冒充实物。取消不取消独立5秒回收预算；删除前核绝对路径、creation、长度/hash与非链接。已有目标保留、未知替换留路径；单异常保原始抛出帧、多错误聚合。

原owner分配顺序与fixture构造失败收口两项P2已在v2修复。检查使用本地协作式路径/元数据/字节核验，验证handle释放后Move/Delete存在目录竞态，不提供inode/file-ID或敌对目录隔离。同步≤128KiB I/O时间边界为协作检查，不能保证OS调用硬中断；进程死亡仍可能留下GUID临时文件。Scope构造未知目录保留并聚合，不宣称构造故障实测覆盖。

## 冻结与独立审查

| 文件 | 最终SHA256 |
| --- | --- |
| src/Sezika/IndependentMarkerHeadAsset.cs | 584E7211E32F11BE51BCBBB163928C2DDFC12293B10227AE86F63704FE04B4B7 |
| tests/Sezika.Tests/Program.cs | CF2231A222A019BE5E50AFD0744EDCCFDC718F761824BFD493E6E75591C92E02 |
| tests/Sezika.Tests/HeadAssetPublicationChecks.cs | 3479D24BFB2D9B65CB45901987D0750EA95C15D03B76F9BA702529D846A5AA75 |

v1冻结保留；v2冻结SHA D57D4D6CBD778EBE98A3E62A27CB95328761320DA53CD2A0D0E349DB370AB993。作者原窗口04:36:58–04:54:58、04:51:58停新，v2于04:49:32锁源；未由作者运行构建/检查。

独立复审原报告SHA 859F4A2C0900F91D9743F800861659F5DBB14CF707BAAAD49FB20D07A6552CA1，完整v1及完整v2 Core/Checks确认两P2解除。报告预写的“v2应包含字段与正常cleanup变化”已由最终消息纠正：目录creation字段与正常cleanup身份判断在v1冻结就存在，v2 Checks仅构造扩展37行；不把未冻结的更早读取与v1冻结混同。原报告字节保留，作者不追加工具篡改。最后写入晚04:51:18停新线14.119秒，在04:53:18硬截止内；父链unknown/未独立进程扫描的局限保留。

## 真实执行

固定PS7.6.6/dotnet10.0.401。root统一使用有界runner，构建禁用servers/shared compilation与节点复用，max1节点；实际只用1次build/120秒，先tiny1/30秒，再full8/60秒。

| 执行 | PID | runner秒 | 结果 |
| --- | --- | --- | --- |
| Release build | 18140 | 17.105 | 0警告/0错误 |
| --head-asset-publication-tiny | 75300 | 2.905 | 1/1 PASS |
| --head-asset-publication | 64400 | 3.088 | 8/8 PASS |

八例覆盖独立旧wire字节/hash与Load、目标冲突、预取消零创建、写后取消、真实256-byte失败prefix/原异常、真实Move+CreateNew替换保留、写入/清理双异常及零清理预算留路径。最后由fixture独立核已登记对象回收，非递归目录删除；tiny/full专属TEMP/TMP根均0项并已删除。零步骤H2仅证明旧格式兼容，不是新诊断CLI/训练/loss证据。

三实际DLL在build/tiny/full身份不变：Tests187904 bytes/SHA147671928BDF85AA286797E92E4BB3B05C12E944CF63D8EA2EECF340B5DA79A1；Sezika698368 bytes/SHA9553EF29B7708DC1F73AEA431BDF7754254826E230D6308375C303005E0E3124；IndependentModelTool478208 bytes/SHA8ACD90F0C73A62C9A3BDB1528B5ED3AADAD4413A594498F3F4A86BC85C9D7655。

## 资源与接续

三runner回执全部Succeeded/exit0/cleanup为空，root终核18条已记录身份无匹配start存活，包含重复launcher；额外短reader的完整argv/父链不足及快进程可能漏采样如实保留，不宣称全机完整零残留。没有新增服务、GPU进程、下载或独占MCP。

证据根 `.artifacts/head-asset-publication-20261008/`：source-freeze[-v2].json、source-snapshots[-v2].json、independent-review.md、root-{build1,tiny,full}-execution.json、各原始runner日志/receipt及root-final-validation.json。下一独立诊断格式/专用loader、资产CLI和CUDA画像分别验收；正式许可、人审/封存、可信上游数值、真实训练收益/质量/校准、trained-head两RID及发布门槛仍开放。Sezika仅commit，不push/发布/部署；用户微信两文件保留。

首Git收口尝试在stage PID75372遇到Process snapshot timeout，receipt Failed/ExitCode=null/cleanup为空；原stage退出码unknown，不改写为命令成功。随后只读核HEAD仍819ada0、八份批准文件已经完整暂存，79864/70796/75372三PID当前absent，未执行commit。恢复使用新日志根、仅接纳这八项完整暂存集合、相同源hash和已支持的有界5秒CIM查询上限；不跳过进程核验，不重跑构建/检查。原失败保留。
