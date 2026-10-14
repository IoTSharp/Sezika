# Linux 编译来源证明兼容修复

2026-10-14，本项修复两种实际观察到的证明格式差异：Linux 执行器先登记 broker，再记录真实 exec 后 argv；Ubuntu 编译器导出的部分生成文档在 Portable PDB 中声明 SHA1。项目仍不固定 SDK 版本，证明记录实际编译工具和输入身份。

`CompiledProgramProof` 在 Linux 构建门中要求真实 `AfterExecCommandLine` 与登记的 `ExpectedExecCommandLine` 相等，再逐个 NUL 分隔参数核对原构建请求。只有预期值、缺出生身份、异 scope 或缺完整退出/回收证据均不能通过。没有利用 ExitProof 字符串豁免真实 exec 观察。

手写 C# 文档仍必须声明 SHA256。生成文档只接受已知 SHA1/SHA256 GUID 及准确20/32字节校验和；SHA1 只允许已明确映射的真实生成文件。所有源文件仍独立固定完整文件 SHA256，并从同一真实字节核对 PDB 声明算法。SHA1 校验和的 `PdbChecksumSha256` 为 null，不将 SHA1 冒充 SHA256。旧 SHA256 证明保持兼容，旧证明原件没有改写。

## 真实正控与负控

原生 Ubuntu 证明根为 `/home/maikebing/sezika-t8-proof-v3b-20261014`，消费者仍引用其中源码、PE/PDB 与运行文件，必须保留该根。它由真实新源码快照编译产生，没有改写 Windows 路径、移动旧 PE 来制造 Linux 证明。

| 原件 | SHA256 / 范围 |
| --- | --- |
| `build-summary.json` | `a044caea144313b192233a40d8b556418405eb955687a840ec1c079ac59de634`；23份手写 C#、1份项目、221份真实生成文件、5份 runtime |
| `proof-full8-report.json` | `5732107bb19849d3189435c239f05ce6aca0da9e6ea536cecfe1ccde2aa2ea0e`；8/8、失败0、未执行0 |
| `laya-reference-receipt.json` | `4b6f0e14f84b3bbcbe3bba3312090584b55e81fd3b6c74be509534c2390394b0`；真实单条 answered1、failed0，`upstream_executed=false` |

221个生成文档中218个声明 SHA1、3个声明 SHA256。实际 PE/PDB 正控为244个源码文档、5个运行文件；完整 proof8 耗时1.382秒，原30秒内限保持。第8项在原真实证明的内存副本上分别改变缺失 post-exec、exec 期望、出生 start ticks、scope、生成文档算法/长度/字节及独立源 SHA256，八个定向拒绝均命中生产门。其他七项保留未声明源、生成源、构建输入、真实 CodeView 单字节变化、编译器/项目 argv 及不完整 runtime 的实际检查。

Root 已独立读回上述三个原件并逐字节匹配完整归档清单，另审查三个源码 diff。原件的完整383文件、7,915,652字节 ZIP 的 SHA256 为 `0f649b98719848564422650c90477ecd62f3f5108949b56ef8e66207372291ee`，位于本机 `oracle-numeric-t8-candidate-v3/ext4-complete-evidence.zip`；它是证据容器，不冒称经过项目 TAR 解包器验收。

后续Root还独立复核新冻结180份源码、六份AOT发布的16个完整运行文件，及保留证明根的新703文件/15,846,651字节，全部SHA/长度相符；两个最终Linux审计Launcher也已准确补查为不存在。61份完整原生回执、401条身份记录的审计通过，只表示该分区当前live/unknown均0；原MP32不完整登记链及其UNKNOWN不在该通过分母。原11:14窗口后写入的分区摘要保持原样，补查记录在本机`root-t8-final-readback-v1/`。

## 保留的失败与范围

最初 Linux 编译实际通过，但旧证明器拒绝生成文档 SHA1，原 FAIL 保留。修复后的 `/mnt/c` 候选在30秒证明内限超时124；随后只进行一次 ext4 对照，真实重新编译和证明通过，不能据此宣称 I/O 是已证明的唯一原因。

首次 Windows Capture AOT 发布触发 MP32 生命周期身份界，原 UNKNOWN 及严格登记链审计失败保持；未使用其输出。后续独立 MP64 发布与消费者范围另见 [Capture 证据](tooling-oracle-capture-final-2026-10-14.md)。本项不宣称全部历史失败都已变为 PASS。

本项是编译来源与固定小规模参考的证明修复，不是上游 Python 执行、全46条 Laya、质量、校准、训练或性能验收。三个消费者的 Native AOT 发布和实际消费另按相应原件验收，不能由 managed JIT 的来源证明外推。
