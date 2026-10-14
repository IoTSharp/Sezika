# IndependentNumericCompare 新消费者有界验收（2026-10-14）

本次通过的是全新冻结候选的 Windows managed-JIT 消费者；原先旧16项回归及03:49比较缺少当时完整consumer runtime pins的历史缺口仍为UNKNOWN，初始独立审查PARTIAL保留。没有用现存DLL/PDB哈希倒填旧运行，也没有改写旧数值报告。

## 来源与构建

候选固定12项输入，source-manifest SHA为`2aeb435454a5f5d8d1144a6afa4ebffea23871ce0148e5b7b693bd094296e714`。编译仅包含consumer自身六个C#文件和已在`2b3ff83`提交的ReferenceTool/CompiledProgramProof链接；csproj没有Tooling ProjectReference或未提交Tooling运行模块依赖。项目global只保留sdk.allowPrerelease=false，实际私有SDK身份随本次编译器argv及回执留证，不构成项目SDK锁。

唯一一次真实构建成功，耗时6.106144秒，完整stdout/stderr EOF、退出0和清理通过。独立读取PE/Portable-PDB，7个手写来源与110个实际生成来源的SHA逐项相同，PE/PDB GUID/stamp绑定，完整runtime为5文件。最终12个冻结输入、117个PDB来源和5个runtime合计134条pins再次核对均未变。原consumer DLL/PDB及原ReferenceTool producer证明保持原件。

- consumer DLL SHA：`7adaa8aea26770cde04ad7337f193810691a32ecfe94a8b98335995c19d2383f`
- consumer PDB SHA：`7e729583abdf1f386cb1b80e0dc22cc363e564f12a0eaed0b3cc49d67af4c624`
- 新build回执SHA：`bab709e53f1699d3cb56ce3d0192ed35cd6598801f15c49abd268a9091d882f0`

## 回归与真实数值比较

legacy先tiny1、再full16通过，完整19个断言变体；port先tiny1、再full16通过，完整24个变体。每阶段前后均核源码及runtime。合成fixture保持fixture_only和拒绝正式准入语义；它们不代替生产完整来源证明。

新实际compare-port复用原始actual capture、reference-v10 capture、测量前run合同及原port-proof，不重新导出、运行模型或连接网络。actual完整元数据为267项，选取25个合同检查点加marker vectors，报告26/26、884736元素、失败比较0、失败元素0，耗时4.9321949秒。原ReferenceTool compound build summary SHA仍为`aa3394b6c4286b1aced19b069e1a8809a96678c6c00622fdba2abda22e8572fa`；新生产消费者实际验证其24项来源、219项生成来源、5项runtime及原134 tensor证明链。原1.23GB权重仅用于只读来源/字节核验，不重新forward、复制或发布。

- 新数值report SHA：`0e8ffe4bf6c711b558cb785da36bb378240371a67637bacf11ae913d95cb4653`
- 新compare原生回执SHA：`f8ceb5e26996b97d147eba6c5254ea46de4bcef679c6e0d7395662949a4f552d`

结果保持`accepted_port_derived_reference=true`、`accepted_independent_reference=false`、`upstream_executed=false`。legacy上游实际执行的v1门槛没有替换或放宽，固定容差未修改。此结果不等于完整算子、tokenizer、语言质量、训练、校准、NativeAOT或benchmark通过；consumer Linux也未在本切片验收。

## 原生收口与范围

严格native audit读取6份完整原生回执、8个scope进程身份，全部ABSENT，matching-live=0、unknown=0、historically-unobserved=0；另7个native launcher按实际PID/出生时间只读终核全部ABSENT。没有按名称杀进程、重跑失败套件或删除旧文件。外层审查曾把NativeReceipts.OriginalStatus误读为Status而抛错；实际audit自身已退出0/PASS，原件保留后按正确字段重读验证，没有再次运行或覆盖audit。

- strict-native-audit SHA：`e8fbde255cd8de9e8e9b6ddfcc41d4d29c34574b894569282c9738c2d658d9e9`
- 新execution-summary SHA：`d5b233856f729ef55e0269de6ca0896a421eec3a0101376127a39b61aac8cc49`

本次独立潜在提交范围为consumer六个.cs、csproj、README、port-derived模板和本文，共10路径。linked ReferenceTool来源已经提交，不重复暂存；私有绝对运行路径、旧run合同、模型/捕获与foreign GPU/Kernel/Trainer dirty均不进入此切片。源码8路径在UTC06:24:51与已验snapshot重新逐项核对，README与本文为随后获准的文档追加。Git/index仍由主集成者独占；T3继续等待最终core，不由本数值切片宣称完成。
