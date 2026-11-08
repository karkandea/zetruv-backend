using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Home
{
    [ApiController]
    [Route("api/v1/homepage")]
    public sealed class HomepageController(HomepageService homepage) : ControllerBase
    {
        [HttpGet("personal")]
        [Authorize(AuthenticationSchemes = CustomerAuthConstants.Scheme)]
        public async Task<ActionResult<HomepageResponse>> Personal(CancellationToken cancellationToken)
        {
            var customerId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            Response.Headers.CacheControl = "private, no-store";
            return Ok(await homepage.GetPersonalAsync(customerId, cancellationToken));
        }

        [HttpGet]
        [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Any)]
        public async Task<ActionResult<HomepageResponse>> Get(
            CancellationToken cancellationToken)
        {
            return Ok(await homepage.GetAsync(cancellationToken));
        }
    }

    [ApiController]
    [Authorize(Policy = AuthPolicies.CmsAdmin)]
    [Route("api/v1/cms/homepage")]
    [Route("api/v1/admin/homepage")]
    public sealed class AdminHomepageController(ZetruvDbContext db) : ControllerBase
    {
        public sealed record HeroPosition(Guid Id);
        public sealed record SectionPosition(string Key);

        [HttpPut("heroes/reorder")]
        public async Task<IActionResult> ReorderHeroes(
            [FromBody] List<HeroPosition> positions,
            CancellationToken cancellationToken)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(8234710321)", cancellationToken);
            var heroes = await db.HomeHeroes.ToListAsync(cancellationToken);
            if (positions.Count != heroes.Count || positions.Select(x => x.Id).Distinct().Count() != positions.Count)
                return BadRequest(new { message = "Daftar urutan banner tidak lengkap. Muat ulang halaman dan coba lagi." });
            var byId = heroes.ToDictionary(x => x.Id);
            for (var i = 0; i < positions.Count; i++)
            {
                if (!byId.TryGetValue(positions[i].Id, out var hero))
                    return BadRequest(new { message = "Banner tidak ditemukan. Muat ulang halaman dan coba lagi." });
                hero.SortOrder = (i + 1) * 10;
                hero.UpdatedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return NoContent();
        }

        [HttpPut("sections/reorder")]
        public async Task<IActionResult> ReorderSections(
            [FromBody] List<SectionPosition> positions,
            CancellationToken cancellationToken)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var sections = await db.HomeSections.ToListAsync(cancellationToken);
            if (positions.Count != sections.Count || positions.Select(x => x.Key).Distinct().Count() != positions.Count)
                return BadRequest(new { message = "Daftar bagian beranda tidak lengkap. Muat ulang halaman dan coba lagi." });
            var byKey = sections.ToDictionary(x => x.Key);
            for (var i = 0; i < positions.Count; i++)
            {
                if (!byKey.TryGetValue(positions[i].Key, out var section))
                    return BadRequest(new { message = "Bagian tidak ditemukan. Muat ulang halaman dan coba lagi." });
                section.SortOrder = (i + 1) * 10;
                section.UpdatedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return NoContent();
        }

        [HttpGet("heroes")]
        public async Task<ActionResult<IReadOnlyList<HomeHero>>> GetHeroes(
            CancellationToken cancellationToken)
        {
            var heroes = await db.HomeHeroes
                .AsNoTracking()
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.CreatedAt)
                .ToListAsync(cancellationToken);

            return Ok(heroes);
        }

        [HttpPost("heroes")]
        public async Task<ActionResult<HomeHero>> CreateHero(
            [FromBody] UpsertHeroRequest request,
            CancellationToken cancellationToken)
        {
            if (request.EndsAt is not null &&
                request.StartsAt is not null &&
                request.EndsAt <= request.StartsAt)
            {
                ModelState.AddModelError(
                    nameof(request.EndsAt),
                    "EndsAt must be later than StartsAt.");
                return ValidationProblem(ModelState);
            }

            if (!ValidHeroLinks(request))
                return BadRequest(new { message = "CTA URLs must be local paths or HTTPS URLs, with paired labels." });
            if (!await ValidHeroImage(request, cancellationToken))
                return BadRequest(new { message = "Gambar banner belum valid. Unggah gambar dari halaman Banner sebelum menayangkan." });
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            // Serialize concurrent CMS inserts so the stored banner quota is deterministic.
            await db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(8234710321)", cancellationToken);
            if (await db.HomeHeroes.CountAsync(cancellationToken) >= 50)
                return Conflict(new { code = "HERO_LIMIT_REACHED", message = "Maksimal 50 banner, termasuk banner nonaktif." });
            var hero = new HomeHero();
            Apply(hero, request);
            db.HomeHeroes.Add(hero);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return StatusCode(StatusCodes.Status201Created, hero);
        }

        [HttpPut("heroes/{id:guid}")]
        public async Task<ActionResult<HomeHero>> UpdateHero(
            Guid id,
            [FromBody] UpsertHeroRequest request,
            CancellationToken cancellationToken)
        {
            var hero = await db.HomeHeroes.FindAsync([id], cancellationToken);
            if (hero is null)
            {
                return NotFound();
            }

            if (request.EndsAt is not null &&
                request.StartsAt is not null &&
                request.EndsAt <= request.StartsAt)
            {
                ModelState.AddModelError(
                    nameof(request.EndsAt),
                    "EndsAt must be later than StartsAt.");
                return ValidationProblem(ModelState);
            }

            if (!ValidHeroLinks(request))
                return BadRequest(new { message = "CTA URLs must be local paths or HTTPS URLs, with paired labels." });
            if (!await ValidHeroImage(request, cancellationToken))
                return BadRequest(new { message = "Gambar banner tidak valid. Unggah ulang gambar sebelum menayangkan." });
            Apply(hero, request);
            await db.SaveChangesAsync(cancellationToken);

            return Ok(hero);
        }

        [HttpDelete("heroes/{id:guid}")]
        public async Task<IActionResult> DeleteHero(
            Guid id,
            CancellationToken cancellationToken)
        {
            var hero = await db.HomeHeroes.FindAsync([id], cancellationToken);
            if (hero is null)
            {
                return NotFound();
            }

            db.HomeHeroes.Remove(hero);
            await db.SaveChangesAsync(cancellationToken);

            return NoContent();
        }

        [HttpGet("sections")]
        public async Task<ActionResult<IReadOnlyList<HomeSection>>> GetSections(
            CancellationToken cancellationToken)
        {
            var sections = await db.HomeSections
                .AsNoTracking()
                .OrderBy(x => x.SortOrder)
                .ToListAsync(cancellationToken);

            return Ok(sections);
        }

        [HttpPut("sections/{key}")]
        public async Task<ActionResult<HomeSection>> UpdateSection(
            string key,
            [FromBody] UpdateSectionRequest request,
            CancellationToken cancellationToken)
        {
            var section = await db.HomeSections
                .SingleOrDefaultAsync(x => x.Key == key, cancellationToken);

            if (section is null)
            {
                return NotFound();
            }

            section.Title = request.Title.Trim();
            section.Subtitle = request.Subtitle?.Trim();
            section.CtaLabel = request.CtaLabel?.Trim();
            section.CtaUrl = request.CtaUrl?.Trim();
            section.IsEnabled = request.IsEnabled;
            section.SortOrder = request.SortOrder;
            section.ItemLimit = request.ItemLimit;
            section.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(cancellationToken);

            return Ok(section);
        }

        private static bool ValidHeroLinks(UpsertHeroRequest request) =>
            ValidLink(request.PrimaryCtaLabel, request.PrimaryCtaUrl) &&
            ValidLink(request.SecondaryCtaLabel, request.SecondaryCtaUrl);

        private async Task<bool> ValidHeroImage(UpsertHeroRequest request, CancellationToken cancellationToken)
        {
            if (!request.IsActive) return true; // Hidden drafts and legacy images can be repaired.
            var imageUrl = request.ImageUrl?.Trim() ?? string.Empty;
            const string marker = "/media/banners/";
            var markerIndex = imageUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            // Keep the existing REST contract: older banners may use valid HTTPS or
            // local image paths. The new admin editor only exposes stored uploads.
            // Invalid strings such as "asofdjtogjesf" must never be publishable.
            var validHttps = Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);
            var validLocal = imageUrl.StartsWith('/') && !imageUrl.StartsWith("//") &&
                !imageUrl.Contains('\\');
            // Local integration/dev servers may return http:// for media served
            // by this same API host. External insecure HTTP links stay blocked.
            var validSameOriginMedia = markerIndex >= 0 && uri is not null &&
                uri.Scheme is "http" or "https" &&
                string.Equals(uri.Authority, Request.Host.Value, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrEmpty(uri.UserInfo);
            if (!validHttps && !validLocal && !validSameOriginMedia)
                return false;

            if (markerIndex < 0) return true; // Backward-compatible legacy URLs.

            var storageKey = imageUrl[(markerIndex + "/media/".Length)..];
            return await db.MediaAssets.AnyAsync(
                asset => asset.StorageKey == storageKey && asset.DeletedAt == null,
                cancellationToken);
        }

        private static bool ValidLink(string? label, string? url)
        {
            if (string.IsNullOrWhiteSpace(label) && string.IsNullOrWhiteSpace(url)) return true;
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(url)) return false;
            var value = url.Trim();
            return (value.StartsWith('/') && !value.StartsWith("//") && !value.Contains('\\')) ||
                (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                 uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo));
        }

        private static void Apply(HomeHero hero, UpsertHeroRequest request)
        {
            hero.Title = request.Title.Trim();
            hero.Subtitle = request.Subtitle.Trim();
            hero.ImageUrl = request.ImageUrl.Trim();
            hero.PrimaryCtaLabel = request.PrimaryCtaLabel?.Trim();
            hero.PrimaryCtaUrl = request.PrimaryCtaUrl?.Trim();
            hero.SecondaryCtaLabel = request.SecondaryCtaLabel?.Trim();
            hero.SecondaryCtaUrl = request.SecondaryCtaUrl?.Trim();
            hero.IsActive = request.IsActive;
            hero.SortOrder = request.SortOrder;
            hero.StartsAt = request.StartsAt;
            hero.EndsAt = request.EndsAt;
            hero.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }
}
