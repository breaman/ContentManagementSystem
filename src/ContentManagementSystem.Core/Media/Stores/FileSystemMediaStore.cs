using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Media.Stores;

/// <summary>
/// The development store: media bytes on the local disk, under a root outside <c>wwwroot</c>
/// (task P5-03, spec section 13.2).
/// </summary>
/// <remarks>
/// Three properties make it safe to point at a directory on a real machine:
/// <list type="bullet">
/// <item>
/// <description>
/// <strong>The root may not be inside <c>wwwroot</c>.</strong> Checked in the constructor, so a
/// deployment that gets this wrong fails at startup rather than quietly serving uploaded files as
/// static content on its own origin (spec section 20.7).
/// </description>
/// </item>
/// <item>
/// <description>
/// <strong>Every key is validated and every resolved path is re-checked against the root.</strong>
/// The validation should make the second check unreachable; it is there because "should" is not a
/// property a path traversal respects, and the cost of a full-path comparison per operation is
/// nothing next to what it prevents.
/// </description>
/// </item>
/// <item>
/// <description>
/// <strong>Writes are atomic.</strong> Content lands in a temporary file and is moved into place, so
/// an interrupted upload cannot leave a half-written file sitting under a key whose hash promises a
/// whole one — which, being content-addressed, would then be trusted forever.
/// </description>
/// </item>
/// </list>
/// </remarks>
public sealed class FileSystemMediaStore : IMediaStore
{
    private const string TempDirectoryName = ".tmp";

    private readonly string _root;
    private readonly ILogger<FileSystemMediaStore> _logger;

    /// <summary>
    /// Creates the store and ensures its root exists.
    /// </summary>
    /// <param name="rootPath">Absolute path of the storage root.</param>
    /// <param name="logger">Logger.</param>
    /// <exception cref="ArgumentException">The root is inside a <c>wwwroot</c> directory.</exception>
    public FileSystemMediaStore(string rootPath, ILogger<FileSystemMediaStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(logger);

        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        _logger = logger;

        // Segment-wise rather than a substring search: a root called "mywwwrootbackup" is fine and
        // a root of "…/wwwroot/media" is not, and only one of those is what a substring test says.
        if (_root.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals(MediaStorageOptions.ForbiddenRootSegment, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                $"The media root must be outside '{MediaStorageOptions.ForbiddenRootSegment}'; " +
                "files served as static content bypass content-type pinning and authorization.",
                nameof(rootPath));
        }

        Directory.CreateDirectory(_root);
    }

    /// <summary>The absolute root the store reads and writes under.</summary>
    public string RootPath => _root;

    /// <inheritdoc />
    public async Task<MediaStoreResult> PutAsync(
        string key,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var path = ResolvePath(key);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var tempDirectory = Path.Combine(_root, TempDirectoryName);

        Directory.CreateDirectory(tempDirectory);

        var tempPath = Path.Combine(tempDirectory, Path.GetRandomFileName());
        long written;

        try
        {
            await using (var file = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
            {
                await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);

                written = file.Length;
            }

            // Overwriting is correct here and not a race worth guarding: the key is the hash of the
            // bytes, so whoever wins wrote the same file.
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            // A leftover temp file is unreachable — nothing addresses the temp directory — but it is
            // also unbounded growth on a development machine if every failed upload leaves one.
            TryDeleteTemp(tempPath);

            throw;
        }

        return new MediaStoreResult(key, written, contentType);
    }

    /// <inheritdoc />
    public Task<Stream?> GetAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = ResolvePath(key);

        if (!File.Exists(path)) return Task.FromResult<Stream?>(null);

        Stream stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);

        return Task.FromResult<Stream?>(stream);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(File.Exists(ResolvePath(key)));
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            File.Delete(ResolvePath(key));
        }
        catch (DirectoryNotFoundException)
        {
            // File.Delete is a no-op for a missing file but throws for a missing directory, and the
            // interface makes no such distinction: deleting what is not there succeeded. The
            // fan-out directories mean an untouched key is routinely a missing directory rather than
            // a missing file, so this is the ordinary path and not an edge case.
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Always null. A file on the application's own disk has no URL a client could fetch, and
    /// inventing one would mean exposing the root through static file middleware — the single thing
    /// this store's constructor exists to prevent.
    /// </remarks>
    public Uri? GetPublicUrl(string key, TimeSpan? validFor = null) => null;

    /// <summary>
    /// Turns a storage key into an absolute path inside the root.
    /// </summary>
    /// <param name="key">The storage key.</param>
    /// <returns>The absolute path.</returns>
    /// <exception cref="ArgumentException">The key is malformed or escapes the root.</exception>
    private string ResolvePath(string key)
    {
        MediaStorageKeys.Validate(key);

        var combined = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));

        // Unreachable given the key validation above. Kept because the two checks fail differently:
        // the first knows what a key may contain, this one knows what the filesystem did with it —
        // including whatever a symlink, a case-insensitive volume, or a future platform contributes.
        if (!combined.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Storage key resolved outside the media root.", nameof(key));
        }

        return combined;
    }

    private void TryDeleteTemp(string tempPath)
    {
        try
        {
            File.Delete(tempPath);
        }
        catch (IOException exception)
        {
            // Losing a temporary file is not worth replacing the original failure with.
            _logger.LogWarning(exception, "Could not remove the temporary upload file {TempPath}.", tempPath);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Could not remove the temporary upload file {TempPath}.", tempPath);
        }
    }
}
