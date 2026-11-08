using System.IO;
using Vortice.Direct2D1;
using Vortice.DXGI;
using OsuRoam.Services;

namespace OsuRoam.Controls;

/// <summary>
/// 预览用的贴图集。**所有视觉元素都是贴图，不再画矩形** —— 以后要换皮肤，
/// 只要把同名的 PNG 放进皮肤目录即可（解析顺序见 <see cref="Resolve"/>）。
///
/// 位图跟着 D3D 设备走（<see cref="Load"/> 在渲染线程里、设备建好之后调用一次），
/// 画笔跟着渲染目标走（每个交换链缓冲区各一套，见 FrameResources）。
/// </summary>
internal sealed class PreviewSkin : IDisposable
{
    private readonly Dictionary<string, ID2D1Bitmap> bitmaps = [];

    /// <summary>这次用的是哪套贴图（写进日志，方便确认）。</summary>
    public string Source { get; private set; } = "内置";

    /// <summary>单点音符。</summary>
    public ID2D1Bitmap? Note => get("note");

    /// <summary>长条头。</summary>
    public ID2D1Bitmap? HoldHead => get("hold-head");

    /// <summary>长条尾。</summary>
    public ID2D1Bitmap? HoldTail => get("hold-tail");

    /// <summary>长条身（横向 4px：左右描边 + 中间可拉伸）。</summary>
    public ID2D1Bitmap? HoldBody => get("hold-body");

    /// <summary>列背景（纵向拉伸）。</summary>
    public ID2D1Bitmap? Key => get("key");

    /// <summary>判定线（横向平铺）。</summary>
    public ID2D1Bitmap? Judge => get("judge");

    private ID2D1Bitmap? get(string name) => bitmaps.TryGetValue(name, out ID2D1Bitmap? bitmap) ? bitmap : null;

    /// <summary>
    /// 解析贴图来源：优先用户指定的目录，其次 osu!stable 的明文皮肤目录，
    /// 最后回落到随程序的分发目录 <c>Assets\Preview</c>。
    /// （lazer 的皮肤在 realm 里，独立进程读不了，见 docs/谱面预览-皮肤调研.md）
    /// </summary>
    public static string ResolveDirectory(string? userDirectory)
    {
        if (!string.IsNullOrWhiteSpace(userDirectory) && Directory.Exists(userDirectory))
            return userDirectory;

        string stableRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu!", "Skins");
        if (Directory.Exists(stableRoot))
        {
            string? newest = Directory.EnumerateDirectories(stableRoot)
                .Where(dir => Directory.EnumerateFiles(dir, "*.png").Any())
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (newest is not null)
                return newest;
        }

        return Path.Combine(AppContext.BaseDirectory, "Assets", "Preview");
    }

    /// <summary>在渲染线程里、D3D 设备建好之后调用。</summary>
    public void Load(ID2D1RenderTarget target, string directory)
    {
        Source = directory;
        foreach (string name in new[] { "note", "hold-head", "hold-tail", "hold-body", "key", "judge" })
        {
            string path = Path.Combine(directory, name + ".png");
            if (!File.Exists(path))
                continue;
            try
            {
                ID2D1Bitmap bitmap = loadBitmap(target, path);
                bitmaps[name] = bitmap;
            }
            catch (Exception exception)
            {
                AppLog.Error($"预览贴图加载失败：{path}", exception);
            }
        }

        AppLog.Info($"预览贴图：{Source}（载入 {bitmaps.Count} 张）");
    }

    private static ID2D1Bitmap loadBitmap(ID2D1RenderTarget target, string path)
    {
        // 用 WPF 解码（PNG 交给 WIC），再按 BGRA 预乘把像素塞进 D2D 位图。
        using FileStream stream = File.OpenRead(path);
        System.Windows.Media.Imaging.BitmapDecoder decoder =
            System.Windows.Media.Imaging.BitmapDecoder.Create(stream,
                System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        System.Windows.Media.Imaging.BitmapSource frame = decoder.Frames[0];
        System.Windows.Media.Imaging.FormatConvertedBitmap converted = new();
        converted.BeginInit();
        converted.Source = frame;
        converted.DestinationFormat = System.Windows.Media.PixelFormats.Pbgra32;
        converted.EndInit();
        converted.Freeze();

        int width = converted.PixelWidth;
        int height = converted.PixelHeight;
        byte[] pixels = new byte[width * height * 4];
        converted.CopyPixels(pixels, width * 4, 0);

        BitmapProperties properties = new(
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            96, 96);
        unsafe
        {
            fixed (byte* pointer = pixels)
            {
                return target.CreateBitmap(new Vortice.Mathematics.SizeI(width, height),
                    (IntPtr)pointer, (uint)(width * 4), properties);
            }
        }
    }

    public void Dispose()
    {
        foreach (ID2D1Bitmap bitmap in bitmaps.Values)
            bitmap.Dispose();
        bitmaps.Clear();
    }
}
