using System.Collections.ObjectModel;
using System.Globalization;
using TimeLab.App;
using TimeLab.Core;

namespace TimeLab.Tests;

public class TaskIdToTitleConverterTests
{
    [Fact]
    public void Convert_MissingTask_UsesReadableDeletedTaskLabel()
    {
        var converter = new TaskIdToTitleConverter();

        var result = converter.Convert(
            [Guid.NewGuid(), new ObservableCollection<TaskItem>()],
            typeof(string),
            parameter: null!,
            CultureInfo.InvariantCulture);

        Assert.Equal("已删除任务", result);
    }
}
