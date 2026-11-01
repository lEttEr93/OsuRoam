using System.ComponentModel;

namespace OsuRoam.Models;

public sealed class SimpleFilterOption : INotifyPropertyChanged
{
    private SimpleFilterSelection selection;
    private readonly string englishName;

    public SimpleFilterOption(string value, string displayName)
    {
        Value = value;
        englishName = displayName;
    }

    public string Value { get; }

    /// <summary>
    /// 中文界面显示中文名，英文界面显示 osu! 自己的叫法（Ranked / Loved …），
    /// 切换语言时要广播一次，见 <see cref="NotifyLanguage"/>。
    /// </summary>
    public string DisplayName => Services.Loc.IsEnglish
        ? englishName
        : chineseDisplayName(Value) ?? englishName;

    public void NotifyLanguage() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));

    public SimpleFilterSelection Selection
    {
        get => selection;
        set
        {
            if (selection == value)
                return;
            selection = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selection)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStrict)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public bool IsSelected
    {
        get => selection != SimpleFilterSelection.None;
        set => Selection = value ? SimpleFilterSelection.Included : SimpleFilterSelection.None;
    }

    public bool IsStrict => selection == SimpleFilterSelection.Strict;

    public bool? IsChecked
    {
        get => selection switch
        {
            SimpleFilterSelection.Included => true,
            SimpleFilterSelection.Strict => null,
            _ => false
        };
        set => Selection = value switch
        {
            true => SimpleFilterSelection.Included,
            null => SimpleFilterSelection.Strict,
            _ => SimpleFilterSelection.None
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static string? chineseDisplayName(string value) => value switch
    {
        "all" => "\u5168\u90e8",
        "ranked" => "\u4e0a\u67b6\u8c31\u9762",
        "approved" => "\u8ba4\u53ef\u8c31\u9762",
        "qualified" => "\u5ba1\u6838\u4e2d",
        "loved" => "\u793e\u533a\u559c\u7231",
        "pending" => "\u5f85\u5b9a",
        "wip" => "\u5236\u4f5c\u4e2d",
        "graveyard" => "\u575f\u5893",
        "unspecified" => "\u672a\u6307\u5b9a",
        "video game" => "\u7535\u5b50\u6e38\u620f",
        "anime" => "\u52a8\u6f2b",
        "rock" => "\u6447\u6eda",
        "pop" => "\u6d41\u884c",
        "other" => "\u5176\u4ed6",
        "novelty" => "\u65b0\u5947",
        "hip hop" => "\u563b\u54c8",
        "electronic" => "\u7535\u5b50",
        "metal" => "\u91d1\u5c5e",
        "classical" => "\u53e4\u5178",
        "folk" => "\u6c11\u8c23",
        "jazz" => "\u7235\u58eb",
        "english" => "\u82f1\u8bed",
        "japanese" => "\u65e5\u8bed",
        "chinese" => "\u4e2d\u6587",
        "instrumental" => "\u7eaf\u97f3\u4e50",
        "korean" => "\u97e9\u8bed",
        "french" => "\u6cd5\u8bed",
        "german" => "\u5fb7\u8bed",
        "swedish" => "\u745e\u5178\u8bed",
        "spanish" => "\u897f\u73ed\u7259\u8bed",
        "italian" => "\u610f\u5927\u5229\u8bed",
        "polish" => "\u6ce2\u5170\u8bed",
        "russian" => "\u4fc4\u8bed",
        _ => null
    };
}

public enum SimpleFilterSelection
{
    None,
    Included,
    Strict
}
