# v3 真实GPU训练实验独立验收

结论：**接受run-03真实小实验的工程闭环：GPU导出features、72次head更新、真实资产保存与加载、新进程验证及8题GPU推理均有对应证据。语言质量未通过/尚未建立，不能用于正式训练质量、校准、封存测试、AOT、性能或发布验收。** 训练集CE下降，开发集CE变差；开发题与教师仅2/8一致。禁止把这次head训练表述为整个encoder反向训练支持。

审查人为 `/root/diagnostic_profile_review`。本窗口2026-10-08 07:51–08:11 UTC，08:08停止新增；上限12准确输入、6次短shell。输入为v2/v3两个freeze、Program/Report/Checks三个准确源、七件实际取回原始训练材料。复用已审evaluator/core，不重新构建、GPU执行、远端操作或训练；不改源码、Git、共享文档。只写本报告，保留此前pending和失败报告。

## v3最小源码delta及实际身份

07:51:57独读v2/v3 freeze和当前三个源，真实bytes/SHA全部符合声明。v2 freeze SHA E353851808DCF5142D29A3CA667FD898E1E48999F9C14C1EAFA8A319780036D6；v3 freeze SHA D8A3826B4AC68125628A680C07EE5786F260384C560DE9E6E58A968505EA7CA9。

| v3准确源 | bytes | SHA256 |
|---|---:|---|
| tools/Sezika.ExperimentTrainer/Program.cs | 35242 | D0B3C897DCC07EDA65AD8AF53551B0837FE2CA617FBED1F13667DD278D4E28E0 |
| tools/Sezika.ExperimentTrainer/ExperimentReport.cs | 11585 | 5A0FD534D10FD58AA31D834B9273ACA41D49EE76884270E441D529B242D10A4C |
| tools/Sezika.ExperimentTrainer/ExperimentChecks.cs | 16557 | 033FB78C7C1F3E21937E889FB72F5CC2372AE2DFACB37FD5FD0532BE91005262 |

仅增加lengths入口/help、RunLengths及source-generated typed长度报告，增加64/1024接受与63/1025拒绝检查，并把MaxTokens上限与完整hidden有限值扫描边界扩到1024；default仍256。07:53:40在内存只撤回上述明确变化，先1个Program tiny，再3源重建，得到原v2 Program34834B/SHA32D1E0A27CC40D84857F0F7C5F0801CD425F5CAF1C6BAA43E32E40DF7CB7E260、Report10802B/SHA02ED613D667E50F9155C837A526E1F1EF1C69378D1EAEBD3305877E14E1F72AE、Checks11398B/SHA928D5290857893836063E16D98CA91D5948FE8E5D6A553AD78D6C36428EFC465，与此前已核v2完全一致。因此其余实现没有额外字节变化；没有源文件修改或临时重建文件。首次大输出的Program中段被工具截断，不伪称该段这次完整重读；精确v2重建证明它未改，旧独审证据可复用。

RunLengths新方法已完整读取：限定1或32记录、30秒linked budget、逐条进度；真实固定tokenizer17525329 bytes及SHA/special IDs，前后hash相同；调用原SequenceBuilder渲染、Max1024并核候选顺序，关闭输入leases后输出。没有加载encoder、打开GPU、计算features或训练。root报告v3实际publish无warnings/errors及tiny/full输入/lengths执行；这些额外日志不在本次12件输入中，不作为本审查人亲读的构建证据。1024实际实验可用由后续真实run-03成功补足，源码本身不预先证明GPU可用。

## 七件实际取回材料

以下路径相对于 `.artifacts/real-gpu-experiment-20261008/retrieved/`。08:00:45独读并计算每件真实bytes/SHA；report、validate stdout和两资产同时符合root预先给定SHA，三资产还符合原logs/trained-files.sha256中的远端绝对路径及SHA声明。两个runner TSV及清单自身的hash是本审查首次本地记录，root没有另外给定它们的预期hash，未伪称有独立远端预期对照。

| 相对路径 | bytes | SHA256 |
|---|---:|---|
| output/run-03/experiment-training-report.json | 19392 | C6B065CD0C66AA4544C0AF277C539D82A4E7932C66CD41AAAD37CFC1A0D30EE7 |
| .artifacts/validate-03/payload.stdout.log | 8401 | 1B49EF8859131ADFB582BD380739C7F99D64E5C88F042C1A417B4A2C1B435A1F |
| .artifacts/train-03/result.tsv | 630 | D852C25E9903530178D36378514955CDC9B723594C0CED23DA568F4EE99E0CF9 |
| .artifacts/validate-03/result.tsv | 630 | BD57BCD4401F143D6CD6FAA2F4DBFEEE812FBEDB80109E49E2639F8A5275CE82 |
| output/run-03/experiment-head.asset | 9619 | 2C93C6A9FD28C491AE5718F00C81C5FFB08859FC7F85C6A8F4DF6B209A5D5AAD |
| output/run-03/experiment-training.checkpoint | 9619 | 5C604C9510658373EB2933C2D82FAE0FF0E798D8837C59D3B66E5BE7A63C3528 |
| logs/trained-files.sha256 | 466 | C1C6EA3812CA8E20BFCCBC48E0FA013570A368064AB98224FE1EF1A89BDF42EB |

独立validate原stdout为实际JSON，contract为sezika/teacher-labelled-gpu-experiment-validation/v1，source_report_sha256精确绑定上述report，head/checkpoint/feature/parameter身份与训练report一致。两个runner为不同token/supervisor；进程独立性的实际命令/PID由root统一终核补足，本审查直接读的是其原stdout和TSV，不扩称独立核过root另列22个Windows身份。

## 真实训练、资产及新进程一致性

report记录frozen encoder、cuda-modernbert-fp32-v1 features、cuda-independent-linear-marker-head-sgd-fp32训练；seed17、learning_rate0.001、72 committed total steps、无resume、MaxTokens1024。源码先完成全部32条CUDA features才进入完整baseline CE和head trainer；完整24train/8dev denominator及成功结果补足真实32 features链路。最后一步更新前单样本loss1.4564497470855713保持单样本语义，未拿它作为完整before/after基线。

train feature SHA44DF5374A9A39C1B064A7AC8E34F1B4C13488F274A45DCCB4B837DB1A2EA145F；dev SHA8D5B807BE55F1ABB3C5C4A5960487E6FE7245E321B66052E93D70D58E7ECA2D7。baseline/after/disk-loaded/fresh的同分母feature身份分别一致，八组CE的完整分母、有限非负loss、host/device参数SHA均机械核过。

| 完整分母CE | before | trained | disk-loaded | independent fresh |
|---|---:|---:|---:|---:|
| train24 | 1.229626456896464 | 1.1749068026741345 | 1.1749068026741345 | 1.1749068026741345 |
| development8 | 1.2687934339046478 | 1.2772859781980515 | 1.2772859781980515 | 1.2772859781980515 |

baseline真实GPU参数SHA为全零9216B tensor的2D07A41AE992770085117E9815300BFD0730745883E60B24AAAD5E69DFC087AE，独立计算相同。训练后实际参数SHA3C15FFF5DADBBF423DF21BD4DFEB0DAD141A1469CE77C39A85C69187BDFF9B6A，与全零不同。两个真实资产magic分别SZMH1/SZMK1；08:05:38在每个固定9619B文件内最多404个偏移、10秒边界寻找该参数SHA，均唯一位于offset371，9216B解为2304个F32，2304/2304有限且非零。该检查证明实际参数字节存在且变化，未重跑模型或GPU，也不把byte-range核验作为完整通用资产parser。真实SaveNew/Load的身份与优化器元数据校验由已审源和独立validate成功结果共同补足。

训练、disk-loaded、independent fresh的同分母CE严格相同；两个原JSON全部8题推理rows严格相同，另机械核候选数/有限logits及probability、归一化、argmax、selected candidate、teacher target/agreement、平均NLL一致。微型scalar核验仅检查已输出数字，不是CPU模型forward/fallback。GPU pipeline平均teacher NLL1.2772859711641456，与F32 GPU完整dev CE因聚合精度有约7e-9差别，不混成质量正确率。

## 质量边界与正式准入

train CE下降0.054719654222329606，dev CE上升0.008492544293403625。8开发题teacher agreement2/8=25%（中文score-01、英文choice-01一致），六题不一致；这是已暴露、未人工审核开发题的一致性，不能称gold accuracy、质量改善、校准或封存泛化。不能因为训练loss下降而忽略dev变差，也不在此次审查后新增调参训练来挑结果。

训练和独立validate JSON的8个正式acceptance字段均显式bool false：formal_training_admission、human_quality、license_release、calibration、sealed_test、native_aot、performance、publication。数据声明仍formal=false、人审pending、teacher_output_training_permitted=false，并单独保留user_authorized_experiment=true。这次接受的是用户授权小实验的真实工程路径，不解除正式人审、许可、质量或发布门槛。

首次train01 strict token预算失败、首次publish四行syntax失败和root所述startup失败均保留为历史；run-03成功不倒填这些失败。旧pending报告仍保留，本报告只推进已真正完成的实验部分。此模型是冻结encoder加已训练head；不宣称完整encoder反向训练支持或可直接发布的语言质量。

## GPU身份和资源证据

report实际device UUID E786E313BC92A63DA833F4E48CBAFB66、name NVIDIA GeForce RTX4090、driver13000、compute8.9、device index0；报告驱动total_memory_bytes50864390144，这里保留真实返回数值，不按型号预期改写。CUDA artifacts source SHA3743158624364474AE920D4863C48D322168954B8FB992B5868D7DB0660E2FBD，manifest SHA8CF83A3B5145345D92D50D4695B4DD0200AE690C9E1FD70E910B399241B18D79，17静态kernel身份（含Linear、MarkerCrossEntropy、MarkerGradientSgd、MarkerUpdateStatus）。已审validate源在输出成功前核实际device/kernel与report身份；原独立stdout成功补足该路径。本审查没有再读core/重新运行GPU。

train和fresh snapshot在context Dispose前均owned_bytes0、owned_allocation_count0、loaded_module_count0、release_failure_count0；device/model Dispose实际返回true。peak owned GPU bytes两者1309369952，小于3GiB；host process峰值train2724786176、fresh2724184064，小于4GiB。report elapsed19.4227186秒为报告内部取样，Linux train elapsed24400ms、validate24090ms为有界runner范围，不能把这些不同范围数据混作benchmark。

两个Linux原TSV均status completed、payload_exit0、fixture none、cleanup_incomplete0、final_check_complete1、verified_live_count0、unknown_count0、unchecked_count0、cleanup_budget_exhausted0、tracked_identities2。train supervisor615111/token sezika-615111-24973-27020，cleanup3660ms；fresh supervisor616776/token sezika-616776-25463-27257，cleanup3750ms。该证明限runner已记录身份；原limitation明确cooperative checks、blocking kernel IO及short intermediates不保证。root另外通知22记录身份均absent，本次不读取额外closure文件、不把它写成独立亲核事实，不主张全机零残留。

## 审查脚本错误、修正和本窗口资源

第1/2次源码/hash/delta短shell成功；第3次七件读取的实际hash及原JSON/TSV成功，但可选asset尾F32检查错误调用PowerShell不可用的AsSpan扩展方法，出现两次非终止错误，exit0不作为该小检查通过。第4次改Array.Copy并启用ErrorActionPreference Stop，前述report/CE/rows/flags/resources机械断言已通过，随后错误假定F32 tensor位于最后9216B，tiny检查拒绝并exit1。第5次在同两个固定小资产内有界核真实参数字节范围，offset371唯一匹配、全finite/nonzero及两个runner机械校验通过exit0。两次审查错误原样保留，不归因模型训练，也不把旧输出倒填为成功。没有新增task输入或重复模型运行。

固定PowerShell7绝对路径，每次核7.6.6。所有循环都有明确项目数和10秒边界，资源终核最多5 reader及6级父链、20秒；比较退出条件人工核为-lt/-le/-eq等，先极小输入试查。等待最多14次、每次≤60秒并受08:08停止新增约束，实际3次。没有下载、网络、安装、长时子进程、独占MCP服务、临时目录、fixture文件；无Graphify、广域工具发现、递归删除或按名称kill。交付物保留。

本窗口五reader均在原回执登记完整argv、创建时间、父PID52200：93172/07:51:57.381823UTC；44884/07:53:40.274785UTC；90180/08:00:45.541480UTC；46412/08:03:47.815738UTC；52832/08:05:38.954276UTC。

## 最终资源追加失败及监督者接续

08:08停止新增后，第6/6短shell只计划终核上述已有5 reader、最多6级父链和20秒预算并追加本报告身份。该shell在解析期因List.Add内pipeline未加括号而失败，0.31秒/exit1；整段未执行，没有reader结果、没有finalwriter PID/完整argv登记、没有进入报告文件追加。错误是审查脚本语法，不是训练或资产错误。没有假装资源终核成功，也不再开启越过6次预算的第7次shell。本节用apply_patch追加，不启动新进程。

因此本窗口自身5 reader的最终absence和完整父链由root按上列PID、创建时间、原argv补核；本报告最终SHA也由root计算。第6次解析失败的PowerShell本体已由工具确认退出，未创建脚本中的子进程/文件流，但PID未捕获是明确证据缺口。此前真实Linux runner的资源结果保持其原证明范围，不拿它们替代审查者Windows进程终核，也不主张全机零残留。所有6次shell回执及三次审查脚本错误均保留；截至本报告，前两次源码、第三次原材料hash/读取、第4次机械数值校验前半段、第5次实际参数和runner验证各自的成功证据仍有效。
