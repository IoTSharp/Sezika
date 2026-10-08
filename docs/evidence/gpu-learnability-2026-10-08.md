# 同24题 GPU 可学性与候选位置诊断

2026-10-08，固定数据/模型/旧权重身份见[执行交接](../training-next-handoff-2026-10-08.md)。评价工程已独审并提交 `2111ed9`。本轮没有重新采集教师或扩大数据，24train/8已曝光development保持不变；标签仍未人工审核，正式许可发布/质量/校准/封存/AOT/性能未通过。

## A：旧72步权重只读评价

单次实际CUDA编码train24及8条训练Choice固定左循环变体，32/32 encoder forward。原数据、候选对象及目标ID完整保留；换序题重新构造token/marker并真实GPU编码。旧report/head/checkpoint与前置固定hash相同，不写旧产物目录。GPU logits和完整GPU CE实际执行，没有参数更新。

train24教师一致10/24：Choice2/8，Score4/8，Boolean4/8；完整GPU CE `1.1749068026741345`，与旧报告相同。训练Choice8原顺序与换序后都预测渲染顺序的首候选，因此8/8预测ID改变；换序教师一致2/8、CE `1.3898979127407074`。这些事实表明旧head在训练题上未充分拟合且Choice明显位置敏感；不能用“标签多数类”解释平衡的训练Choice位置，也不能单凭本样本判定encoder表示能力不足。

train原题zh5/12、en5/12；Choice分别1/4、1/4，Score3/4、1/4，Boolean2/4、2/4。换序Choice中英各1/4。全部逐题logits/probability/目标排名与真实token/marker/feature身份在[原字节A报告](gpu-learnability-20261008/stage-a-evaluation.json)，报告SHA `B3868C86946B8E283B12C6B0DFA7D77A5BA895E5B524940A326BE0FF09B51082`。未经校准的概率与教师一致率不表示金标正确率。

Linuxrunner actual completed/exit0、22,270ms（清理3,660ms），complete1/live0/unknown0/unchecked0/incomplete0/budget0；Windows PID72736 actual Succeeded0/27.409秒。GPU owned峰值1,309,369,952 bytes、host峰值2,723,254,272 bytes，低于3GiB/4GiB；device/model Dispose返回，owned bytes/alloc/modules/release failures全零。独立Linux后核runner-tiny及stage-a的8条已登记身份（含重复）全absent，可用显存仍15294MiB；不声明全机无进程。旧报告原hash再次核相同。

## B/C 固定新实验

待执行：零初始化新三行head、768次SGD（32×24）、LR0.001、seed17、strict1024、encoder冻结、不resume。dev8只诊断，不选参数；A结果不改变这个预先固定配置。保存真实head/checkpoint/训练与评价报告后，由独立新进程从磁盘加载，重新GPU编码、完整CE和train24/dev8/换序8对比。每阶段一次，上限72 encoder forwards及既定资源/墙钟；不自动加训或改变架构。
