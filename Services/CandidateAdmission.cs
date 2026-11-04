using OsuRoam.Models;

namespace OsuRoam.Services;

/// <summary>
/// 候选准入的门槛：勾了键型 / 「只要面谱」时，一张谱必须先被解析出真实键型才能进候选，
/// 这里集中放判定口径，避免 UI 和排序各写一份。
/// </summary>
public static class CandidateAdmission
{
    /// <summary>
    /// 所选键型占比达到这个值才算「达标候选」。0.25 与预取命中口径、卡片上「叠键 ××%」的
    /// 可辨识下限一致；低于它的谱只作为候选不足时的备用。
    /// </summary>
    public const double StrictShareFloor = 0.25;

    /// <summary>
    /// 这张谱算不算「键型达标」。勾了具体键型看所选占比；「只要面谱」看是否 LN 谱。
    /// 没有解析出真实键型（pattern 为 null）一律不算达标。
    /// </summary>
    public static bool IsStrict(ManiaPattern? pattern, AppSettings settings)
    {
        if (pattern is null)
            return false;
        if (settings.SelectedPatterns.Count > 0)
            return pattern.SelectedShare(settings.SelectedPatterns) >= StrictShareFloor;
        return pattern.IsLnChart;
    }
}
