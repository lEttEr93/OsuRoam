using System.Globalization;
using System.Windows;

namespace OsuRoam.Services;

public enum UiLanguage
{
    System,
    Chinese,
    English
}

/// <summary>
/// 界面语言。
///
/// 文案表在 <see cref="LocTable"/>；<see cref="Apply(UiLanguage)"/> 会把它包成一个
/// <see cref="ResourceDictionary"/>（键名加 <c>L.</c> 前缀）塞进
/// <c>Application.Resources</c>，于是 XAML 里 <c>{DynamicResource L.btn.next}</c>
/// 在换语言时由 WPF 自己重新解析，不用重建窗口，也不会闪一下中文。
///
/// 代码里拼的文字走 <see cref="T"/>/<see cref="F"/>；换语言后由各窗口的
/// <c>ApplyLanguage()</c> 重算（见 <see cref="Changed"/>）。
/// </summary>
public static class Loc
{
    public const string AutoTag = "auto";
    public const string ChineseTag = "zh";
    public const string EnglishTag = "en";

    private static ResourceDictionary? dictionary;

    /// <summary>设置里存的那个选项。</summary>
    public static UiLanguage Setting { get; private set; } = UiLanguage.System;

    /// <summary>实际生效的语言：<c>zh-CN</c> 或 <c>en-US</c>。</summary>
    public static string Code { get; private set; } = "zh-CN";

    public static bool IsEnglish => Code.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    /// <summary>语言真的换了才触发；窗口在这里重算自己拼的字符串。</summary>
    public static event Action? Changed;

    public static IReadOnlyDictionary<string, string> Table => IsEnglish ? LocTable.English : LocTable.Chinese;

    public static UiLanguage Parse(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "zh" or "zh-cn" or "zh-hans" or "chinese" => UiLanguage.Chinese,
        "en" or "en-us" or "english" => UiLanguage.English,
        _ => UiLanguage.System
    };

    public static string ToTag(UiLanguage language) => language switch
    {
        UiLanguage.Chinese => ChineseTag,
        UiLanguage.English => EnglishTag,
        _ => AutoTag
    };

    /// <summary>跟随系统：非中文系统就用英文，这样英文用户第一次开箱就是英文。</summary>
    public static string ResolveCode(UiLanguage setting) => setting switch
    {
        UiLanguage.Chinese => "zh-CN",
        UiLanguage.English => "en-US",
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase)
            ? "zh-CN"
            : "en-US"
    };

    public static void Apply(string? value) => Apply(Parse(value));

    public static void Apply(UiLanguage language)
    {
        Setting = language;
        Code = ResolveCode(language);

        ResourceDictionary next = buildDictionary(Code);
        ResourceDictionary? previous = dictionary;
        dictionary = next;

        if (Application.Current is { } application)
        {
            // 先加新的再删旧的：中间不会出现「键没了」的一帧，界面上不会闪空字符串。
            application.Resources.MergedDictionaries.Add(next);
            if (previous is not null)
                application.Resources.MergedDictionaries.Remove(previous);
        }

        Changed?.Invoke();
    }

    public static string T(string key)
    {
        if (Table.TryGetValue(key, out string? value))
            return value;
        // 漏翻时不要显示裸键名，退到中文比退到 "btn.next" 有用得多。
        return LocTable.Chinese.TryGetValue(key, out string? fallback) ? fallback : key;
    }

    public static string F(string key, params object?[] args)
    {
        string format = T(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    /// <summary>
    /// 技能轴来源的显示名。库里存的是 <c>MinaCalc 23.1</c> 或 <c>本地估算</c>，
    /// 拉丁版本本来就是给人看的，中文的「本地估算」才有英文对应。
    /// </summary>
    public static string SkillSourceText(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return string.Empty;
        return source.StartsWith("MinaCalc", StringComparison.OrdinalIgnoreCase)
            ? source
            : T("skill.estimated");
    }

    /// <summary>6 个核心键型占比条上的标签（全长）。</summary>
    public static IReadOnlyList<string> CorePatternLabels() =>
    [
        T("pattern.stream"), T("pattern.chordstream"), T("pattern.jacks"),
        T("pattern.coordination"), T("pattern.density"), T("pattern.misc")
    ];

    /// <summary>7 轴技能雷达的标签，要短到能塞进 132px 的图里。</summary>
    public static IReadOnlyList<string> SkillPatternLabels() =>
    [
        T("skill.stream"), T("skill.jumpstream"), T("skill.handstream"), T("skill.stamina"),
        T("skill.jackspeed"), T("skill.chordjack"), T("skill.technical")
    ];

    private static ResourceDictionary buildDictionary(string code)
    {
        ResourceDictionary result = new();
        IReadOnlyDictionary<string, string> table = code.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? LocTable.English
            : LocTable.Chinese;
        foreach ((string key, string value) in table)
            result["L." + key] = value;
        return result;
    }
}
