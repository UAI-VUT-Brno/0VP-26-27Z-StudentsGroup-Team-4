namespace UAI.ZeroVP.Data.Persistence;

/// <summary>
/// Abstraction for file system operations to decouple data persistence from the physical disk,
/// enabling deterministic unit testing of file I/O edge cases.
/// </summary>
public interface IFileStorage
{
    /// <summary>Determines whether the specified file exists.</summary>
    bool FileExists(string path);

    /// <summary>Asynchronously reads the entire file content into a string.</summary>
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Asynchronously writes the specified string content to a file, creating target directories if needed.</summary>
    Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default);
}