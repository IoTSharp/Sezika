# T7：具名历史配方裁定与能力替代分别验收

`inventory review` 可选接收 `HistoricalArchiveDecisions`。每条裁定固定一个现存原件的 bytes/SHA、具体用途、理由和独立确认；用途与理由须有实质文本且不含控制字符。重复、字节变化、外部技能、既有未跟踪材料，以及同一原件另有能力替代门，均拒绝。现行调用、不完整 caller 范围和歧义仍会阻断就绪。

报告保留冻结分类的原 `Disposition`，以 `ReviewBasis` 明确历史配方裁定与能力替代门。具名旧配方退出执行不意味着 C# 复现其全部历史语义；本命令不执行、移动或删除原件。Windows 和 Ubuntu WSL 的当前 Native AOT 检查均先 tiny1，再完整19项通过，覆盖20个独立 fixture。准确两源、原始执行回执和完整输出见[验收清单](tooling-retirement-archive-2026-10-14/acceptance.json)。

本次独立逐项审查75个固定旧对象/故障阶段配方，另保留安装、参数化发布和远端发布三个长期入口。结合原分类中184份有准确 Git 原件保全的历史资料，形成[259条具名裁定输入](tooling-retirement-bindings-2026-10-14/historical-archive-decisions.json)，原件共1,306,389字节；计入三个保留入口的全部262份读回为1,334,883字节。[逐配方理由](tooling-retirement-bindings-2026-10-14/root-specific-recipe-decisions.json)保留原用途、行为锚点与分开的长期能力条件。5份既有未跟踪材料当前均存在，其可信历史基线继续 UNKNOWN，不纳入这些裁定。

实际 C# 检查器在已冻结 v7 范围中读取63个命中文件，完整扫描证据另覆盖472文件。Root 对保守检测器实际要求的44行逐一绑定完整文件/正文SHA；包括用例名称中的 `CPython` 子串误报，未作关键字自动豁免。结果是466项中259项历史配方审查就绪、207项能力或既有材料门待绑定，零歧义；顶层仍为 `PENDING_review_incomplete_no_mutation`。后续源码、说明或行号变化须重新钉受影响输入。这不是全量迁移或服务器验收完成。
