# T2 上传预检重构：Windows 限定验收

2026-10-15。上传 destination、partial 和 state 共用一次完整父链验证及一次目录读取；后续 I/O 仍重新验证祖先与完整 SHA，不跨阶段缓存。链接、设备、重复项、目录数量、路径深度、取消和超时拒绝保持。512 入站帧、32 MiB 和 64 KiB 状态上限未提高。

当前候选 Windows/Ubuntu JIT 构建曾分别通过，实际 SDK 为 10.0.401/10.0.112，仅记录工具身份，项目不锁 SDK。Root 已独核 412 个运行文件、1,208 个生成源及 92 个当前来源；Ubuntu 重启后当前 Linux 原件缺失，不能据先前通过宣称当前可运行。

Windows 实际完成 10 个应用、35 项检查：两个离线合同（1/26）及八个真实回环 SSH/SFTP 场景。case1 拒绝覆盖且目标不变；case3 同时验证恢复后 partial/state 消失；case6 创建 partial/state 两个真实虚拟 SFTP 链接，精确拒绝且未生成目标，独立模型 SHA 不变。八场景请求数依次为 120、51、121、52、1、92、36、36，结束句柄均为零。

Root 独核全部 native exit0、EOF、cleanup PASS/remaining0、具名合同与目标原字节。contract26 仅有精确 76 字节的已存在输出负控 stderr；其他业务 stderr 为零。case5 的预期错 host-key 断链产生 JSON diagnostics；原额外“diagnostics 必须为空”辅助 FAIL 与短进程 CIM argv=null 保留，没有重跑或伪造观测。一次资源审计覆盖 10 目录/10 身份，live0/unknown0；另核 16 个包装器/控制器出生身份均已关闭。

验收仅限此 Windows 预检重构。原 native9 的 511 个非 INIT 请求加 1 个 INIT 耗尽 512 帧是真实 FAIL；当前修复的 native9、四份生产 bundle 捕获、四项 crossOS 及 Linux 十应用仍为 **NOT_RUN**，列入 ROADMAP。不能宣称端到端帧问题、T2 或全迁移已完成。服务器/GPU/教师/训练/模型及数据发布均 NOT_RUN。

逐项原件、SHA、来源与 Root 独立复核见 [JSON](ssh-upload-preflight.json)。原始失败和任务专属目录保留，未绕过自动审批重试删除。
