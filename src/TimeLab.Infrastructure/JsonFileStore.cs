using System.Collections.Concurrent;
using System.Text.Json;

namespace TimeLab.Infrastructure;

/// <summary>
/// 面向简单列表数据的 JSON 文件存储工具，供具体仓储复用。
/// </summary>
internal sealed class JsonFileStore<T>
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    private readonly string _dataDir;
    private readonly string _filePath;
    private readonly SemaphoreSlim _fileGate;

    /// <summary>
    /// 创建文件存储。未指定目录时使用用户本地 AppData/TimeLab 目录。
    /// </summary>
    public JsonFileStore(string fileName, string? dataDir = null)
    {
        var resolvedDataDir = dataDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TimeLab");

        _dataDir = Path.GetFullPath(resolvedDataDir);
        _filePath = Path.Combine(_dataDir, fileName);
        _fileGate = JsonFileGateRegistry.Get(_filePath);
    }

    public async Task<TResult> ExecuteExclusiveAsync<TResult>(Func<Task<TResult>> operation)
    {
        await _fileGate.WaitAsync();
        try
        {
            return await operation();
        }
        finally
        {
            _fileGate.Release();
        }
    }

    public async Task ExecuteExclusiveAsync(Func<Task> operation)
    {
        await _fileGate.WaitAsync();
        try
        {
            await operation();
        }
        finally
        {
            _fileGate.Release();
        }
    }

    /// <summary>
    /// 加载列表数据；文件不存在或 JSON 损坏时返回空列表。
    /// </summary>
    public async Task<List<T>> LoadAsync()
    {
        EnsureDataDirectory();

        if (!File.Exists(_filePath))
            return [];

        try
        {
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<List<T>>(stream) ?? [];
        }
        catch (JsonException)
        {
            BackupCorruptedFile();
            return [];
        }
    }

    /// <summary>
    /// 使用临时文件写入列表数据，降低写入中断导致文件损坏的风险。
    /// </summary>
    public async Task SaveAsync(List<T> items)
    {
        EnsureDataDirectory();

        var tempPath = _filePath + ".tmp";
        var backupPath = _filePath + ".bak";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, items, Options);
        }

        if (File.Exists(_filePath))
            File.Replace(tempPath, _filePath, backupPath, ignoreMetadataErrors: true);
        else
            File.Move(tempPath, _filePath);
    }

    private void EnsureDataDirectory()
    {
        if (!Directory.Exists(_dataDir))
            Directory.CreateDirectory(_dataDir);
    }

    /// <summary>
    /// 将损坏的 JSON 文件移动为 .bak，保留现场便于后续排查。
    /// </summary>
    private void BackupCorruptedFile()
    {
        var backupPath = _filePath + ".bak";
        if (File.Exists(backupPath))
            File.Delete(backupPath);

        File.Move(_filePath, backupPath);
    }
}

internal static class JsonFileGateRegistry
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public static SemaphoreSlim Get(string filePath)
    {
        return Gates.GetOrAdd(filePath, static _ => new SemaphoreSlim(1, 1));
    }
}
