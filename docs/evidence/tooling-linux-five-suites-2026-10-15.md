# 五套旧 Linux suite 的 v8 补验

2026-10-14 UTC，冻结 v8 Native AOT Tests 对五套旧 suite 分别先 tiny1、再 full：task 1/11、bundle-inputs 1/13、controller-review 1/14、oracle 1/5、corpus 1/38，10 个 client 全部 PASS。完整套共81项，包含重复 tiny 的实际检查执行共86次；不是86个不同测试。

| suite | tiny run | full run | full检查数 |
| --- | --- | --- | --- |
| task | 01 | 02 | 11 |
| bundle-inputs | 03 | 04 | 13 |
| controller-review | 05 | 06 | 14 |
| oracle | 07 | 08 | 5 |
| corpus | 09 | 10 | 38 |

每个 client 的原 Linux/Windows 执行回执均为 exit0、SUCCEEDED、EOF完整、cleanup PASS、remaining0。正确的 v8 CLI/broker、显式 nested broker SHA、CPU2、单client MP64 和完整11份 fixture cwd 已绑定。未测量实际 live peak，不能由64上限反推。

测试源码五份已独立与 v7 原件逐 SHA 核对一致；v8 只含既有四修。源清单 SHA 为 `ab3d882eeb218d6f458ba61951ee7e3403f62b9f47cc9fb9ae371d32fabcfc5c`，Linux CLI SHA 为 `45f34ada583e19df43722df7297c526a0c4b5f3e0ce37bd063622e6f85480e14`，Tests SHA 为 `eca91bc5bc6a79eff7b629daf52dc69a91ee84c02fa23002442278e3193f22bd`。123项源/runtime/fixture在运行前后完整读取一致，零漂移。

两次真实 C# process audit 仅审具名 native 执行目录：第一组5目录/5真实回执/10身份，第二组6目录/6真实回执/12身份（含第一audit的执行目录），均 live0、unknown0。随后对24份内外实际回执里的108个已记录进程身份作PID/birth/ticks/boot/argv/父链回读，匹配存活0、UNKNOWN0；未执行终止，不碰shared/reused进程。诊断 shell 的退出补核另见 `final-inspection-exit-check.json`，它不宣称准备期全局诊断进程历史完全齐全。

本结果精确映射旧 pending19 表中的10条Linux行，原NOT_RUN及全部旧FAIL保持；其余9条不属于本片覆盖范围，不据此判断它们当前是否仍pending。这里的旧corpus38不替代新 `corpus-migration-native20` / `internal1`。Oracle5是离线编排模拟，五套均不构成真实模型、教师、GPU、语言质量、训练或生产验收。

证据目录为 `C:\Users\mysti\Documents\Codex\Sezika-tooling-artifacts-20261014\linux-five-old-suites-v8-closure-v1`。原始 `runs/01..10/{request.json,result.json,outer-result.json,summary.json}`、`audits/01..02`、两组新Linuxaudit输出、`actual-receipt-pins.json`、`final-source-runtime-fixture-pins.json`、`pending19-ten-row-new-evidence.json` 和 `current-native-process-readback.json` 都保留。独占 Linux 根 `/tmp/sezika-five-old-v8-corpus-20261014-160831` 保留交付物；未改 canonical/Git、未运行SDK/模型/GPU/平台服务，也未新增脚本或源码。

Root于2026-10-15（Asia/Shanghai）核交付，具名原件副本及SHA见 [验收清单](tooling-linux-five-suites-2026-10-15/acceptance.json)。这是T8-code的独立补验切片，不代表全量完成。
