namespace OsuRoam.Services;

/// <summary>
/// 谱面预览的时钟：把「tosu 每隔几十毫秒报一次的预览音频位置」变成每帧平滑前进的时间。
///
/// 直接拿快照里的毫秒数当帧时间，画面会一格一格跳（tosu 不是每帧都发）。
/// 所以这里记下「值 + 到达时刻」，每帧按墙上时间往前推，再用 15% 的力度往预测值上拉：
/// <list type="bullet">
/// <item>差值在 200ms 以内：正常滑动 + 轻微纠偏，肉眼看不到修正；</item>
/// <item>差值超过 200ms（换歌、预览跳回去、玩家重新开始预览）：直接对齐，不做平滑；</item>
/// <item>超过 200ms 没新值（预览音频暂停 / tosu 断流）：停止前进，画面停在原地。</item>
/// </list>
///
/// 抽出来单独一个类是为了能离线测：窗口里的渲染循环没法在单元测试里跑，这里的数学可以。
/// </summary>
public sealed class PreviewClock
{
    /// <summary>这么久没有新值就算「停更」，画面不再自己往前跑（毫秒）。</summary>
    public const double StallThreshold = 200;

    /// <summary>预测值和本地时间差这么多就硬对齐，别慢慢磨（毫秒）。</summary>
    public const double SnapThreshold = 200;

    /// <summary>每帧把差值补掉的比例。</summary>
    public const double Pull = 0.15;

    private double sourceTime;
    private double lastArrival;
    private bool reset = true;

    /// <summary>画面该用的时间（毫秒）。</summary>
    public double Time { get; private set; }

    /// <summary>tosu 最后报的位置（毫秒）。</summary>
    public double SourceTime => sourceTime;

    /// <summary>收到过 tosu 的位置没有。</summary>
    public bool HasSource { get; private set; }

    /// <summary>换歌：下一个 <see cref="Sync"/> 直接落到该去的位置，不从上一首滑过去。</summary>
    public void Reset()
    {
        reset = true;
        HasSource = false;
    }

    /// <summary>灌一条 tosu 的位置。<paramref name="now"/> 是同一条时间轴上的当前时刻。</summary>
    public void Sync(double milliseconds, double now)
    {
        if (double.IsNaN(milliseconds) || milliseconds < 0)
            return;

        sourceTime = milliseconds;
        lastArrival = now;
        HasSource = true;

        if (reset)
        {
            Time = milliseconds;
            reset = false;
        }
    }

    /// <summary>推一帧：<paramref name="delta"/> 是距上一帧的毫秒数。</summary>
    public void Advance(double now, double delta)
    {
        if (!HasSource)
        {
            Time += delta;
            return;
        }

        double stale = now - lastArrival;
        if (stale > StallThreshold)
        {
            // 停更：停在原地，别自己往前跑。这里**不能**回退到 sourceTime ——
            // 那个值本来就是旧的，回退等于画面倒退两三百毫秒。
            return;
        }

        double predicted = sourceTime + stale;
        double drift = predicted - Time;

        if (drift < -SnapThreshold)
        {
            // 源比画面落后一大截：这是真的「跳回去了」（预览音频重播 / 玩家重新开始），
            // 只有这种情况允许倒退。
            Time = predicted;
        }
        else if (drift > SnapThreshold)
        {
            // 画面落在源后面太多：直接对齐（向前）。
            Time = predicted;
        }
        else if (drift > 0)
        {
            Time += delta + drift * Pull;
        }
        else
        {
            // 源比预测慢一点（采样抖动、样本迟到）：只按墙上时间前进，**绝不回退**。
            // 以前这里会用 15% 的力度往回收，结果就是画面上「倒回去一点点」，
            // 每次样本迟到都抽一下 —— 这才是肉眼看到的那个抖动。
            Time += delta;
        }
    }
}
