using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Data;
using TimeLab.Core;

namespace TimeLab.App;

/// <summary>
/// 多值转换器，根据 TaskId 和 Tasks 集合查找并返回任务标题
/// 用于 Session Log 中显示关联任务名称
/// </summary>
public class TaskIdToTitleConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return "";

        if (values[0] is not Guid taskId)
            return "";

        if (values[1] is not ObservableCollection<TaskItem> tasks)
            return "已删除任务";

        return tasks.FirstOrDefault(t => t.Id == taskId)?.Title ?? "已删除任务";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
