using System.Globalization;
using System.Windows.Data;

namespace TimeLab.App;

/// <summary>
/// 将 TimeSpan 转换为人类可读的时长字符串（如 "25 分钟"、"1h 25m"）
/// </summary>
public class DurationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not TimeSpan duration)
            return "";

        if (duration.TotalSeconds < 60)
            return $"{duration.Seconds} 秒";

        if (duration.TotalHours < 1)
        {
            var m = (int)duration.TotalMinutes;
            var s = duration.Seconds;
            return s > 0 ? $"{m} 分 {s} 秒" : $"{m} 分钟";
        }

        return $"{(int)duration.TotalHours}h {duration.Minutes}m";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
