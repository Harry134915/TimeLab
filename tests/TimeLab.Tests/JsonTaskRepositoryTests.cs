using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TimeLab.Infrastructure;

namespace TimeLab.Tests;

public class JsonTaskRepositoryTests : IDisposable
{
    private readonly string _dataDir;

    public JsonTaskRepositoryTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "TimeLab.Tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEmptyListWhenFileDoesNotExist()
    {
        var repository = new JsonTaskRepository(_dataDir);

        var items = await repository.GetAllAsync();

        Assert.Empty(items);
    }

    [Fact]
    public async Task AddAsync_SavesTaskThatCanBeReadBack()
    {
        var repository = new JsonTaskRepository(_dataDir);
        var item = await new TimeLab.Application.TaskService(repository)
            .CreateAsync("Persist me", plannedSeconds: 300);

        var reloadedRepository = new JsonTaskRepository(_dataDir);
        var items = await reloadedRepository.GetAllAsync();

        var saved = Assert.Single(items);
        Assert.Equal(item.Id, saved.Id);
        Assert.Equal("Persist me", saved.Title);
        Assert.Equal(300, saved.PlannedSeconds);
    }

    [Fact]
    public async Task AddAsync_FromDifferentRepositoryInstances_PreservesEveryTask()
    {
        const int taskCount = 24;
        var repositories = Enumerable.Range(0, taskCount)
            .Select(_ => new JsonTaskRepository(_dataDir))
            .ToArray();
        var tasks = Enumerable.Range(0, taskCount)
            .Select(index => new TimeLab.Core.TaskItem
            {
                Id = Guid.NewGuid(),
                Title = $"Concurrent task {index}",
                CreatedAt = DateTime.UtcNow,
                PlannedSeconds = 300
            })
            .ToArray();

        await Task.WhenAll(tasks.Select((item, index) => repositories[index].AddAsync(item)));

        var saved = await new JsonTaskRepository(_dataDir).GetAllAsync();
        Assert.Equal(taskCount, saved.Count);
        Assert.Equal(
            tasks.Select(item => item.Id).OrderBy(id => id),
            saved.Select(item => item.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task GetAllAsync_BacksUpCorruptedJsonAndReturnsEmptyList()
    {
        Directory.CreateDirectory(_dataDir);
        var filePath = Path.Combine(_dataDir, "tasks.json");
        await File.WriteAllTextAsync(filePath, "{ invalid json");

        var repository = new JsonTaskRepository(_dataDir);
        var items = await repository.GetAllAsync();

        Assert.Empty(items);
        Assert.False(File.Exists(filePath));
        Assert.True(File.Exists(filePath + ".bak"));
    }

    [Fact]
    public async Task GetAllAsync_RecoversNewerCompletedTemporaryFile()
    {
        Directory.CreateDirectory(_dataDir);
        var filePath = Path.Combine(_dataDir, "tasks.json");
        var oldItem = CreateTask("Old task");
        var recoveredItem = CreateTask("Recovered task");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(new[] { oldItem }));
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddMinutes(-1));
        await File.WriteAllTextAsync(
            filePath + ".tmp",
            JsonSerializer.Serialize(new[] { oldItem, recoveredItem }));

        var items = await new JsonTaskRepository(_dataDir).GetAllAsync();

        Assert.Equal(2, items.Count);
        Assert.Contains(items, item => item.Id == recoveredItem.Id);
        Assert.False(File.Exists(filePath + ".tmp"));
        Assert.True(File.Exists(filePath + ".bak"));
    }

    [Fact]
    public async Task GetAllAsync_InvalidTemporaryFile_PreservesValidMainFile()
    {
        Directory.CreateDirectory(_dataDir);
        var filePath = Path.Combine(_dataDir, "tasks.json");
        var item = CreateTask("Valid task");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(new[] { item }));
        await File.WriteAllTextAsync(filePath + ".tmp", "{ incomplete");

        var items = await new JsonTaskRepository(_dataDir).GetAllAsync();

        Assert.Equal(item.Id, Assert.Single(items).Id);
        Assert.True(File.Exists(filePath));
        Assert.True(File.Exists(filePath + ".tmp.bak"));
    }

    [Fact]
    public async Task AddAsync_WaitsForSeparateProcessFileLock()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var process = StartFileLockProcess(holdMilliseconds: 1200);
        try
        {
            var ready = await process.StandardOutput.ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("READY", ready);

            var stopwatch = Stopwatch.StartNew();
            await new JsonTaskRepository(_dataDir).AddAsync(CreateTask("Cross-process task"));
            stopwatch.Stop();

            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(700));
            var items = await new JsonTaskRepository(_dataDir).GetAllAsync();
            Assert.Single(items);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.Dispose();
        }
    }

    [Fact]
    public async Task AddAsync_AfterLockOwnerIsTerminated_RecoversAbandonedMutex()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var process = StartFileLockProcess(holdMilliseconds: 30_000);
        try
        {
            var ready = await process.StandardOutput.ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("READY", ready);

            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));

            await new JsonTaskRepository(_dataDir)
                .AddAsync(CreateTask("Recovered after abandoned lock"))
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Single(await new JsonTaskRepository(_dataDir).GetAllAsync());
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.Dispose();
        }
    }

    private Process StartFileLockProcess(int holdMilliseconds)
    {
        var filePath = Path.GetFullPath(Path.Combine(_dataDir, "tasks.json"))
            .ToUpperInvariant();
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(filePath)));
        var mutexName = $@"Local\TimeLab.JsonFile.{hash}";
        var script = $"""
            $mutex = [Threading.Mutex]::new($false, '{mutexName}')
            $null = $mutex.WaitOne()
            [Console]::Out.WriteLine('READY')
            [Console]::Out.Flush()
            Start-Sleep -Milliseconds {holdMilliseconds}
            $mutex.ReleaseMutex()
            $mutex.Dispose()
            """;
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(encodedScript);
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动跨进程仓储测试。");
    }

    private static TimeLab.Core.TaskItem CreateTask(string title) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        CreatedAt = DateTime.UtcNow,
        PlannedSeconds = 300
    };

    public void Dispose()
    {
        if (Directory.Exists(_dataDir))
            Directory.Delete(_dataDir, recursive: true);
    }
}
