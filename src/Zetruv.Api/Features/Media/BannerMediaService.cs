using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Media;

/// <summary>
/// Optimized, quota-limited CMS uploads are independent of the legacy generic uploader.
/// The persisted image is always a validated, server-encoded WebP file.
/// </summary>
public sealed class BannerMediaService(
    ZetruvDbContext db,
    MediaStorageResolver resolver,
    IOptions<MediaOptions> mediaOptions)
{
    public const long InputLimit = 3 * 1024 * 1024;
    public const long OutputTarget = 500 * 1024;
    public const long StorageQuota = 100 * 1024 * 1024;
    public const int BannerLimit = 50; // Homepage controller enforces the number of banners.

    public Task<MediaUploadResult> UploadAsync(IFormFile file, CancellationToken ct) =>
        UploadOptimizedAsync(file, "banners", ct);

    public Task<MediaUploadResult> UploadCatalogAsync(IFormFile file, CancellationToken ct) =>
        UploadOptimizedAsync(file, "catalog", ct);

    private async Task<MediaUploadResult> UploadOptimizedAsync(IFormFile file, string category, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return MediaUploadResult.Failure("Pilih gambar untuk diunggah.", 400);
        if (file.Length > InputLimit)
            return MediaUploadResult.Failure("Gambar terlalu besar. Maksimal ukuran gambar adalah 3 MB. Silakan pilih gambar lain.", 413);

        await using var input = new MemoryStream(checked((int)file.Length));
        await file.CopyToAsync(input, ct);
        if (input.Length > InputLimit)
            return MediaUploadResult.Failure("Gambar terlalu besar. Maksimal ukuran gambar adalah 3 MB.", 413);
        if (MediaFileValidation.Detect(input.GetBuffer().AsSpan(0, (int)Math.Min(32, input.Length))) is null)
            return MediaUploadResult.Failure("Format gambar harus JPG, PNG, atau WebP.", 415);

        byte[] optimized;
        try
        {
            using var data = SKData.CreateCopy(input.ToArray());
            using var codec = SKCodec.Create(data);
            if (codec is null)
                return MediaUploadResult.Failure("Gambar tidak dapat dibaca. Pilih file gambar lain.", 422);

            var info = codec.Info;
            if (info.Width < 16 || info.Height < 16 ||
                info.Width > 4096 || info.Height > 4096 ||
                (long)info.Width * info.Height > 8_388_608)
                return MediaUploadResult.Failure("Dimensi gambar tidak didukung. Gunakan maksimal 4096 piksel dan 8 megapiksel.", 422);

            using var bitmap = SKBitmap.Decode(data);
            if (bitmap is null)
                return MediaUploadResult.Failure("Gambar tidak dapat dibaca. Pilih file gambar lain.", 422);

            using var image = SKImage.FromBitmap(bitmap);
            optimized = Array.Empty<byte>();
            foreach (var quality in new[] { 86, 78, 70, 62, 54, 46, 38, 30, 22 })
            {
                using var webp = image.Encode(SKEncodedImageFormat.Webp, quality);
                if (webp is null) continue;
                optimized = webp.ToArray();
                if (optimized.Length <= OutputTarget) break;
            }

            if (optimized.Length == 0 || optimized.Length > OutputTarget)
                return MediaUploadResult.Failure("Gambar terlalu kompleks untuk dioptimasi. Coba gambar dengan dimensi lebih kecil.", 422);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return MediaUploadResult.Failure("Gambar tidak dapat diproses. Pilih JPG, PNG, atau WebP yang valid.", 422);
        }

        var storage = resolver.Resolve();
        if (storage is null || storage.Name != "local")
            return MediaUploadResult.Failure("Penyimpanan gambar belum tersedia.", 503);

        // Both storage quota checks and writes are serialized within the DEV/PROD database.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(8234710350)", ct);
        var prefix = category + "/";
        var assets = db.MediaAssets.Where(x => x.DeletedAt == null && x.StorageKey.StartsWith(prefix));
        var storedBytes = await assets.SumAsync(x => (long?)x.SizeBytes, ct) ?? 0;
        if (storedBytes + optimized.LongLength > StorageQuota)
            return MediaUploadResult.Failure("Kuota penyimpanan gambar 100 MB sudah penuh.", 409);

        var asset = new MediaAsset
        {
            Provider = storage.Name,
            OriginalFileName = CleanFileName(file.FileName),
            ContentType = "image/webp",
            SizeBytes = optimized.LongLength
        };
        await using var content = new MemoryStream(optimized, writable: false);
        try
        {
            asset.StorageKey = await storage.StoreAsync(new MediaStoreRequest(asset.Id, ".webp", content, category), ct);
            db.MediaAssets.Add(asset);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            if (!string.IsNullOrEmpty(asset.StorageKey))
                await storage.DeleteAsync(asset.StorageKey, CancellationToken.None);
            throw;
        }
        return MediaUploadResult.Success(asset);
    }

    private static string CleanFileName(string? name)
    {
        var safe = Path.GetFileName(name ?? string.Empty).Trim();
        return string.IsNullOrEmpty(safe) ? "banner" : safe[..Math.Min(safe.Length, 255)];
    }
}
