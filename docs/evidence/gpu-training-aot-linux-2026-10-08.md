# Ubuntu GPU head 训练 Native AOT 证据

2026-10-08。S4-07/S4-12 的合成 H4 marker-head 训练在 Ubuntu WSL2 `linux-x64` Native AOT 中实际通过。基线 `6c3b096`，复用 Windows 最终五个测试源码/项目与核心、kernel source、17 PTX/manifest/绑定，共28项输入；没有源码修改、PTX重导出、CPU trainer/encoder、教师、网络或安装。正式数据训练、encoder反传、上游数值、语言质量、校准与性能仍未验收。

## 真实发布及训练范围

PowerShell7.6.6、Ubuntu SDK10.0.112、gcc13.3.0、clang18.1.3、ld2.42。两次publish使用各自独立obj/bin/output/TMPDIR；任务独占NUGET_PACKAGES，明确复用既有cache与`/usr/lib/dotnet/library-packs`，空local RestoreSources、NuGetAudit=false，无远端源。单MSBuild节点、禁shared compiler/build server/node reuse，PublishAot及AOT/trim警告视为错误。

第2且最后publish实际60.930秒（Linux58秒），成功且无warning；ELF6,415,616bytes，64-bit/little-endian/x86_64，SHA-256 `75BE4B654A624801D6F940FC709432DF84E7BBE2F5631425AF184C1754EB2258`。root独立核原始文件hash和ELF头；native报告的process hash与此相同。实际runtime dynamic-code-supported=false、compiled=false、TPA=null、RID=linux-x64，不用旧ELF或Windows obj。

CLI请求`IlcMaxDegreeOfParallelism=2`是意图；实际ILC rsp没有parallelism/maxdegree项，不能宣称两线程上限生效。root只读已知`microsoft.dotnet.ilcompiler/10.0.12/build/Microsoft.NETCore.Native.targets`，行278实际使用`IlcSingleThreaded=true`生成`--parallelism:1`。保留原rsp/编译器argv，不增加第3publish；后续编译须使用有效属性并核rsp。该Linux核查不自动证明Windows版本的属性行为，Windows证据亦已改为仅记录请求。

固定Choice/Score/Boolean各一合成训练题，H4、seed17、learning-rate0.05、12个FP32参数、最多6累计步；另一个不同ID的开发诊断题。开发评分仍为真实`scalar-linear-marker-head-fp64`，不是GPU质量验收。使用生产report/checkpoint/head loader完成三文件磁盘恢复。

| 调用 | 初始→完成步 | GPU更新 | launches | H2D bytes | D2H bytes | owned峰bytes |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| tiny | 0→1 | 1 | 4 | 80 | 56 | 700 |
| first | 0→3 | 3 | 12 | 160 | 72 | 780 |
| resumed | 3→6 | 3 | 12 | 160 | 72 | 780 |
| continuous | 0→6 | 6 | 24 | 160 | 96 | 780 |

主对照12更新、48launch、480-byte H2D/240-byte D2H；恢复和连续的12参数bytes、loss、checkpoint/head hash完全相同，首段三个文件前后hash不变。只比较Linux RID内部，不要求跨RID权重或文件hash相同。设备RTX4070 Laptop、Driver13020、SM8.9、UUID `134BF256A4B04FD4E3073C3D34F79CB4`。

native tiny1及full11共16更新；full主12更新、六cleanup另3更新、三adoption零更新，预取消零训练/发布。full含caller vector探针，实际合计61launch/308-byte D2H；cleanup JSON未记录H2D，不伪造全套H2D总和。11case的context均实际销毁，有终资源字段者bytes/buffers/modules为0，adoption events为0；主workflow另有释放断言。正常profiling路径实际运行，不代表AOT覆盖所有历史event/OOM故障、真实heap OOM或native hang。这些计数与耗时用于执行身份核验，不是benchmark。

## 失败、执行边界与独立复核

作者窗口UTC23:46:05–00:11:05，00:10:05停止新增，实际00:01:14收口。每次publish≤600秒、Windows outer≤612秒；native tiny≤15秒，full≤108秒/outer120秒，最多16更新。源/project/runner读取18项上限、输入hash≤31，实际28输入执行后未变。

首次publish在链接阶段被boundary自身2秒/proc扫描上限中止：inventory_failed/exit127，clang收到TERM/143，Windows回执63.869秒。它不是AOT/trim编译失败结论；原cleanup_incomplete=1/survivors=0保留，之后独立22身份与45项token库存确认无残留。snapshot注释/条件仅2→4秒，root逆变换精确重建原SHA `C2F165D7D5C15F07AB67753ECB29F6CE56FF0606C583B9BF83260671C59F2C07`。额外唯一取消tiny exit125/cleanup0，只观察readiness worker和sleep0.1，没有覆盖请求的sleep10 payload。

现runner的6pass/8秒只包清理主循环，末尾无条件snapshot与嵌套调用不共享严格总截止，不能把注释当8秒证明；第二publish及native两调用分别实际cleanup0/survivors0。survivors=0必须结合完整终核，不能消除首次incomplete。此缺口已登记独立通用runner硬化任务，本次不覆盖冻结脚本或新增第三次发布。

两份实际执行源码已按原字节保存：[Bash](gpu-training-aot-linux-2026-10-08/bounded-linux.sh) SHA `45E0C3A04E63266DCE48408D0BF9263404EE4A901C39F13D8D56368D45F0C790`；[PS7入口](gpu-training-aot-linux-2026-10-08/Invoke-LinuxBoundary.ps1) SHA `9E5746209BAEF9A13A0736E3EC6178A71A730A13D3CE78EEC581896FBC20C75D`。它们只作本次证据，固定artifact根为`independent-training-aot-20261008/linux-v1`，不当作已硬化通用工具。cooperative payload须保持token、不daemonize；/proc采样可能漏极短helper，无pidfd/cgroup原子保证。

最终作者`.artifacts/independent-training-aot-20261008/linux-v1/execution-evidence.json` SHA `5694BA0C921394684950A34918C3991185051AEC667CDFF7D855EB8BE789F1FF`；关闭回执SHA `2AA78FD1CABE4BA0BDEAEEF5DF4DC23EC8AC9E95454B5ECB699B47F9BA5A7DB5`。独立输入审查SHA `117E9C20E4C4B75CADB60A98F0AEF62506BAB87BDB02AFEAABF3B89B23ACCCB9`只绑定其读取时29项，不冒充运行验收。

root先tiny再full：28输入/实际ELF、tiny三保存物/full九保存物原字节、native1→11case及runtime/context通过；九Windows回执（两个预期125及首publish127明确单列）、七Linux三项原始回执身份均核。UTC00:05:08另核作者及八root回执，共46身份无匹配存活，见`linux-v1/root-receipt-closure-9.json`，没有新增GPU。作者最终49个Linux pid/starttick均absent，43个/proc token项live=0、uncertain=0；17个Windows登记身份无匹配存活。

临时清理原批命令被自动审批拒绝，未提供理由，作者关闭时`temporary_cleanup_completed=false`保持不变。root改为更窄的静态精确路径：先验证并删除一个0-byte/非link lock，再另17个同身份lock；随后对17个精确空目录（含两TMPDIR根）使用`Directory.Delete(path,false)`，逐项确认空、17次/15秒上限，无递归删除。成功记录`root-compiler-cleanup.json`及有界回执；两个TMPDIR均不存在，用户文件/共享cache/交付物未删。原拒绝及关闭时库存33项不改写。

GPU/原源码窗口已释放，无教师服务、下载或独占MCP。此合成head的Linux训练AOT切片与Windows各自闭环；正式trained-head两RID、数据准入/封存、上游数值、真实训练收益、质量/校准/性能/发布仍开放。Sezika仅commit，不push/发布/部署，30分钟监督继续。
