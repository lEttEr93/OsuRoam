using System.Windows;
using System.Windows.Media;

namespace OsuRoam.Services;

/// <summary>
/// 界面字体。
///
/// Torus 是 osu!(lazer) 的默认界面字体（<c>OsuFont.Default</c> → <c>Typeface.Torus</c>），
/// 文件在 <c>Fonts\</c> 里，以 WPF <c>Resource</c> 编进程序集。
/// 它只有 363 个字形：拉丁字母、数字、常用标点，**没有汉字**。
/// 中文的显示路径是 WPF 的逐字符回退 —— 和 osu!lazer 用 Noto 补 CJK 是一个思路，
/// 只是回退目标是系统里的 <see cref="SystemFontName"/>。
/// </summary>
public static class Typography
{
    /// <summary>程序集内 Torus 的 pack URI。片段 <c>#Torus</c> 必须和字体家族名一致。</summary>
    public const string OsuFontUri = "pack://application:,,,/Fonts/#Torus";

    /// <summary>Torus 没有汉字字形时落到的系统字体。</summary>
    public const string SystemFontName = "Microsoft YaHei UI";

    /// <summary><c>App.xaml</c> 里的字体资源键，所有控件样式用 DynamicResource 引用它。</summary>
    public const string ResourceKey = "UiFont";

    /// <summary>用来判断「Torus 真的加载到了」的探针字符。</summary>
    private const char LatinProbe = 'o';

    private static readonly Lazy<bool> osuAvailable = new(probeOsuFont, isThreadSafe: true);
    private static FontFamily? osuFamily;
    private static FontFamily? systemFamily;
    private static bool usingOsuFont = true;

    /// <summary>内置 Torus 是否可用（文件缺失或家族名对不上时为 false）。</summary>
    public static bool OsuAvailable => osuAvailable.Value;

    /// <summary>当前是否在用 Torus。</summary>
    public static bool UsingOsuFont => usingOsuFont && OsuAvailable;

    /// <summary>内置 Torus 家族。</summary>
    public static FontFamily Osu => osuFamily ??= loadEmbedded();

    /// <summary>系统回退字体。</summary>
    public static FontFamily SystemFont => systemFamily ??= create(SystemFontName);

    /// <summary>当前生效的字体家族。</summary>
    public static FontFamily Current => UsingOsuFont ? Osu : SystemFont;

    /// <summary>
    /// 切换字体并立刻应用到已经建好的界面（<see cref="ResourceKey"/> 是 DynamicResource，
    /// 换资源会直接刷新所有引用它的控件）。
    /// </summary>
    public static void Apply(bool preferOsuFont)
    {
        usingOsuFont = preferOsuFont && OsuAvailable;
        if (Application.Current is { } application)
            application.Resources[ResourceKey] = Current;
    }

    /// <summary>给日志用的一行说明。</summary>
    public static string Describe()
    {
        if (!OsuAvailable)
            return $"字体：Torus 不可用（{OsuFontUri} 没加载出来），回退 {SystemFontName}";

        string detail = glyphDescription(Osu);
        return usingOsuFont
            ? $"字体：内置 Torus{detail}；汉字回退 {SystemFontName}"
            : $"字体：{SystemFontName}（设置里关掉了 osu! 字体）";
    }

    /// <summary>启动时自检：Torus 没加载出来就自动退回系统字体，至少文字可读。</summary>
    public static void EnsureUsable()
    {
        if (usingOsuFont && !OsuAvailable)
        {
            AppLog.Info("内置 Torus 没有加载出来，本次改用系统字体");
            usingOsuFont = false;
            if (Application.Current is { } application)
                application.Resources[ResourceKey] = SystemFont;
        }
    }

    private static bool probeOsuFont()
    {
        try
        {
            if (!new Typeface(Osu, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal)
                    .TryGetGlyphTypeface(out GlyphTypeface glyphTypeface))
            {
                AppLog.Info($"字体：{OsuFontUri} 没有解析出字形面");
                return false;
            }

            bool ok = glyphTypeface.CharacterToGlyphMap.ContainsKey(LatinProbe);
            AppLog.Info(ok
                ? $"字体：Torus 已加载（{glyphTypeface.GlyphCount} 字形，家族名 {string.Join('/', glyphTypeface.FamilyNames.Values)}）"
                : "字体：Torus 缺少探针字符，判定加载失败");
            return ok;
        }
        catch (Exception exception)
        {
            AppLog.Info($"字体：Torus 加载异常 {exception.GetType().Name}: {exception.Message}");
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

    /// <summary>程序集内字体所在目录（pack URI）。</summary>
    public const string EmbeddedFontsUri = "pack://application:,,,/Fonts/";

    /// <summary>字体家族名，必须和 otf 里 name 表的 family 一致。</summary>
    public const string OsuFamilyName = "Torus";

    /// <summary>
    /// 从程序集里取字体家族。
    ///
    /// 注意：**不能**直接 <c>new FontFamily("pack://application:,,,/Fonts/#Torus")</c>。
    /// 那样建出来的家族 BaseUri 是空的，<see cref="Typeface.TryGetGlyphTypeface"/> 一律返回 false
    /// （实测 pack URI、<c>/Fonts/#Torus</c>、<c>./Fonts/#Torus</c>、文件 URI 等 9 种写法全失败，
    /// 但同一个 <c>GetFontFamilies</c> 却能正常列出 <c>./#Torus</c>）。
    /// 所以走 <see cref="Fonts.GetFontFamilies(Uri)"/>，它返回的是带 BaseUri 的家族。
    /// </summary>
    private static FontFamily loadEmbedded()
    {
        try
        {
            ICollection<FontFamily> families = Fonts.GetFontFamilies(new Uri(EmbeddedFontsUri));
            FontFamily? torus = families.FirstOrDefault(family =>
                family.FamilyNames.Values.Any(name => name.Equals(OsuFamilyName, StringComparison.OrdinalIgnoreCase)));
            if (torus is not null)
                return torus;

            AppLog.Info($"字体：{EmbeddedFontsUri} 里没有 {OsuFamilyName}");
        }
        catch (Exception exception)
        {
            AppLog.Info($"字体：读取 {EmbeddedFontsUri} 失败 {exception.GetType().Name}: {exception.Message}");
        }

        return new FontFamily(OsuFamilyName);
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
