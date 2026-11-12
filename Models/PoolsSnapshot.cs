namespace OsuRoam.Models;

/// <summary>
/// 池子调试快照：总池 / 候选池 / 推荐池三层的当前状态。
/// 只给 <see cref="Controls.PoolsDebugWindow"/> 用，字段都是「肉眼能读」的字符串，
/// 不在界面上再做计算（调试界面越笨越好，免得它自己成为变量）。
/// </summary>
public sealed record PoolsSnapshot(
    string Header,
    IReadOnlyList<PoolRow> Total,
    IReadOnlyList<PoolRow> Candidates,
    IReadOnlyList<PoolRow> Recommended,
    string TotalNote,
    string CandidateNote,
    string RecommendedNote)
{
    public static readonly PoolsSnapshot Empty = new(
        "还没有池子 —— 先点「开始漫游」",
        [], [], [], "", "", "");
}

/// <summary>调试界面里的一行（总池用简化行，候选池/推荐池用卡片）。</summary>
public sealed record PoolRow(
    string Title,
    string Subtitle,
    string Stars,
    string Tags,
    string Reason,
    string State,
    string Key)
{
    /// <summary>状态底色：normal / good / warn / dim —— 界面里映射成边框色。</summary>
    public string Tone => State switch
    {
        "达标" or "推荐中" => "good",
        "不达标" or "已给过" or "已安装" => "warn",
        _ => "normal"
    };
}
