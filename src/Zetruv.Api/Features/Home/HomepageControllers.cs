using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Home
{
    [ApiController]
    [Route("api/v1/homepage")]
    public sealed class HomepageController(HomepageService homepage) : ControllerBase
    {
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

            var hero = new HomeHero();
            Apply(hero, request);

            db.HomeHeroes.Add(hero);
            await db.SaveChangesAsync(cancellationToken);

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
