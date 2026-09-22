using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Media;

public sealed record MediaUploadResult(
    MediaAsset? Asset,
    string? Error,
    int StatusCode)
{
    public static MediaUploadResult Success(MediaAsset asset) =>
        new(asset, null, StatusCodes.Status201Created);

    public static MediaUploadResult Failure(string error, int statusCode) =>
        new(null, error, statusCode);
}

public sealed class MediaStorageResolver(
    IEnumerable<IMediaStorage> storages,
    IOptions<MediaOptions> options)
{
    public IMediaStorage? Resolve() =>
        ResolveByName(options.Value.Provider);

    public IMediaStorage? ResolveByName(string provider) =>
        storages.FirstOrDefault(x =>
            string.Equals(
                x.Name,
                provider.Trim(),
                StringComparison.OrdinalIgnoreCase));
}

public sealed class MediaService(
    ZetruvDbContext db,
    MediaStorageResolver resolver,
    IOptions<MediaOptions> options)
{
    private readonly MediaOptions _options = options.Value;

    public async Task<MediaUploadResult> UploadAsync(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (file.Length <= 0)
        {
            return MediaUploadResult.Failure(
                "Media file cannot be empty.",
                StatusCodes.Status400BadRequest);
        }

        var maxFileSize = Math.Clamp(
            _options.MaxFileSizeBytes,
            64 * 1024,
            10 * 1024 * 1024);

        if (file.Length > maxFileSize)
        {
            return MediaUploadResult.Failure(
                $"Media file exceeds the {maxFileSize} byte limit.",
                StatusCodes.Status413PayloadTooLarge);
        }

        await using var buffer = new MemoryStream(
            capacity: checked((int)Math.Min(file.Length, int.MaxValue)));
        await file.CopyToAsync(buffer, cancellationToken);

        if (buffer.Length > maxFileSize)
        {
            return MediaUploadResult.Failure(
                $"Media file exceeds the {maxFileSize} byte limit.",
                StatusCodes.Status413PayloadTooLarge);
        }

        var headerLength = (int)Math.Min(buffer.Length, 32);
        var descriptor = MediaFileValidation.Detect(
            buffer.GetBuffer().AsSpan(0, headerLength));
        if (descriptor is null)
        {
            return MediaUploadResult.Failure(
                "Unsupported media file. Allowed formats: PNG, JPEG, and WebP.",
                StatusCodes.Status415UnsupportedMediaType);
        }

        var storage = resolver.Resolve();
        if (storage is null)
        {
            return MediaUploadResult.Failure(
                "Media storage provider is not configured.",
                StatusCodes.Status503ServiceUnavailable);
        }

        var asset = new MediaAsset
        {
            Provider = storage.Name,
            OriginalFileName = CleanFileName(file.FileName),
            ContentType = descriptor.ContentType,
            SizeBytes = buffer.Length
        };

        string storageKey;
        try
        {
            storageKey = await storage.StoreAsync(
                new MediaStoreRequest(
                    asset.Id,
                    descriptor.Extension,
                    buffer),
                cancellationToken);
        }
        catch (IOException)
        {
            return MediaUploadResult.Failure(
                "Media file could not be stored.",
                StatusCodes.Status503ServiceUnavailable);
        }

        asset.StorageKey = storageKey;
        db.MediaAssets.Add(asset);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await storage.DeleteAsync(storageKey, CancellationToken.None);
            throw;
        }

        return MediaUploadResult.Success(asset);
    }

    public async Task<IReadOnlyList<MediaAsset>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default) =>
        await db.MediaAssets
            .AsNoTracking()
            .Where(x => x.DeletedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);

    public async Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var asset = await db.MediaAssets
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (asset is null)
        {
            return false;
        }

        if (asset.DeletedAt.HasValue)
        {
            return true;
        }

        var storage = resolver.ResolveByName(asset.Provider);
        if (storage is null)
        {
            throw new InvalidOperationException(
                $"Media storage provider '{asset.Provider}' is not configured.");
        }

        await storage.DeleteAsync(asset.StorageKey, cancellationToken);
        asset.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string CleanFileName(string? fileName)
    {
        var safe = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(safe))
        {
            return "upload";
        }

        return safe[..Math.Min(safe.Length, 255)];
    }
}

public static class MediaPaths
{
    public static string ResolveLocalRoot(
        MediaOptions options,
        IHostEnvironment environment)
    {
        var configured = string.IsNullOrWhiteSpace(options.LocalPath)
            ? "media"
            : options.LocalPath.Trim();

        return Path.GetFullPath(
            Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(environment.ContentRootPath, configured));
    }

    public static string NormalizePublicPath(string? value)
    {
        var path = string.IsNullOrWhiteSpace(value)
            ? "/media"
            : value.Trim();

        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        path = path.TrimEnd('/');
        if (path.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Media public path is invalid.");
        }

        return path;
    }
}
