using UAI.ZeroVP.Data.Persistence;

namespace UAI.ZeroVP.Tests;

/// <summary>
/// 0VP-T4-F12: <see cref="PhysicalFileStorage.WriteAllTextAsync"/> must replace the target file
/// atomically, so an interrupted or concurrent save never leaves a partially written file.
/// </summary>
public sealed class PhysicalFileStorageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "zerovp-filestorage-" + Guid.NewGuid().ToString("N"));
    private readonly PhysicalFileStorage _storage = new();

    public PhysicalFileStorageTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string TodosPath => Path.Combine(_directory, "todos.json");

    private string[] OtherFiles() =>
        Directory.GetFiles(_directory).Where(file => file != TodosPath).Select(Path.GetFileName).ToArray()!;

    [Fact]
    public async Task WriteAllTextAsync_ReplacesTheFile_AndLeavesNoTemporaryFile()
    {
        await File.WriteAllTextAsync(TodosPath, "[\"a much longer original content that must disappear completely\"]");

        await _storage.WriteAllTextAsync(TodosPath, "[]");

        Assert.Equal("[]", await File.ReadAllTextAsync(TodosPath));
        Assert.Empty(OtherFiles());
    }

    [Fact]
    public async Task WriteAllTextAsync_CreatesMissingDirectory()
    {
        var path = Path.Combine(_directory, "nested", "data", "todos.json");

        await _storage.WriteAllTextAsync(path, "[1]");

        Assert.Equal("[1]", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task WriteAllTextAsync_WhenCancelled_KeepsTheOriginalAndLeavesNoTemporaryFile()
    {
        const string original = "[\"original\"]";
        await File.WriteAllTextAsync(TodosPath, original);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _storage.WriteAllTextAsync(TodosPath, new string('x', 1_000_000), cancelled.Token));

        Assert.Equal(original, await File.ReadAllTextAsync(TodosPath));
        Assert.Empty(OtherFiles());
    }

    [Fact]
    public async Task ConcurrentWrites_AlwaysLeaveOneCompleteVersion()
    {
        // Different lengths: a non-atomic in-place write would leave a mix of two versions
        // or the tail of a longer one behind a shorter one.
        var versions = Enumerable.Range(1, 40)
            .Select(i => "[" + string.Join(',', Enumerable.Repeat($"\"v{i}\"", i * 500)) + "]")
            .ToList();

        await Task.WhenAll(versions.Select(content => Task.Run(() => _storage.WriteAllTextAsync(TodosPath, content))));

        Assert.Contains(await File.ReadAllTextAsync(TodosPath), versions);
        Assert.Empty(OtherFiles());
    }
}
