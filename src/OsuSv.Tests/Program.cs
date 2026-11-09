using System.Globalization;
using System.Text;
using OsuSv;

// OsuSv 的独立冒烟测试：纯控制台、无 WPF、跨平台，CI 在 ubuntu 上跑这一份。
// 完整用例还在 osu! Roam 的 Tests/Program.cs 里（那边要 Windows + WPF）。

int failures = 0;
int checks = 0;

void Check(bool condition, string name)
{
    checks++;
    if (condition)
    {
        Console.Out.WriteLine($"ok   {name}");
        return;
    }

    failures++;
    Console.Out.WriteLine($"FAIL {name}");
}

string BuildChart(double bpm, int notes, bool withSv, int mode = 3, int keys = 4, double spacingMs = 60)
{
    StringBuilder builder = new();
    builder.Append("osu file format v14\n");
    builder.Append("\n[General]\n");
    builder.Append(CultureInfo.InvariantCulture, $"Mode: {mode}\n");
    builder.Append("\n[Difficulty]\n");
    builder.Append(CultureInfo.InvariantCulture, $"CircleSize:{keys}\n");
    builder.Append("\n[TimingPoints]\n");
    double beatLength = 60000 / bpm;
    builder.Append(CultureInfo.InvariantCulture, $"0,{beatLength:0.#####},4,2,0,100,1,0\n");
    if (withSv)
    {
        // 每 1500ms 在 1x / 2x / 0.5x 之间跳一次（负 beatLength = 继承点，-50 ≈ 2 倍速）。
        double[] multipliers = [-100, -50, -200];
        for (int index = 0; index < 40; index++)
        {
            double time = 1500 + index * 1500;
            builder.Append(CultureInfo.InvariantCulture, $"{time:0.###},{multipliers[index % multipliers.Length]},4,2,0,100,0,0\n");
        }
    }

    builder.Append("\n[HitObjects]\n");
    int[] columns = [64, 192, 320, 448];
    for (int index = 0; index < notes; index++)
    {
        double time = 200 + index * spacingMs;
        int column = columns[index % columns.Length];
        builder.Append(CultureInfo.InvariantCulture, $"{column},192,{time:0.###},1,0,0:0:0:0:\n");
    }

    return builder.ToString();
}

SvMetrics? steady = Evaluator.Compute(BuildChart(bpm: 180, notes: 120, withSv: false));
SvMetrics? jumpy = Evaluator.Compute(BuildChart(bpm: 180, notes: 120, withSv: true));

Check(Evaluator.Compute("") is null, "空文本返回 null");
Check(Evaluator.Compute("not an osu file") is null, "非 .osu 文本返回 null");
Check(Evaluator.Compute(BuildChart(180, 120, false, mode: 0)) is null, "非 mania（Mode: 0）返回 null");
Check(Evaluator.Compute(BuildChart(180, 120, false, keys: 0)) is null, "CircleSize 为 0 返回 null");
Check(Evaluator.Compute(BuildChart(180, notes: 10, withSv: false)) is null, "音符行不足 40 返回 null");
Check(Evaluator.Compute(BuildChart(180, 120, false), scrollSpeed: 0) is null, "流速 <= 0 返回 null");

Check(steady is not null, "匀速谱面算得出来");
Check(jumpy is not null, "带 SV 的谱面算得出来");

if (steady is not null && jumpy is not null)
{
    Check(Math.Abs(steady.ScreenTimeMs - Evaluator.MaxTimeRange / Evaluator.DefaultScrollSpeed) < 1e-6,
        "标称一屏用时 = 11485 / 流速");
    Check(steady.Extremeness < 0.01, "匀速谱面的极端度为 0");
    Check(Math.Abs(steady.MedianFlightMs - steady.ScreenTimeMs) < 1, "匀速谱面飞行中位数 ≈ 标称值");
    Check(steady.Rows >= 40, "行数 >= 40");
    Check(jumpy.Irregularity > steady.Irregularity, "跳变谱面的不等时高于匀速");
    Check(jumpy.Memorization >= steady.Memorization, "跳变谱面的背谱度不低于匀速");
    Check(jumpy.Discomfort > steady.Discomfort, "跳变谱面的不适度高于匀速");

    SvMetrics? again = Evaluator.Compute(BuildChart(bpm: 180, notes: 120, withSv: true));
    Check(again is not null && Math.Abs(again.Irregularity - jumpy.Irregularity) < 1e-12
          && Math.Abs(again.Discomfort - jumpy.Discomfort) < 1e-12, "同一份文本两次结果一致");

    // 流速只影响标称值，不该影响「结构」类指标的方向。
    SvMetrics? fast = Evaluator.Compute(BuildChart(bpm: 180, notes: 120, withSv: true), scrollSpeed: 40);
    Check(fast is not null && Math.Abs(fast.ScreenTimeMs - Evaluator.MaxTimeRange / 40) < 1e-6, "换流速后标称值跟着变");
}

Console.Out.WriteLine($"\n{checks - failures}/{checks} 通过");
return failures == 0 ? 0 : 1;
