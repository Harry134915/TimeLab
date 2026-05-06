using System.Globalization;
using System.Windows.Data;

namespace TimeLab.App;

/// <summary>
/// 将总秒数转换为 "分:秒" 显示格式
/// </summary>
public class SecondsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int totalSeconds || totalSeconds <= 0)
            return "";

        var m = totalSeconds / 60;
        var s = totalSeconds % 60;
        return $"{m}:{s:D2}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
