# T7 当前调用者与退役门槛最终准备（2026-10-14）

本轮完成 22 份当前文档/技能说明的有界审阅，并准备两项防止错误 READY 的最小源码修复。它没有完成当前 466 项原件的实际退役审查。原有 tiny/full 成功回执只验收了 synthetic review fixtures；新 14 项检查、真实当前清单小/完整审查均为 NOT_RUN，等待主集成者安排构建槽。历史脚本、既有 dirty、原始失败和旧回执均保留。

**范围与身份**

开始时间为 `2026-10-14T03:42:22Z`，截止 `04:17:00Z`。PowerShell 7.6.6。本轮输入范围为 30 个文件，当前 caller 文档/技能为其中 22 个；一次范围搜索返回 35 个匹配，0.422 秒，低于 600 匹配 / 240 秒上限，无文件达到每文件 30 匹配上限。未递归搜索历史证据目录、运行旧脚本、构建、训练、访问服务器、安装、联网或写 Git。

主集成者独占共享入口、8 份技能说明、根文档与 Git。本轮继承两份 untracked 源码，先绑定原始 SHA 并复制到新审查根的 `source-before`，再按主集成者明确批准修改。仅修改以下两份源码和本证据文档：

| 文件 | 原始 SHA256 | 新 SHA256 |
| --- | --- | --- |
| `tools/Sezika.Tooling/Inventory/RetirementReview.cs` | `d1b33686158deabb3a1a9c68adebcd3a760b15efd242648598e6dcfea352f05a` | `01664a5626d1da669aaedfa94c577e088efda3b4a243402c74d2aa1d984ff4cd` |
| `tests/Sezika.Tooling.Tests/RetirementChecks.cs` | `140ae08bc8120667e33ae5d9e65b0d58bd4aa21a562ade6bf215614e42fbf76f` | `570ca0c711fa76eae18b98f533737bec87f3490329effe6af3dc157cd1949156` |

外部审查根为 `C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\retirement-final-plan-v1`。保存 `source-freeze.json`、原源码、`minimal-source-change.patch`、`caller-pins.json`、`bounded-caller-review.json`、`per-path-gate-plan.json` 和两个实际清单请求。原候选和历史文件未改写。

**E1：466 项只完成分类**

原分类 `docs/evidence/tooling-migration-classification-2026-10-13.json` 的 SHA 为 `71117f6b33bdc49e11da582348bd6cae528792a5d57b128471b5d99a29605a8d`，状态 `PASS_classification_only`，`ImplementationsAccepted=false`。其 `SourceSha256` 与原 CSV `docs/evidence/tooling-native-aot-inventory-2026-10-13.csv` 实际 SHA `ec3703ae4d0f8ec285124826b0fc9a7a3f25284cf509f2b0e66205dbd7bfd568` 一致；CSV 与 classification 均有 466 行。

| 分类 | 数量 | 本轮可得结论 |
| --- | ---: | --- |
| tracked | 282 | 受版本管理的原脚本清单，不等于替代实现已验收 |
| ignored 定向样本 | 174 | 不是全部 ignored 源码普查，也不能全部认作长期实现或历史废料 |
| 外部技能依赖 | 5 | 只审项目是否解除依赖，不授权改动机器技能 |
| 既有 untracked 证据 | 5 | 保留原件，不删除、不用当前状态倒造历史 SHA |

工作包分母为 T1a=3、T1b=17、T2=22、T3=44、T4=140、T5=26、T6=25、T7=189。T7 的 189 项全部是 `PRESERVE_HISTORICAL_BYTES_REMOVE_CURRENT_CALLERS`：184 tracked 与 5 既有 untracked 历史脚本。保留旧源不表示仍要执行，也不能因被归到历史类而跳过原字节保全。

ignored 174 项分布为 T1a=1、T1b=7、T2=14、T3=25、T4=118、T5=1、T6=8。它们覆盖旧 guardian/SSH bridge、resident、full-corpus transport/publication/local-mirror、数据准备和验证；其中包含一次性修复、探针和可能的长期能力。必须逐项绑定用途、当前 caller、C# 来源与独立回执，不能把同一包的一个 PASS 批量套到全部条目。全 466 个确切路径及拟替代能力列于 `per-path-gate-plan.json`，明确标为 PLAN_ONLY，未伪造文件现存状态或接受 gate。

**E2：已有成功属于 synthetic checks**

`.artifacts/tooling-migration-20261013-01/separate-retirement-review-tiny-result.json` 与 `separate-retirement-review-full-result.json` 记录了 Windows 测试驱动真实退出 0、完整输入输出、cleanup PASS / remaining 0。完整运行的原始 `checks.txt` SHA 为 `32ee47e11e2e42a707299a2a3584f36d28fc0de80547b31ba3e4f783ac80721a`，完成 12 项，驱动 `native_aot=False`。

原名称“actual 466 item receipt”的检查实际构造一个 inert 原脚本 fixture 和 465 个缺失路径，检验 READY=1 / UNKNOWN=465 / gate gaps=465 的分母语义。它没有读取仓库当前 466 项。因此新源码将该名称改为 `synthetic 466 item fixture`，旧日志原字节仍保留。12 项原覆盖全部保留，另加两项直接调用生产 `RetirementReview.Review` 的回归检查，共 14 项；本轮未执行，不能把旧 12 项成功移用于新源码。

**F1：同一行历史词不能隐藏实际调用**

旧文档判定只要一行出现“原始回执”等词就给出历史豁免。例如当前指令 `执行 pwsh tools/old.ps1，并保留原始回执。` 会被排除，具备其他 gate 的 fixture 可能错误 READY。该结论来自源码分支审查，本轮未运行旧版本复现。

最小修复只在同一行没有执行语法时应用该行的历史词豁免；明确历史标题/历史代码块和禁止执行说明继续保留原语义。新增回归要求上述当前调用得到 `CURRENT_EXECUTION_REFERENCE`，对应条目不能 READY。没有新增通用语言解析器，也不删除历史示例。

**F2：拼接扩展名不能静默通过**

旧 caller 收集只纳入有完整 `.ps1/.py/.sh/.cmd/.bat` 的行。`Process.Start("pwsh", "tools/" + "old" + ".ps" + "1");` 不含完整扩展名，可能在收集前消失，随后错误 READY。本轮同样只完成静态分支确认。

修复额外收集代码中明确运行 API / 启动语法加可疑解释器的行。无法绑定到 frozen original path 时，沿用既有 `unresolved_script_expression` / `REVIEW_REQUIRED`，不猜测实际脚本路径或执行成功。新增回归要求 split-extension 示例保留 REVIEW_REQUIRED。注释与明确历史文档的已有测试仍保留，fixture 总数 14 未超过既有上限 16。

**E3：22 份当前文档/技能的真实范围**

范围包括 README、ROADMAP、HANDOFF、AGENTS、`docs/tooling-current-workflow.md`、两技能共 8 份说明，以及 Tooling、Benchmarks、Evaluation、IndependentOracle、LayaOracle、NumericParity、IndependentNumericCompare、OracleCapture、OracleCompare 的 README。具体 bytes/SHA 见 `caller-pins.json`；后续任何编辑必须更新 pins 并重审。

8 份 dirty 技能说明均已将当前流程指向 C#/.NET10 工作流，保留新授权、准确 ref、预算、真实 GPU、独立读回和历史证据边界；未发现其中有当前执行旧脚本的指令。原 Python/PS 调用被历史快照链接替代。此处确认的是当前文档写法，不证明旧技能能力全部已有可用、完成验收的 C# 替代，也未在本轮重读历史快照原字节。

| 观察位置 | 判断与主集成者后续动作 |
| --- | --- |
| `tools/Sezika.Tooling/README.md:56` | “尚有旧调度调用者”是未指明当前 caller 的 T1a 阶段旧陈述；应按最终证据更新，不能把这句话当作实际旧调用证明 |
| `HANDOFF.md:287` | 旧 `Invoke-BoundedProcess.ps1` 指令位于顶部已声明的先前阶段记录中；应加明确历史章节边界，防止当前接续或简单 checker 把它重新当默认入口 |
| ROADMAP 的旧脚本名称 | 属于迁移目标与禁止标准，不是当前执行证据；有歧义时保留 review，不能直接报“仍在运行” |
| IndependentOracle / LayaOracle README | 明确历史章节中的 Python/backend/exporter 合同只供追溯，当前执行链为 C# capture/compare |
| 新独立上游参考生成 | mmBERT/Laya 参考与 tokenizer 边界的 C# 新生成能力尚未完成接线/验收；冻结参考消费不能替代这项门槛 |

在这 22 份当前说明中没有确认正在执行的旧脚本入口，但本轮没有覆盖全部 C# caller、配置、构建接线、ignored 实现和外部技能依赖。不能将“22 份说明未发现当前执行”扩大成“项目全部 caller 已无脚本”。两个实际请求均保留 `CallerScopeDeclaredComplete=false`。

**P1：必要 C# caller 与真实清单计划**

当前工作流需要的入口分别是：`process run/audit`；固定 RemoteTool `upload/download/dispatch` 与 NativeAOT worker；`git run`；`transport download/prepare/verify/reassemble`；`publication prepare/preview/upload/commit`；`task prepare/resident`、`bundle inputs/build/freeze`、`archive pack/extract`、`checkpoint verify`、`host inspect`；Corpus / DatasetTool / TeacherTool；六个 Validation 命令和既有 OracleCapture / OracleCompare / IndependentNumericCompare；最后 `inventory review`。每个能力必须绑定准确 `.cs/.csproj` 来源、输入和独立验收，不能只列命令名称便接受 gate。新独立参考生成仍是缺项。

本轮已准备 `tiny1-caller-current466-request-v2.json` 和 `full22-callers-current466-request.json`，都使用真正当前分类 SHA 与 `ExpectedEntries=466`。tiny 指一个当前 caller，未制造一行“真实清单”替代原 466 分母；完整请求覆盖本轮的 22 份说明。两者均为 `Gates=[]`、caller scope 未声明完整，当前执行状态 NOT_RUN；不能产生接受替代能力或项目 DONE 的凭据。

取得构建槽后，主集成者先从本轮两源 SHA 创建准确候选并构建，使用新输出目录运行 retirement tiny1 检查，再运行完整 `--suite retirement --maximum-checks 14`。随后运行两个 `inventory review --request ... --output <新回执>` 请求，预期保留 PENDING 而不是 READY。主集成者已说明共享入口当前把 RetirementReport 的 PENDING 映射为 exit 1，本轮未扩大范围重新读取或运行共享入口；最终须用实际 CLI 回执确认。

**真正剩余清单**

1. 新两源的构建与 14 项回归，Windows/Linux/AOT 适用范围分别留证；旧 12 项 PASS 不替代新门槛。
2. 对当前 466 个原件实际生成 source snapshot、NOT_FOUND/UNKNOWN、准确逐条 gate gaps 和当前 caller 结果。本轮分类与计划不提供这些结果。
3. 扩展并人工确认完整当前 code/configuration/ignored caller 范围，再冻结 pins。主集成者修订共享旧措辞后重生所有受影响 pins；动态/歧义引用保留 REVIEW_REQUIRED。
4. 为每条需要长期执行的 ignored 能力落实正式 C# 来源及独立通过回执；189 条历史原件另做不改字节保全，不能以当前快照倒造原始 SHA。
5. 五个共享技能依赖逐项验证项目已解除依赖，机器技能原件保持；本轮不改外部技能。
6. 新独立参考生成及其他未验业务能力保持待实现/待验收。8 份说明切换、fixture PASS、退出 0 均不能闭合这些门槛。
7. 最终共享入口、根文档与技能字节冻结后，主集成者串行完成最终源码树门槛和 Git 集成。退役 checker 只产出 review，不执行删除或退役。

请求形状复核：tiny 初版的单元素 CurrentCallers 被 PowerShell 展开为 JSON 对象，未执行；原计划保留，v2 使用明确 object[]。最终 tiny/full 的 CurrentCallers 与 Gates 均已按 JSON array 独立检查，分别为1/22个caller，仍未运行 checker。见 plan-json-shape-check.json。

## 最终检查器的实际分项验收（2026-10-14）

`tooling-final-combined-candidate-v1` 的92文件 SHA256 `7123d23ce6b0993456e892e03a6e667550d88a7ac78581c6f4cab8356b07367c` 包含本两源冻结字节。Windows FDD 与 Linux Native AOT 均实际运行 retirement tiny1/full14通过，Linux日志native_aot=True；两处false-READY修复没有削弱scope、原件、语义或独立gate要求。Root独立核源和精确共享接线，`PENDING`仍映射CLI exit1。

真实466分类的Windows tiny/full32均返回Ready0、Pending466、MissingGates466；Linux同源检查Ready0、Pending461、UNKNOWN5、MissingGates466，5项是原分类内Windows外部技能路径，不伪造Linux可读或机器技能变更。full32原有4caller PIN_MISMATCH按运行时原样保留；后续文档继续变化须重新pin。以上预期PENDING的原native FAIL/exit1不改为执行PASS，检查器通过不代表466项退役完成。

组合资源终核Linux16回执73身份与Windows30回执110身份全部缺失或复用，live0/unknown0；不是全部属于retirement套件。整个组合原LinuxOracle模拟30秒取消FAIL及唯一ext4受控重跑另行保留，验收汇总SHA256 `5616e8ba4c88c94b22d7c16d56b2be0ca688254e174e394dc79d78900b34ee3b`。

历史189项另列：Root对固定历史提交`4d02942704447e34e2a4e9f12b5f47a4ddeb8fb4`的184个Git blob与当前原始字节逐项计算Git对象身份，全部相等，未经过EOL/attribute过滤。新`historical-184-git-readback-v2/preservation-manifest.json` SHA256 `d3edcea4d4f5b6cb94724833f7c56965120b850d1ddadadd0673948b58d778c2`另记本轮raw SHA256，明确没有回填独立历史SHA256；5原untracked旧基线UNKNOWN保持。读取只使用Git树元数据、没有执行或在日志复制脚本正文；两个预启动script-argv guard拒绝原件保留，guard未放松。

本提交仅检查器实现、14项回归及实际466 PENDING事实。完整当前caller范围、逐path能力/原receipt绑定、机器技能项目依赖解除和最终退役仍在后续T7任务中，不能以本切片结束整个迁移。