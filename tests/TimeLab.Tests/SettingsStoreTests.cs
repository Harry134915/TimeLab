using TimeLab.App;

namespace TimeLab.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _settingsDir;

    public SettingsStoreTests()
    {
        _settingsDir = Path.Combine(Path.GetTempPath(), "TimeLab.Tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public void Load_ReturnsDefaultSettingsWhenFileDoesNotExist()
    {
        var store = new SettingsStore(_settingsDir);

        var settings = store.Load();

        Assert.False(settings.IsDarkMode);
    }

    [Fact]
    public void Save_WritesSettingsThatCanBeLoaded()
    {
        var store = new SettingsStore(_settingsDir);

        store.Save(new AppSettings { IsDarkMode = true });
        var settings = new SettingsStore(_settingsDir).Load();

        Assert.True(settings.IsDarkMode);
    }

    [Fact]
    public async Task Load_ReturnsDefaultSettingsWhenJsonIsCorrupted()
    {
        Directory.CreateDirectory(_settingsDir);
        await File.WriteAllTextAsync(Path.Combine(_settingsDir, "settings.json"), "{ invalid json");
        var store = new SettingsStore(_settingsDir);

        var settings = store.Load();

        Assert.False(settings.IsDarkMode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_settingsDir))
            Directory.Delete(_settingsDir, recursive: true);
    }
}
