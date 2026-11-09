using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OsuRoam.Controls;

/// <summary>
/// 滚动条的「可滚高度」（Maximum）→ 可见性。不足 1 像素就当作不可滚。
///
/// 为什么需要它：内容的滚动条是常驻的（这样三个标签页切换时宽度不会抖 9px），
/// 但内容正好放得下时 WPF 会把滑块撑满整条轨道，看起来像一根竖线。
/// 这里按可滚高度把它藏掉，滚动条本体仍然占着位置。
/// </summary>
public sealed class ScrollableToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double maximum && maximum >= 1 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
