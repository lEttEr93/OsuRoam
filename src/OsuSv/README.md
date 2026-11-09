# OsuSv

osu! mania 谱面的**变速（SV）评估**：把滚动速度的变化拆成几个互相独立的量，而不是合成一个分数。

```csharp
using OsuSv;

SvMetrics? metrics = Evaluator.Compute(File.ReadAllText("chart.osu"), scrollSpeed: 25);
if (metrics is null)
    Console.WriteLine("不是 mania 谱面，或者音符/时间点不够");
else
    Console.WriteLine($"不等时 {metrics.Irregularity:0.00}｜极端度 {metrics.Extremeness:0.00}｜" +
                      $"背谱度 {metrics.Memorization:0.00}｜不适度 {metrics.Discomfort:0.00}");
```

## 输入与输出

- 输入：`.osu` 文件的**文本**（不是路径，不碰磁盘）。要求 `Mode: 3`（mania）、`CircleSize` > 0、至少 40 个有效音符行、至少 1 个时间点。
- 输出：`SvMetrics?`（不满足条件返回 `null`，不抛异常）：
  - `Irregularity` 不等时（负向）：切换频率 + 1 秒局部飞行时长带宽；
  - `Extremeness` 极端度（负向）：飞行时长偏离标称值多远（±10% 容忍带外才计）；
  - `Memorization` 背谱度（正向）：`A² + (1−A)·B`；
  - `Discomfort` 不适度（正向）：切换时「幅度 × 当时在飞的音符数」累计；
  - `Exaggeration` 夸张度：飞行途中相对自身平均速度的偏离积分。
- 只做 mania，不做 DT/HT/键数转换；BPM 绝对大小被约掉，谱内改 BPM 与改 SV 等价。

完整口径（速度模型、飞行时长反解、四个量的推导）：见仓库里的 [`docs/1.5.3-SV评估算法.md`](https://github.com/lEttEr93/OsuRoam/blob/main/docs/1.5.3-SV%E8%AF%84%E4%BC%B0%E7%AE%97%E6%B3%95.md)。

## 命令行版

```bash
dotnet tool install --global OsuSv.Cli      # 或者用 Release 里的单文件 exe
osu-sv compute chart.osu --scroll 25 --json
```

## 许可

MIT。这个库是 [osu! Roam](https://github.com/lEttEr93/OsuRoam) 的附带产品，同一套代码同时被 Roam 用于推荐池的键型/SV 标注。
