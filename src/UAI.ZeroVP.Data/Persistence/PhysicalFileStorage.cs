using System.IO;
using System.Text;

namespace UAI.ZeroVP.Data.Persistence;

/// <summary>
/// Default implementation of <see cref="IFileStorage"/> performing physical disk I/O.
/// </summary>
public sealed class PhysicalFileStorage : IFileStorage
{
    private const int MoveAttempts = 5;

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
        File.ReadAllTextAsync(path, cancellationToken);

    /// <summary>
    /// Atomically replaces <paramref name="path"/> with <paramref name="content"/> (UTF-8 without BOM).
    /// <para>
    /// The content is written to a temporary file in the same directory, flushed to disk and then
    /// moved over the target with <see cref="File.Move(string, string, bool)"/>. Readers and a crash
    /// at any moment therefore see either the complete old file or the complete new one, never a
    /// partially written file. Concurrent writers each use their own temporary file, so the last
    /// move wins with one complete version. A failed or cancelled write leaves the target untouched
    /// and deletes its temporary file.
    /// </para>
    /// </summary>
    public async Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        // Same directory as the target: a move within one file system is atomic.
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, FileOptions.Asynchronous))
            {
                await using (var writer = new StreamWriter(stream, Utf8WithoutBom))
                {
                    await writer.WriteAsync(content.AsMemory(), cancellationToken);
                    await writer.FlushAsync(cancellationToken);

                    // Without this a power loss right after the move could leave an empty file.
                    stream.Flush(flushToDisk: true);
                }
            }

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(temporaryPath, fullPath, overwrite: true);
                    break;
                }
                catch (Exception exception) when (attempt < MoveAttempts && (exception is IOException or UnauthorizedAccessException))
                {
                    // An antivirus or indexer can hold the target for a moment (mainly on Windows).
                    await Task.Delay(20 * attempt, cancellationToken);
                }
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // Best effort: a leftover temporary file never replaces the real data.
                }
            }
        }
    }

    /// <inheritdoc />
    public void Copy(string sourcePath, string destinationPath) =>
        File.Copy(sourcePath, destinationPath, overwrite: false);
}
