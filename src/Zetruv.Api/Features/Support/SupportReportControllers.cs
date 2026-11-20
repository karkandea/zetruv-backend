using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Support;

public sealed class SupportReport
{
    public const int NameLimit = 120;
    public const int PhoneLimit = 40;
    public const int DescriptionLimit = 2000;

    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Phone { get; init; }
    public required string Description { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record CreateSupportReportRequest(
    [Required, StringLength(SupportReport.NameLimit)] string Name,
    [Required, StringLength(SupportReport.PhoneLimit), RegularExpression(@"^\+?[0-9 ().-]+$")] string Phone,
    [Required, StringLength(SupportReport.DescriptionLimit)] string Description,
    Guid? RequestId);

[ApiController]
[Route("api/v1/support/reports")]
public sealed class SupportReportsController(ZetruvDbContext db) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("support-reports")]
    [RequestSizeLimit(32 * 1024)]
    public async Task<IActionResult> Create(CreateSupportReportRequest request, CancellationToken ct)
    {
        if (request.Phone.Count(char.IsAsciiDigit) is < 8 or > 15 || request.RequestId == Guid.Empty)
            return BadRequest(new { message = "Enter a valid phone number and report request ID." });

        var report = new SupportReport
        {
            Id = request.RequestId ?? Guid.NewGuid(),
            Name = request.Name.Trim(),
            Phone = request.Phone.Trim(),
            Description = request.Description.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.SupportReports.Add(report);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "PK_support_reports"
        })
        {
            // A retry can arrive after a successful write whose response was lost.
            var matches = await db.SupportReports.AsNoTracking().AnyAsync(x =>
                x.Id == report.Id && x.Name == report.Name && x.Phone == report.Phone &&
                x.Description == report.Description, ct);
            if (!matches)
                return Conflict(new { message = "This request ID was used for a different report." });

            return Ok(new { report.Id });
        }

        return StatusCode(StatusCodes.Status201Created, new { report.Id });
    }
}

[ApiController]
[Authorize(Policy = AuthPolicies.CmsAdmin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/v1/cms/support/reports")]
public sealed class CmsSupportReportsController(ZetruvDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([Range(0, int.MaxValue)] int offset = 0,
        [Range(1, 100)] int limit = 20, CancellationToken ct = default)
    {
        var reports = await db.SupportReports.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip(offset)
            .Take(limit + 1)
            .ToListAsync(ct);

        return Ok(new { items = reports.Take(limit), hasMore = reports.Count > limit });
    }
}
