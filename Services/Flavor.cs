namespace OsuRoam.Services;

/// <summary>
/// 构建风味。同一套源码出两个 exe：
///
/// - 中文版（默认）：界面跟随系统语言，谱面走国内 sayobot 镜像；
/// - 英文版（<c>-p:RoamFlavor=en</c>）：界面默认英文，谱面走国际源。
///
/// 分开打包的理由是下载源：两个镜像对彼此的受众都不合适，与其在界面上让用户
/// 自己挑，不如在构建时定好默认值。除此之外两份代码完全一致。
/// </summary>
public static class Flavor
{
#if EN_BUILD
    public const bool IsEnglish = true;
#else
    public const bool IsEnglish = false;
#endif

    /// <summary>界面语言的出厂默认值；用户在设置里改过之后以配置为准。</summary>
    public const string DefaultLanguage = IsEnglish ? Loc.EnglishTag : Loc.AutoTag;

    /// <summary>主下载源。国际源（catboy）会挑 User-Agent，下载器里已经带上了。</summary>
    public const string DefaultDownloadTemplate = IsEnglish
        ? "https://catboy.best/d/{setId}"
        : "https://dl.sayobot.cn/beatmaps/download/full/{setId}";

    /// <summary>备用源，一行一个；第一个失败就换下一个。</summary>
    public const string DefaultDownloadMirrors = IsEnglish
        ? "https://catboy.best/d/{setId}\nhttps://beatconnect.io/b/{setId}/"
        : "https://dl.sayobot.cn/beatmaps/download/full/{setId}\nhttps://txy1.sayobot.cn/beatmaps/download/full/{setId}";

    /// <summary>给日志和发布说明用的一行标识。</summary>
    public static string Describe() => IsEnglish ? "en" : "zh";
}
