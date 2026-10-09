# 256题扩展实验入口工程验证

日期：2026-10-09，Asia/Shanghai。256道公开样本已准备，教师完整标签仍待收齐；本项为工程验证，未执行本轮真实GPU训练。

新增显式 `train-expanded`、`validate-expanded`、`check-expanded-input`。v2输入固定六文件，完整核对原请求/响应实际字节、hash、题目/候选ID及教师目标，fixture只允许合同检查。旧v1的32题、24/8分母及768步语义保持。

新入口固定192 train /64 development、24576次GPU更新、LR0.001、seed17、strict1024、零初始化head、encoder冻结。每个训练或独立验证进程编码256原题及16条训练Choice固定左移变体，共272次encoder forward；CLI≤1500秒、SGD≤300秒、forward上限384。使用完整GPU CE和逐题logits，保存后独立进程重新编码及校对全部输出。开发集不参与参数选择。

实际工程证据根为 `.artifacts/teacher-expansion-20261008/`：

- trainer构建零警告/错误；v2合同fixture按tiny1→full16通过，实际exit0及清理通过。对应 `process-logs/20261008-113351-392-expanded-input-tiny1.result.json`、`20261008-113504-211-expanded-input-full16.result.json`。
- 原v1合同tiny1及full8全部检查通过。full8实际child exit0，但进程在CIM首次取样前已退出，wrapper如实记为CleanupFailed/unknown1，不能将其解释为完整进程审计通过；保留 `20261009-033511-275-old-v1-checks-full8-v2.result.json`。首次缺少artifact目录的失败另存，未覆盖。
- 原32题及审核sidecar的tiny1→full32绑定验证通过，记录3项修正；两次child exit0，同样保留短进程身份未取到的清理局限，不表示实际GPU回归已完成。
- 256题实际tokenizer和严格renderer检查tiny1→full256通过，256完成、0拒绝、最长914 tokens。完整报告 `lengths-full256.json`，SHA256 `385BE6F31B803E7BC83B1EC979FAA69F9E782EC552DDD1CDED569D5D735C6D45`。仅CPU tokenizer，没有encoder、特征或GPU执行。
- Linux trainer与回归工具已构建。五项trainer源码独立审查见 `trainer-independent-review.md`；源码冻结见 `source-freeze.json`。回归工具位于 `regression-check/`，其内存采样从每次JSON token调整为最多每0.5秒一次，原超时回执保留。

题目来源为MASSIVE1.1的160中英Choice与BoolQ的96英文Boolean，官方split/family冻结192/64。原32修正题单列已曝光回归。五级Score暂无核实的新金标，不新增Score监督。全部正式验收标志保持false，人审pending；工程检查、教师一致率及泛化质量不互相替代。
