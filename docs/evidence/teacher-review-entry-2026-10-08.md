# 32 题教师建议人工审核入口证据

2026-10-08。本项从已经验收的 v2 GPU 教师结果离线生成审核材料，没有新增教师、网络、GPU 或训练调用。结果是可填写的审核入口，人工审核及正式训练准入仍未完成。

## 产物与语义

`datasets/teacher-general-v2-review/` 共五文件：有界生成器、README、完整逐题 Markdown、JSONL 填写模板和 hash manifest。包含实际 32 题原文、instruction、全部候选、Boolean statement 和已经取得的教师答案；中英各 Choice 6 / Score 5 / Boolean 5，共 32 个不同 family。

八个人工字段全部 null：reviewer、reviewer_id、reviewed_at_utc、independent_label、decision、correction、notes、teacher_suggestion_seen。32 条均 pending；training_admission、quality_evidence、output_training_permitted 为 false。teacher_answer_exposed=true、eligible_as_sealed_test=false；折叠教师建议不构成盲评或独立金标。已曝光题及 family 不能用于封存测试。

填写时在模板副本保存真实判断，保留来源和教师建议；Score 使用从零开始的字符串 ID，Boolean 使用字符串 `false` / `true`。本包不自动导入训练或填写批准，不把模型标称许可等同于已核实输出训练许可。

## 身份与验证

固定来源为 `.artifacts/teacher-gpu-prompt-v2-20261008/simple-run-20261007-2019/result-all-32/`。records 54,198 bytes，SHA-256 `deb5d8ec8e2c8f1d3f772c42cde1bde9f6141e975d4b20e942b52e1ab09a08e4`；manifest 2,019 bytes，SHA-256 `5151fa98f0496e76c4a9b6cd64f6230792f078e695aae7311e41300b556093c2`。完整生成实际核 64 个 request/response 原字节引用，不重新采集。

| 文件 | bytes | SHA-256 |
| --- | ---: | --- |
| Build-Review.ps1 | 23491 | `1b47186826ae22d9592440cc08d47002bba4bffe80e647ae3506b2900ebbed96` |
| README.md | 2785 | `4aa9410ab19a9386c5a87156015092985bbb2daebafb6ffefe920066148858ad` |
| review.md | 47785 | `2027e7a5aebb98cda357a5f9a1b9965303231fdfeb0b1296377e8f45c4e8eb14` |
| review-template.jsonl | 68166 | `f5967fa557d60136c29a9b65489607801e1fccf4d1e10da9cff7bcda7a9fd4c0` |
| manifest.json | 16534 | `39281c4f184dd20af59338a6e49b9607ea71ddb6111d5b922ebc18fd79d7c396` |

作者先 tiny 1 再 full 32，补明确曝光标记后再次 full 32，均成功；首版精确核归属后移入 task artifact 保留，没有覆盖填写文件。生成器最多 32 记录、64 引用、120 秒；Guard 核取消 token/文件和墙钟，每题报告进度。目录祖先最多 16 层，输入路径/字节/hash 与候选顺序均核验，目标存在则拒绝；GUID staging 完整后才原子 Move，finally 只清理精确所属 staging。

独立审查限定五包文件及三既有证据，实际保存 UTC 23:16:02，早于 23:16:29 截止。初大输出截断后仅对同三文件补紧凑 tiny/full 验证；32 条 Markdown/template 字段、全部候选数量、八个 null 字段和状态全部一致，无未解决工程发现。独立审查不重算 64 引用或判断教师答案；原字节核验沿用已通过作者执行证据。监督者另 tiny 1/full 5 复核五个交付文件，UTC 23:18:43 全部原字节/长度匹配，没有重新生成。

作者证据 `.artifacts/teacher-review-20261008/verification.json` SHA-256 `6BAC9FF5966CE0D5FAB2E134900B1E4423FFF809C5D13BC98CFDD188E554A212`；独立审查 `independent-review.md` SHA-256 `38E00DA969C5AFE377CEB4F3C40F8EDB129E3A9842F85CFF5579A417D992B226`。监督者复核见 `root-package-identity-5.json`。该目录在 .gitattributes 使用精确 -text，保持 UTF-8 无 BOM / LF 的 hash 身份；review.md 的生成段落有意保留末尾空行，单文件 whitespace 规则允许该空行，未改已核原字节。暂存的五个 Git blob 与工作树原字节 hash-object 一致。

作者 UTC 23:09:03 精确核七 PID 全退出，stage 残留零；独立审查没有创建临时文件、服务或下载，短读取均 exit 0。监督者两复核 runner PID 54000/96148 成功、cleanup 为空。已单独提交`c386d29`；UTC23:22:02 root后核本项与adoption的七Git/复验回执、14身份，无匹配存活（`root-receipt-closure-7.json`）；最后两审计PID25000/9820亦后核不存在。所有原失败/首版/probe/最终包保留，不清理用户文件、共享缓存或整个 TEMP。工程入口关闭不代表真实逐条审核、独立封存、许可审签、训练、质量或校准验收。
