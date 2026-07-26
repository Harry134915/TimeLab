using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
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
    private readonly JsonFileGate _fileGate;

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
        => await _fileGate.ExecuteAsync(operation);

    public async Task ExecuteExclusiveAsync(Func<Task> operation)
        => await _fileGate.ExecuteAsync(operation);

    /// <summary>
    /// 加载列表数据；文件不存在或 JSON 损坏时返回空列表。
    /// </summary>
    public async Task<List<T>> LoadAsync()
    {
        EnsureDataDirectory();

        var recovery = await TryRecoverTemporaryFileAsync();
        if (recovery.Recovered)
            return recovery.Items;

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
        var committed = false;
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, items, Options);
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_filePath))
                File.Replace(tempPath, _filePath, backupPath, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, _filePath);

            committed = true;
        }
        finally
        {
            if (!committed && File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                    // 保留原始写入异常；残留临时文件会在下次加载时验证并恢复或隔离。
                }
                catch (UnauthorizedAccessException)
                {
                    // 清理失败不应覆盖更有诊断价值的原始保存异常。
                }
            }
        }
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

    /// <summary>
    /// 进程在临时文件完成写入后、替换主文件前退出时，优先恢复较新的有效临时文件。
    /// </summary>
    private async Task<(bool Recovered, List<T> Items)> TryRecoverTemporaryFileAsync()
    {
        var tempPath = _filePath + ".tmp";
        if (!File.Exists(tempPath))
            return (false, []);

        List<T> items;
        try
        {
            await using var stream = File.OpenRead(tempPath);
            items = await JsonSerializer.DeserializeAsync<List<T>>(stream) ?? [];
        }
        catch (JsonException)
        {
            BackupCorruptedTemporaryFile(tempPath);
            return (false, []);
        }

        if (File.Exists(_filePath)
            && File.GetLastWriteTimeUtc(tempPath) <= File.GetLastWriteTimeUtc(_filePath))
        {
            File.Delete(tempPath);
            return (false, []);
        }

        if (File.Exists(_filePath))
        {
            File.Replace(
                tempPath,
                _filePath,
                _filePath + ".bak",
                ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, _filePath);
        }

        return (true, items);
    }

    private static void BackupCorruptedTemporaryFile(string tempPath)
    {
        var backupPath = tempPath + ".bak";
        if (File.Exists(backupPath))
            File.Delete(backupPath);
        File.Move(tempPath, backupPath);
    }
}

internal static class JsonFileGateRegistry
{
    private static readonly ConcurrentDictionary<string, JsonFileGate> Gates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public static JsonFileGate Get(string filePath)
    {
        return Gates.GetOrAdd(filePath, static path => new JsonFileGate(path));
    }
}

/// <summary>
/// 使用进程内信号量和 Windows 命名互斥体保护同一 JSON 文件。
/// </summary>
internal sealed class JsonFileGate
{
    private static readonly TimeSpan CrossProcessTimeout = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _inProcessGate = new(1, 1);
    private readonly Mutex? _crossProcessMutex;

    internal JsonFileGate(string filePath)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var normalizedPath = Path.GetFullPath(filePath).ToUpperInvariant();
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)));
        _crossProcessMutex = new Mutex(
            initiallyOwned: false,
            name: $@"Local\TimeLab.JsonFile.{hash}");
    }

    internal async Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> operation)
    {
        await _inProcessGate.WaitAsync();
        try
        {
            if (_crossProcessMutex is null)
                return await operation();

            return await Task.Run(() => ExecuteWithMutex(operation));
        }
        finally
        {
            _inProcessGate.Release();
        }
    }

    internal async Task ExecuteAsync(Func<Task> operation)
    {
        await ExecuteAsync(async () =>
        {
            await operation();
            return true;
        });
    }

    private TResult ExecuteWithMutex<TResult>(Func<Task<TResult>> operation)
    {
        var acquired = false;
        try
        {
            try
            {
                acquired = _crossProcessMutex!.WaitOne(CrossProcessTimeout);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
                throw new TimeoutException("等待 JSON 文件的跨进程写入锁超时。");

            return operation().ConfigureAwait(false).GetAwaiter().GetResult();
        }
        finally
        {
            if (acquired)
                _crossProcessMutex!.ReleaseMutex();
        }
    }
}
