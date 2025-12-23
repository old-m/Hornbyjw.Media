Hornbyjw.Media

As used on my website: [https://blog.hornbyjw.tech/](https://blog.hornbyjw.tech/)

A tiny media server that exposes a single endpoint to serve blobs from Azure Blob Storage to a web client. It is intended to back a static web app so you don't have to bundle every media asset with the frontend.

## Quick summary

- Endpoint: `GET /media/{**assetPath}` — fetches a blob from the configured container and returns it to the caller.
- Local on-disk cache: downloaded blobs are cached on disk to speed repeated requests and reduce calls to Azure.
- Range support: responses are returned with range processing enabled so clients can request byte ranges (useful for large media).
- Response caching: the controller uses response caching attributes and the app enables response-caching middleware.
# Hornbyjw.Media

A tiny media server that exposes a single endpoint to serve blobs from Azure Blob Storage to a web client. It is intended to back a static web app so you don't have to bundle every media asset with the frontend.

## Quick summary

- Endpoint: `GET /media/{**assetPath}` — fetches a blob from the configured container and returns it to the caller.
- Local on-disk cache: downloaded blobs are cached on disk to speed repeated requests and reduce calls to Azure.
- Range support: responses are returned with range processing enabled so clients can request byte ranges (useful for large media).
- Response caching: the controller uses response caching attributes and the app enables response-caching middleware.

## What the cache does

- Implements a file-based cache (`Services/FileBlobCache.cs`) which:
  - ensures a blob is downloaded once and stored under a cache directory
  - deduplicates concurrent downloads for the same asset (per-asset async lock)
  - writes to a temporary file and moves into place atomically
  - returns a cached file path so the framework opens and disposes file streams properly
  - uses a TTL (configurable) to treat files as stale after a period

## Configuration

Add these keys to `appsettings.json` or supply as environment variables:

- `MediaCache:CacheDirectory` — on-disk cache directory (default: `media-cache`)
- `MediaCache:TTLSeconds` — seconds before a cached file is stale (default: 86400)
- `ContainerName` — the Azure blob container name
- `StorageUrl`, `KeyVaultUrl`, `APPLICATIONINSIGHTS_CONNECTION_STRING` — used to create Azure clients (the project uses `DefaultAzureCredential`)

Example env for local runs:

```
ContainerName=your-container
StorageUrl=https://youraccount.blob.core.windows.net/
KeyVaultUrl=https://your-keyvault.vault.azure.net/
APPLICATIONINSIGHTS_CONNECTION_STRING=...
```

## Notes and rationale

- The cache returns file paths and the controller uses `PhysicalFile(...)` so ASP.NET Core opens/closes file streams and handles range requests — this avoids accidental file-handle leaks.
- Buffer size for file downloads uses 81920 bytes (matches .NET's internal Stream defaults).
- Per-asset `SemaphoreSlim` instances prevent duplicate downloads; dictionary entries are removed after use and semaphores are disposed centrally when the cache is disposed at shutdown.
- There is no size-based eviction yet (only TTL). For production with many unique assets, consider adding LRU or max-size eviction, or put a CDN in front.

## Run

Build and run as a normal .NET web app:

```powershell
dotnet build
dotnet run
```

Then request `/media/<path>` from your browser or client.

## Quick test

Use curl to fetch an asset (example assumes HTTPS local host and that you've configured ports appropriately):

```powershell
# Download the file
curl -v -o output.jpg "https://localhost:5001/media/path/to/image.jpg"

# Inspect headers only
curl -I "https://localhost:5001/media/path/to/image.jpg"
```

Note: adjust host/port/path to match your local launch profile or deployed endpoint.

## Clearing the cache

The cache directory (default `media-cache`) can be cleared safely to free disk space or force fresh downloads. Example commands:

PowerShell:

```powershell
Remove-Item -Recurse -Force .\\media-cache\\*
```

Bash / WSL:

```bash
rm -rf media-cache/*
```

Be cautious when running these in production; ensure you target the correct directory.

## Possible next improvements

- Add cache eviction (max-size / LRU) for long-running servers
- Add ETag/Last-Modified headers and better CDN support
- Replace the semaphore pattern with an AsyncLazy/Task-based deduplication if you prefer that model
