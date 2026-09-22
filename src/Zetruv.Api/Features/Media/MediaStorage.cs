using Microsoft.Extensions.Options;

namespace Zetruv.Api.Features.Media;

public sealed class MediaOptions
{
    public const string SectionName = "Media";

    public string Provider { get; init; } = "local";
    public string LocalPath { get; init; } = "media";
    public string PublicPath { get; init; } = "/media";
    public long MaxFileSizeBytes { get; init; } = 5 * 1024 * 1024;
}

public sealed class MediaAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed record MediaStoreRequest(
    Guid AssetId,
    string Extension,
    Stream Content);

public interface IMediaStorage
{
    string Name { get; }

    Task<string> StoreAsync(
        MediaStoreRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default);
}

public sealed class LocalMediaStorage(
    IOptions<MediaOptions> options,
    IHostEnvironment environment) : IMediaStorage
{
    private readonly string _root = MediaPaths.ResolveLocalRoot(options.Value, environment);

    public string Name => "local";

    public async Task<string> StoreAsync(
        MediaStoreRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var relativeDirectory = Path.Combine(
            now.ToString("yyyy"),
            now.ToString("MM"));
        var fileName = $"{request.AssetId:N}{request.Extension}";
        var storageKey = Path.Combine(relativeDirectory, fileName)
            .Replace(Path.DirectorySeparatorChar, '/');

        var targetDirectory = Path.Combine(_root, relativeDirectory);
        Directory.CreateDirectory(targetDirectory);

        var targetPath = Path.GetFullPath(Path.Combine(_root, storageKey));
        EnsureUnderRoot(_root, targetPath);

        await using var target = new FileStream(
            targetPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        request.Content.Position = 0;
        await request.Content.CopyToAsync(target, cancellationToken);
        return storageKey;
    }

    public Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var targetPath = Path.GetFullPath(Path.Combine(
            _root,
            storageKey.Replace('/', Path.DirectorySeparatorChar)));
        EnsureUnderRoot(_root, targetPath);

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        return Task.CompletedTask;
    }

    private static void EnsureUnderRoot(string root, string targetPath)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        if (!targetPath.StartsWith(normalizedRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid media storage path.");
        }
    }
}

public sealed record MediaFileDescriptor(
    string ContentType,
    string Extension);

public static class MediaFileValidation
{
    public static MediaFileDescriptor? Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 &&
            bytes[1] == 0x50 &&
            bytes[2] == 0x4E &&
            bytes[3] == 0x47 &&
            bytes[4] == 0x0D &&
            bytes[5] == 0x0A &&
            bytes[6] == 0x1A &&
            bytes[7] == 0x0A)
        {
            return new("image/png", ".png");
        }

        if (bytes.Length >= 3 &&
            bytes[0] == 0xFF &&
            bytes[1] == 0xD8 &&
            bytes[2] == 0xFF)
        {
            return new("image/jpeg", ".jpg");
        }

        if (bytes.Length >= 12 &&
            bytes[0] == (byte)'R' &&
            bytes[1] == (byte)'I' &&
            bytes[2] == (byte)'F' &&
            bytes[3] == (byte)'F' &&
            bytes[8] == (byte)'W' &&
            bytes[9] == (byte)'E' &&
            bytes[10] == (byte)'B' &&
            bytes[11] == (byte)'P')
        {
            return new("image/webp", ".webp");
        }

        return null;
    }
}
