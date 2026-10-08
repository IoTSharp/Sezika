# 独立未训练诊断head资产工程证据

本批只验收资产存储。`SZDH1/schema1` 与正式 `SZMH1` head及 `SZMK1` checkpoint分别解析。新身份仅绑定model、encoder、tokenizer、hidden size、decision-v1和strict长度策略；没有数据manifest、feature cache或训练state。Origin固定 `UntrainedDiagnostic`，CompletedUpdates固定0，ObservedLoss为null，不能用作训练收益或质量证明。

## 实现与审查

`DiagnosticHeadAssetStore` 使用域 `sezika/diagnostic-head-initialization/v1`、算法 `xorshift32-f32-v1`。描述符采用canonical UTF8/LE并绑定seed和身份。loader核magic、schema、provenance、checksum、整文件SHA、描述符及shape/finite，重新生成全部权重并逐F32位模式比较。限hidden1–2048、6144权重、128KiB文件、30秒协作预算与取消。完整返回对象在发布文件前准备。

原publisher仅提取至 `HeadAssetFilePublisher`。旧序列化/hash/Load未改，机械逆变换恢复原Core SHA `584E7211E32F11BE51BCBBB163928C2DDFC12293B10227AE86F63704FE04B4B7`。延续CreateNew前准备owner、实际prefix捕获与独立5秒清理；未知替换保留路径。它是协作式路径/creation/length/hash核验，没有inode/file-ID或敌对目录竞态隔离。

作者UTC05:01:04–05:13:26，9调用/7明确源，无build/test/GPU；05:12:29锁源。早审SHA `23A69D03C7CB59FDB26CBE5678A4668DA0255012FE2804C286794FB7355D0E6B`，最终checks独立审查SHA `19DC3BD6EAA5EBE866ABC503E0E863659CAF2AD32F17D42209C589804DF24AD9`，均无P1/P2。源码冻结与快照分别为 `1FA9043EF2622B1DCD42119B27B1B42F122BCE339F565D50DA2A8ECB6F0C82CA`、`9332F6703664409CFD00394D7CF6017CA5E3B7A462531FDA9C1F3D7401D1C371`。

| 冻结源码 | SHA256 |
| --- | --- |
| IndependentMarkerHeadAsset.cs | BC4D16E951C0C3C03E564E907FEDE451BD6ADC5B2902386841D225DEB10514E3 |
| HeadAssetFilePublisher.cs | A8B338A83D6501BB5259CF4E2CA2EF8833F958E0EADA9BED826FC9DF13A18EFA |
| DiagnosticHeadAsset.cs | 54F322DBF9925B7737FAA861519CC607CE47559B6F8310D5195314963CCAC9E9 |
| Tests Program.cs | 2A878AE493B7F07FF411B17A4AC492D7A6C05E04D85D44C8266E3B36CEAFFD49 |
| DiagnosticHeadAssetChecks.cs | B74ACE858F34329F4E8F17DF9150785476A00EF42C4DB7F04F92A5317BB8E79A |

只读旧publication checks SHA `3479D24BFB2D9B65CB45901987D0750EA95C15D03B76F9BA702529D846A5AA75`，trainer SHA `A2CA5828F09E13B78E37ABF34050A23F0AC4FFEDAA79A2B55125A1317801E96C`。

## 实际验证

固定PS7.6.6/dotnet10.0.401。root有界runner执行一次Release build（no restore、禁build servers、m:1、禁共享编译/节点复用），专属output与TEMP/TMP，每阶段新日志，不重复历史build。

| 执行 | PID | runner秒 | 实际结果 |
| --- | --- | --- | --- |
| build1 | 76260 | 37.993 | 0warning/0error |
| diagnostic tiny | 42044 | 4.031 | 1/1 PASS |
| diagnostic full | 83180 | 4.170 | 8/8 PASS，27子变体 |
| legacy publisher | 84256 | 4.452 | 8/8 PASS |

八组变体数为1/5/5/6/5/2/1/2：确定性、身份绑定、provenance/magic、初始化、decoder、取消、已有目标、旧入口拒绝。mutant重算真实payload checksum及整文件SHA，实际触达parser；seed变化同步expected seed，触达canonical descriptor重算，不能称它已触达权重再生门。有限weight篡改实际触达F32位模式再生门。预取消与AfterFlush取消分别核文件未创建/写后回收；目标冲突保留旧字节。旧head/checkpoint负例传新文件真实SHA及valid tiny feature preflight，实际因magic拒绝，不创建训练state。

三DLL在全部阶段恒定：Tests206848bytes/SHA `B73C20E3F2D452E7A9E88C5738F000AC234730FC113882656BD89D00C121D7EC`；Sezika709632bytes/SHA `49FDC4D4CA626B903DACC82365AD5CAC6EAB91FC45271E51262135E6A08828CD`；IndependentModelTool478208bytes/SHA `3215D0A94C16EE9624888A3A7EEAA6B7B461935F96CEDCB44DCC447BC2A2146C`。

## 资源与未验范围

原始证据根 `.artifacts/diagnostic-head-asset-20261008/`；四runner均Succeeded0/cleanup为空。UTC05:22:23 root核21条记录身份无匹配start存活，三个专属fixture根均0项后删除；`root-final-validation.json` SHA `BBE3055CF7858F17C1BA9190002FAF3A7ABC0CA75F7F2BB61E54378382EA5158`。记录包含保留handle的root identities与三额外短reader；未覆盖所有历史短reader/未观测子进程，不宣称全机零残留。作者旧PID81592曾被不同创建时间进程复用，保留未操作。无GPU/服务/下载/独占MCP；交付日志和用户微信文件保留。

本批未建立真实H768诊断资产、CLI、runtime或推理接线。没有正式训练、实测loss、语言质量、校准、GPU前向、AOT或性能验收。下一步采用独立typed诊断pipeline；不得把descriptor放入训练feature/data hash或生成虚假正式head。

本批已单独提交 `c72ab5acee27edecd39c5ec56f8d9a7af684f973`，没有push。八Git回执全部Succeeded0/cleanup为空，Git roots/launchers与五额外已知reader共21条记录身份无匹配、unknown0；`root-git-resource-closure.json`。首次默认沙箱读取StartTime为null、exit1，没有写成功回收结论；后续有权限的只读精确核验通过，原失败文件保留。后续runtime/CUDA另批验收见[独立证据](diagnostic-runtime-2026-10-08.md)，不扩写本资产批的运行范围。
