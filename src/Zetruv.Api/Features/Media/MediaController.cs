using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Zetruv.Api.Features.Auth;

namespace Zetruv.Api.Features.Media;

public sealed record MediaAssetResponse(
    Guid Id,
    string Url,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAt);

[ApiController]
[Authorize(Policy = AuthPolicies.CmsAdmin)]
[Route("api/v1/cms/media")]
public sealed class CmsMediaController(
    MediaService mediaService,
    BannerMediaService bannerMediaService,
    IOptions<MediaOptions> options) : ControllerBase
{
    [HttpGet("banner/capabilities")]
    public IActionResult BannerCapabilities() => Ok(new
    {
        enabled = true,
        maxUploadBytes = BannerMediaService.InputLimit,
        targetBytes = BannerMediaService.OutputTarget,
        maxBanners = BannerMediaService.BannerLimit,
        storageQuotaBytes = BannerMediaService.StorageQuota,
        formats = new[] { "image/jpeg", "image/png", "image/webp" }
    });

    [HttpPost("banner")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<ActionResult<MediaAssetResponse>> UploadBanner(
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        var result = await bannerMediaService.UploadAsync(file, cancellationToken);
        return result.Asset is null
            ? StatusCode(result.StatusCode, new { message = result.Error })
            : StatusCode(StatusCodes.Status201Created, ToResponse(result.Asset));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MediaAssetResponse>>> List(
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var assets = await mediaService.ListAsync(limit, cancellationToken);
        return Ok(assets.Select(ToResponse).ToList());
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<MediaAssetResponse>> Upload(
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        var result = await mediaService.UploadAsync(file, cancellationToken);
        if (result.Asset is null)
        {
            return StatusCode(
                result.StatusCode,
                new { message = result.Error });
        }

        return StatusCode(
            StatusCodes.Status201Created,
            ToResponse(result.Asset));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await mediaService.DeleteAsync(id, cancellationToken);
            return deleted ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Gambar masih digunakan", StringComparison.Ordinal))
        {
            return Conflict(new { message = ex.Message });
        }
    }

    private MediaAssetResponse ToResponse(MediaAsset asset) =>
        new(
            asset.Id,
            BuildUrl(asset.StorageKey),
            asset.OriginalFileName,
            asset.ContentType,
            asset.SizeBytes,
            asset.CreatedAt);

    private string BuildUrl(string storageKey)
    {
        var publicPath = MediaPaths.NormalizePublicPath(options.Value.PublicPath);
        var escapedKey = string.Join(
            "/",
            storageKey.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));

        return $"{Request.Scheme}://{Request.Host}{Request.PathBase}{publicPath}/{escapedKey}";
    }
}
