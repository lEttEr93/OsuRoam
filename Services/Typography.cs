using System.Windows;
using System.Windows.Media;

namespace OsuRoam.Services;

/// <summary>
/// 界面字体。
///
/// 内置的是 <b>Outfit</b>（SIL OFL 1.1，见 <c>Fonts/ATTRIBUTION.md</c>）：
/// 几何无衬线，观感接近 osu!(lazer) 的 Torus，但可以随程序自由分发。
/// 三个字面 Regular / SemiBold / Bold 由可变字体切出来（<c>Fonts/*.ttf</c>），
/// 以 WPF <c>Resource</c> 编进程序集。
///
/// Outfit 没有汉字字形：中文由 WPF 逐字符回退到 <see cref="SystemFontName"/>，
/// 和 osu!lazer 用 Noto 补 CJK 是一个思路。
/// </summary>
public static class Typography
{
    /// <summary>程序集内字体所在目录（pack URI）。</summary>
    public const string EmbeddedFontsUri = "pack://application:,,,/Fonts/";

    /// <summary>字体家族名，必须和 ttf 里 name 表的 typographic family（id 16）一致。</summary>
    public const string FamilyName = "Outfit";

    /// <summary>没有汉字字形时落到的系统字体。</summary>
    public const string SystemFontName = "Microsoft YaHei UI";

    /// <summary><c>App.xaml</c> 里的字体资源键，所有控件样式用 DynamicResource 引用它。</summary>
    public const string ResourceKey = "UiFont";

    /// <summary>用来判断「字体真的加载到了」的探针字符。</summary>
    private const char LatinProbe = 'o';

    private static readonly Lazy<bool> embeddedAvailable = new(probeEmbeddedFont, isThreadSafe: true);
    private static FontFamily? embeddedFamily;
    private static FontFamily? systemFamily;
    private static bool usingEmbeddedFont = true;

    /// <summary>内置字体是否可用（文件缺失或家族名对不上时为 false）。</summary>
    public static bool EmbeddedAvailable => embeddedAvailable.Value;

    /// <summary>当前是否在用内置字体。</summary>
    public static bool UsingEmbeddedFont => usingEmbeddedFont && EmbeddedAvailable;

    /// <summary>内置字体家族。</summary>
    public static FontFamily Embedded => embeddedFamily ??= loadEmbedded();

    /// <summary>系统回退字体。</summary>
    public static FontFamily SystemFont => systemFamily ??= create(SystemFontName);

    /// <summary>当前生效的字体家族。</summary>
    public static FontFamily Current => UsingEmbeddedFont ? Embedded : SystemFont;

    /// <summary>
    /// 切换字体并立刻应用到已经建好的界面（<see cref="ResourceKey"/> 是 DynamicResource，
    /// 换资源会直接刷新所有引用它的控件）。
    /// </summary>
    public static void Apply(bool preferEmbeddedFont)
    {
        usingEmbeddedFont = preferEmbeddedFont && EmbeddedAvailable;
        if (Application.Current is { } application)
            application.Resources[ResourceKey] = Current;
    }

    /// <summary>给日志用的一行说明。</summary>
    public static string Describe()
    {
        if (!EmbeddedAvailable)
            return $"字体：内置 {FamilyName} 不可用（{EmbeddedFontsUri} 没加载出来），回退 {SystemFontName}";

        return usingEmbeddedFont
            ? $"字体：内置 {FamilyName}{glyphDescription(Embedded)}；汉字回退 {SystemFontName}"
            : $"字体：{SystemFontName}（设置里关掉了内置字体）";
    }

    /// <summary>启动时自检：内置字体没加载出来就自动退回系统字体，至少文字可读。</summary>
    public static void EnsureUsable()
    {
        if (usingEmbeddedFont && !EmbeddedAvailable)
        {
            AppLog.Info($"内置 {FamilyName} 没有加载出来，本次改用系统字体");
            usingEmbeddedFont = false;
            if (Application.Current is { } application)
                application.Resources[ResourceKey] = SystemFont;
        }
    }

    private static bool probeEmbeddedFont()
    {
        try
        {
            if (!new Typeface(Embedded, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal)
                    .TryGetGlyphTypeface(out GlyphTypeface glyphTypeface))
            {
                AppLog.Info($"字体：{EmbeddedFontsUri} 没有解析出字形面");
                return false;
            }

            bool ok = glyphTypeface.CharacterToGlyphMap.ContainsKey(LatinProbe);
            AppLog.Info(ok
                ? $"字体：{FamilyName} 已加载（{glyphTypeface.GlyphCount} 字形，家族名 {string.Join('/', glyphTypeface.FamilyNames.Values)}）"
                : $"字体：{FamilyName} 缺少探针字符，判定加载失败");
            return ok;
        }
        catch (Exception exception)
        {
            AppLog.Info($"字体：{FamilyName} 加载异常 {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private static string glyphDescription(FontFamily family)
    {
        try
        {
            return new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal)
                .TryGetGlyphTypeface(out GlyphTypeface glyphTypeface)
                ? $"（{glyphTypeface.GlyphCount} 字形）"
                : "（字形面未知）";
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 从程序集里取字体家族。
    ///
    /// 注意：**不能**直接 <c>new FontFamily("pack://application:,,,/Fonts/#Outfit")</c>。
    /// 那样建出来的家族 BaseUri 是空的，<see cref="Typeface.TryGetGlyphTypeface"/> 一律返回 false
    /// （实测 pack URI、<c>/Fonts/#Outfit</c>、<c>./Fonts/#Outfit</c>、文件 URI 等 9 种写法全失败，
    /// 但同一个 <c>GetFontFamilies</c> 却能正常列出 <c>./#Outfit</c>）。
    /// 所以走 <see cref="Fonts.GetFontFamilies(Uri)"/>，它返回的是带 BaseUri 的家族。
    /// </summary>
    private static FontFamily loadEmbedded()
    {
        try
        {
            ICollection<FontFamily> families = Fonts.GetFontFamilies(new Uri(EmbeddedFontsUri));
            FontFamily? family = families.FirstOrDefault(candidate =>
                candidate.FamilyNames.Values.Any(name => name.Equals(FamilyName, StringComparison.OrdinalIgnoreCase)));
            if (family is not null)
                return family;

            AppLog.Info($"字体：{EmbeddedFontsUri} 里没有 {FamilyName}");
        }
        catch (Exception exception)
        {
            AppLog.Info($"字体：读取 {EmbeddedFontsUri} 失败 {exception.GetType().Name}: {exception.Message}");
        }

        return new FontFamily(FamilyName);
    }

    private static FontFamily create(string name)
    {
        try
        {
            return new FontFamily(name);
        }
        catch
        {
            return new FontFamily(SystemFontName);
        }
    }
}
