using System.IO;
using System.Text.Json;

namespace TimeLab.App;

public sealed class AppSettings
{
    public bool IsDarkMode { get; set; }
}

public sealed class SettingsStore
{
    private readonly string _settingsDir;
    private readonly string _settingsPath;

    public SettingsStore(string? settingsDir = null)
    {
        _settingsDir = settingsDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TimeLab");

        _settingsPath = Path.Combine(_settingsDir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings();

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            if (!Directory.Exists(_settingsDir))
                Directory.CreateDirectory(_settingsDir);

            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings));
        }
        catch
        {
            // 保存失败不应影响主窗口交互。
        }
    }
}
