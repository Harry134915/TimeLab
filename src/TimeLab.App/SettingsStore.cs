using System.IO;
using System.Text.Json;

namespace TimeLab.App;

/// <summary>
/// 应用级设置，目前只保存深色模式偏好。
/// </summary>
public sealed class AppSettings
{
    /// <summary>是否启用深色模式。</summary>
    public bool IsDarkMode { get; set; }
}

/// <summary>
/// 负责读取和保存本地 settings.json，读写失败时使用默认设置。
/// </summary>
public sealed class SettingsStore
{
    private readonly string _settingsDir;
    private readonly string _settingsPath;

    /// <summary>
    /// 创建设置存储。未指定目录时使用用户本地 AppData/TimeLab 目录。
    /// </summary>
    public SettingsStore(string? settingsDir = null)
    {
        _settingsDir = settingsDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TimeLab");

        _settingsPath = Path.Combine(_settingsDir, "settings.json");
    }

    /// <summary>
    /// 读取设置；文件不存在、内容损坏或读取失败时返回默认设置。
    /// </summary>
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

    /// <summary>
    /// 保存设置；保存失败时静默忽略，避免影响主窗口交互。
    /// </summary>
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
