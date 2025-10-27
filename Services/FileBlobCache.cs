namespace Hornbyjw.Media.Services
{
    using Azure.Storage.Blobs.Specialized;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IFileBlobCache : IDisposable
    {
        /// <summary>
        /// Ensure the blob is cached locally and return the full file path to the cached file.
        /// </summary>
        Task<string> GetOrDownloadAsync(string assetPath, BlockBlobClient blobClient, CancellationToken cancellationToken = default);
    }

    public class FileBlobCache : IFileBlobCache
    {
        private readonly string _cacheRoot;
        private readonly TimeSpan _ttl;
        private readonly ILogger<FileBlobCache> _logger;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
        private bool _disposed;

        public FileBlobCache(IConfiguration configuration, ILogger<FileBlobCache> logger)
        {
            _logger = logger;
            var section = configuration.GetSection("MediaCache");
            var configured = section.GetValue<string>("CacheDirectory");
            if (string.IsNullOrEmpty(configured))
            {
                _cacheRoot = Path.Combine(Directory.GetCurrentDirectory(), "media-cache");
            }
            else
            {
                // If a relative path is provided in configuration, make it absolute based on current directory
                _cacheRoot = Path.IsPathRooted(configured) ? configured : Path.Combine(Directory.GetCurrentDirectory(), configured);
            }
            var ttlSeconds = section.GetValue<int?>("TTLSeconds") ?? 24 * 60 * 60; // default 1 day
            _ttl = TimeSpan.FromSeconds(ttlSeconds);

            Directory.CreateDirectory(_cacheRoot);
        }

        public async Task<string> GetOrDownloadAsync(string assetPath, BlockBlobClient blobClient, CancellationToken cancellationToken = default)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FileBlobCache));
            }
            // Sanitize assetPath into a relative file path under cache root
            var safeRelative = MakeSafeRelativePath(assetPath);
            var finalPath = Path.Combine(_cacheRoot, safeRelative);

            // Ensure directory exists
            var dir = Path.GetDirectoryName(finalPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // If cached and fresh, return path
            if (File.Exists(finalPath))
            {
                var fi = new FileInfo(finalPath);
                var age = DateTime.UtcNow - fi.LastWriteTimeUtc;
                if (age <= _ttl)
                {
                    _logger.LogDebug("Cache hit for {AssetPath}", assetPath);
                    return Path.GetFullPath(finalPath);
                }
                else
                {
                    _logger.LogDebug("Cache expired for {AssetPath}", assetPath);
                }
            }

            // Acquire per-key lock to avoid multiple downloads
            var sem = _locks.GetOrAdd(finalPath, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Double-check after acquiring lock
                if (File.Exists(finalPath))
                {
                    var fi = new FileInfo(finalPath);
                    var age = DateTime.UtcNow - fi.LastWriteTimeUtc;
                        if (age <= _ttl)
                        {
                            _logger.LogDebug("Cache hit (after wait) for {AssetPath}", assetPath);
                            return Path.GetFullPath(finalPath);
                        }
                }

                // Download to a temp file then move into place
                var tempPath = finalPath + ".download" + Guid.NewGuid().ToString("N");
                try
                {
                    _logger.LogInformation("Downloading blob to cache: {AssetPath} -> {Path}", assetPath, finalPath);
                    using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                    {
                        await blobClient.DownloadToAsync(fs, cancellationToken).ConfigureAwait(false);
                    }

                    // Move into place (overwrite if necessary)
                    if (File.Exists(finalPath)) File.Delete(finalPath);
                    File.Move(tempPath, finalPath);

                    // Return the cached file path
                    return Path.GetFullPath(finalPath);
                }
                catch
                {
                    // Remove temp file on failure
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                    throw;
                }
            }
            finally
            {
                sem.Release();
                // remove semaphore entry (non-blocking). Do not dispose here —
                // disposal is handled centrally in Dispose() to avoid races with
                // other waiters that may still be transitioning after Release().
                _locks.TryRemove(finalPath, out _);
            }
        }

        private static string MakeSafeRelativePath(string assetPath)
        {
            // Normalize separators and remove any leading slashes
            var parts = assetPath.Replace('\\', '/').TrimStart('/').Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i];
                foreach (var c in Path.GetInvalidFileNameChars()) p = p.Replace(c, '_');
                parts[i] = p;
            }
            return Path.Combine(parts);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                // Dispose all semaphores
                foreach (var sem in _locks.Values)
                {
                    try
                    {
                        sem.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error disposing semaphore in cache cleanup");
                    }
                }
                
                _locks.Clear();
            }
        }
    }
}
