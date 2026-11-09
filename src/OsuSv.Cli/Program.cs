using System.Globalization;
using System.Text.Json;
using OsuSv;

// osu-sv：命令行版 SV 评估。
//
//   osu-sv compute chart.osu [--scroll 25] [--json]
//   osu-sv compute -            # 从 stdin 读 .osu 文本
//
// 退出码：0 成功；2 输入不是 mania 谱面/数据不够（此时 stderr 有原因）；1 用法或 IO 错误。

if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
{
    Console.Out.WriteLine("""
        osu-sv —— osu! mania 谱面的 SV 评估

          osu-sv compute <chart.osu|-> [--scroll <n>] [--json]

            --scroll <n>   游戏内流速（默认 25），决定标称一屏用时 11485/n 毫秒
            --json         输出 JSON（默认是人读的一行行）

        osu-sv version
        """);
    return 0;
}

if (args[0] == "version")
{
    Console.Out.WriteLine($"{typeof(Evaluator).Assembly.GetName().Version}");
    return 0;
}

if (args[0] != "compute")
{
    Console.Error.WriteLine($"未知子命令：{args[0]}（试试 osu-sv --help）");
    return 1;
}

string? source = null;
double scrollSpeed = Evaluator.DefaultScrollSpeed;
bool asJson = false;

for (int index = 1; index < args.Length; index++)
{
    switch (args[index])
    {
        case "--json":
            asJson = true;
            break;
        case "--scroll":
            if (index + 1 >= args.Length)
            {
                Console.Error.WriteLine("--scroll 后面要跟一个数");
                return 1;
            }
            if (!double.TryParse(args[++index], NumberStyles.Float, CultureInfo.InvariantCulture, out scrollSpeed) || !(scrollSpeed > 0))
            {
                Console.Error.WriteLine("--scroll 需要一个正数");
                return 1;
            }
            break;
        default:
            if (args[index].StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"未知参数：{args[index]}");
                return 1;
            }
            source = args[index];
            break;
    }
}

if (source is null)
{
    Console.Error.WriteLine("要指定谱面路径（或者用 - 从 stdin 读）");
    return 1;
}

string text;
try
{
    text = source == "-" ? Console.In.ReadToEnd() : File.ReadAllText(source);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"读不到 {source}：{exception.Message}");
    return 1;
}

SvMetrics? metrics = Evaluator.Compute(text, scrollSpeed);
if (metrics is null)
{
    Console.Error.WriteLine("算不出来：不是 mania 谱面（需要 Mode: 3 和 CircleSize），或者有效音符行 < 40、没有时间点。");
    return 2;
}

if (asJson)
{
    Console.Out.WriteLine(JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

static string Row(string label, double value) =>
    string.Create(CultureInfo.InvariantCulture, $"{label,-14}{value,8:0.000}");

Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"标称一屏      {metrics.ScreenTimeMs,8:0} ms（流速 {scrollSpeed:0.##}）"));
Console.Out.WriteLine(Row("不等时", metrics.Irregularity));
Console.Out.WriteLine(Row("极端度", metrics.Extremeness));
Console.Out.WriteLine(Row("背谱度", metrics.Memorization));
Console.Out.WriteLine(Row("不适度", metrics.Discomfort));
Console.Out.WriteLine(Row("夸张度", metrics.Exaggeration));
Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"行数          {metrics.Rows,8}｜飞行中位数 {metrics.MedianFlightMs:0} ms"));
return 0;
