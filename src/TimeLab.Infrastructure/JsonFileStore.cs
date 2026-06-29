using System.Text.Json;

namespace TimeLab.Infrastructure;

internal sealed class JsonFileStore<T>
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    private readonly string _dataDir;
    private readonly string _filePath;

    public JsonFileStore(string fileName)
    {
        _dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TimeLab");

        _filePath = Path.Combine(_dataDir, fileName);
    }

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

    private void BackupCorruptedFile()
    {
        var backupPath = _filePath + ".bak";
        if (File.Exists(backupPath))
            File.Delete(backupPath);

        File.Move(_filePath, backupPath);
    }
}
