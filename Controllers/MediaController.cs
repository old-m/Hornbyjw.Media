namespace Hornbyjw.Media.Controllers
{
    using Azure.Storage.Blobs;
    using Azure.Storage.Blobs.Specialized;
    using Microsoft.AspNetCore.Mvc;
    using Hornbyjw.Media.Services;
    using System.IO;

    [ApiController]
    [Route("media")]
    public class MediaController : ControllerBase
    {
    private readonly ILogger<MediaController> _logger;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly IConfiguration _configuration;
    private readonly IFileBlobCache _fileBlobCache;

        public MediaController(ILogger<MediaController> logger, BlobServiceClient blobServiceClient, IConfiguration configuration, IFileBlobCache fileBlobCache)
        {
            _logger = logger;
            _blobServiceClient = blobServiceClient;
            _configuration = configuration;
            _fileBlobCache = fileBlobCache;
        }

        /// <summary>
        /// Get a blob item stored at a requested virtual path
        /// </summary>
        /// <param name="assetPath">The URL path including extension</param>
        /// <returns>A stream of the requested blob</returns>
        [Route("{**assetPath}")]
        [HttpGet]
        [ResponseCache(Duration = 2629746, Location = ResponseCacheLocation.Any)]
        public async Task<IActionResult> Get(string assetPath)
        {
            var mimeType = CheckValidExtensionAndReturnMimeType(Path.GetExtension(assetPath));

            if (!string.IsNullOrEmpty(mimeType))
            {
                var container = _blobServiceClient.GetBlobContainerClient(_configuration.GetValue<string>("ContainerName"));

                // Download or serve from cache
                var blobClient = container.GetBlockBlobClient(assetPath);
                if (await blobClient.ExistsAsync())
                {
                    // Use local file cache to speed repeated reads. This returns the cached file path.
                    var cachedPath = await _fileBlobCache.GetOrDownloadAsync(assetPath, blobClient).ConfigureAwait(false);
                    // Return a PhysicalFile so the framework opens and disposes the file stream and supports ranges
                    return PhysicalFile(cachedPath, mimeType, enableRangeProcessing: true);
                }
            }
            return BadRequest("Requested a bad path.");
        }

        /// <summary>
        /// Performs a few semantic tasks.
        /// 1. Checks that you've actually requested a file (with an extension)
        /// 2. Limits the types of file that one can request
        /// 3. While we're iterating through file extensions, return the mime type
        /// </summary>
        /// <param name="extension">The file extension to validate and return a mime type for</param>
        /// <returns>The mime type for your extension, if it exists and is valid, otherwise an empty string</returns>
        private string CheckValidExtensionAndReturnMimeType(string extension)
        {
            if (!string.IsNullOrEmpty(extension))
            {
                extension = extension.ToLower();

                switch (extension)
                {
                    case ".jpg":
                        return "image/jpeg";
                    case ".png":
                        return "image/png";
                    case ".gif":
                        return "image/gif";
                    case ".jpeg":
                        return "image/jpeg";
                    case ".webp":
                        return "image/webp";
                    case ".pdf":
                        return "application/pdf";
                    case ".ico":
                        return "image/x-icon";
                    case ".avif":
                        return "image/avif";
                    case ".webm":
                        return "video/webm";
                }
            }

            return string.Empty;
        }
    }
}
