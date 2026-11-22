using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Zetruv.Api.Features.Auth;
using Zetruv.Api.Persistence;

namespace Zetruv.Api.Features.Shipping;

public sealed class CustomerAddress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerUserId { get; set; }
    public string Label { get; set; } = "";
    public string RecipientName { get; set; } = "";
    public string RecipientPhone { get; set; } = "";
    public string? Email { get; set; }
    public string Province { get; set; } = "";
    public string City { get; set; } = "";
    public string District { get; set; } = "";
    public string Subdistrict { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string FullAddress { get; set; } = "";
    public string? Note { get; set; }
    public int RajaOngkirDeliveryId { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record CustomerAddressInput(
    [Required, MaxLength(40)] string Label,
    [Required, MaxLength(120)] string RecipientName,
    [Required, MaxLength(24)] string RecipientPhone,
    [EmailAddress, MaxLength(320)] string? Email,
    [Required, MaxLength(120)] string Province,
    [Required, MaxLength(120)] string City,
    [Required, MaxLength(120)] string District,
    [Required, MaxLength(120)] string Subdistrict,
    [Required, RegularExpression("^[0-9]{5}$")] string PostalCode,
    [Required, MaxLength(500)] string FullAddress,
    [MaxLength(250)] string? Note,
    [Range(1, int.MaxValue)] int RajaOngkirDeliveryId,
    decimal? Latitude,
    decimal? Longitude,
    bool IsDefault = false);

[ApiController]
[Authorize(AuthenticationSchemes = CustomerAuthConstants.Scheme)]
[Route("api/v1/me/addresses")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerAddressesController(
    ZetruvDbContext db, IHttpClientFactory httpClients, IConfiguration config) : ControllerBase
{
    private Guid CustomerId => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var items = await db.CustomerAddresses.AsNoTracking()
            .Where(x => x.CustomerUserId == CustomerId)
            .OrderByDescending(x => x.IsDefault).ThenByDescending(x => x.UpdatedAt).ToListAsync(ct);
        return Ok(new { items });
    }

    [HttpGet("delivery-destinations")]
    [EnableRateLimiting("shipping-quote")]
    public async Task<IActionResult> Search([FromQuery] string? keyword, CancellationToken ct)
    {
        keyword = keyword?.Trim();
        if (keyword is null || keyword.Length is < 3 or > 80)
            return BadRequest(new { code = "INVALID_QUERY", message = "Enter at least 3 characters." });
        var key = config["RajaOngkir:ShippingDelivery:ApiKey"];
        if (string.IsNullOrWhiteSpace(key))
            return StatusCode(503, new { code = "DELIVERY_REGION_UNAVAILABLE",
                message = "RajaOngkir Shipping Delivery API key is not configured." });
        var production = string.Equals(config["RajaOngkir:ShippingDelivery:Environment"],
            "production", StringComparison.OrdinalIgnoreCase);
        var host = production ? "https://api.collaborator.komerce.id" :
            "https://api-sandbox.collaborator.komerce.id";
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{host}/tariff/api/v1/destination/search?keyword={Uri.EscapeDataString(keyword)}");
        request.Headers.Add("x-api-key", key);
        var client = httpClients.CreateClient();
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return StatusCode(502, new { code = "DELIVERY_REGION_UNAVAILABLE" });
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
                return StatusCode(502, new { code = "INVALID_DELIVERY_RESPONSE" });
            var items = data.EnumerateArray().Take(40).Select(x => new {
                id = ReadInt(x, "id"),
                label = ReadString(x, "label"),
                provinceName = ReadString(x, "province_name"),
                cityName = ReadString(x, "city_name"),
                districtName = ReadString(x, "district_name"),
                subdistrictName = ReadString(x, "subdistrict_name"),
                zipCode = ReadString(x, "zip_code")
            }).Where(x => x.id > 0 && !string.IsNullOrWhiteSpace(x.label)).ToList();
            return Ok(new { items, idType = "shipping-delivery" });
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (ct.IsCancellationRequested) throw;
            return StatusCode(502, new { code = "DELIVERY_REGION_UNAVAILABLE" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CustomerAddressInput input, CancellationToken ct)
    {
        if (!Valid(input)) return BadRequest(new { code = "INVALID_ADDRESS" });
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockCustomer(ct);
        var count = await db.CustomerAddresses.CountAsync(x => x.CustomerUserId == CustomerId, ct);
        if (count >= 20) return Conflict(new { code = "ADDRESS_LIMIT" });
        var isDefault = count == 0 || input.IsDefault;
        if (isDefault) await ClearDefaults(ct);
        var address = new CustomerAddress { CustomerUserId = CustomerId, IsDefault = isDefault };
        Assign(address, input);
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Created($"/api/v1/me/addresses/{address.Id}", address);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CustomerAddressInput input, CancellationToken ct)
    {
        if (!Valid(input)) return BadRequest(new { code = "INVALID_ADDRESS" });
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockCustomer(ct);
        var address = await db.CustomerAddresses.SingleOrDefaultAsync(
            x => x.Id == id && x.CustomerUserId == CustomerId, ct);
        if (address is null) return NotFound();
        var isDefault = address.IsDefault || input.IsDefault;
        if (input.IsDefault) await ClearDefaults(ct);
        Assign(address, input);
        address.IsDefault = isDefault;
        if (input.IsDefault) db.Entry(address).Property(x => x.IsDefault).IsModified = true;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(address);
    }

    [HttpPut("{id:guid}/default")]
    public async Task<IActionResult> SetDefault(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockCustomer(ct);
        var address = await db.CustomerAddresses.SingleOrDefaultAsync(
            x => x.Id == id && x.CustomerUserId == CustomerId, ct);
        if (address is null) return NotFound();
        await ClearDefaults(ct);
        address.IsDefault = true;
        address.UpdatedAt = DateTimeOffset.UtcNow;
        db.Entry(address).Property(x => x.IsDefault).IsModified = true;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(address);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockCustomer(ct);
        var address = await db.CustomerAddresses.SingleOrDefaultAsync(
            x => x.Id == id && x.CustomerUserId == CustomerId, ct);
        if (address is null) return NotFound();
        var primary = address.IsDefault;
        db.CustomerAddresses.Remove(address);
        await db.SaveChangesAsync(ct);
        if (primary)
        {
            var next = await db.CustomerAddresses.Where(x => x.CustomerUserId == CustomerId)
                .OrderByDescending(x => x.UpdatedAt).FirstOrDefaultAsync(ct);
            if (next is not null)
            {
                next.IsDefault = true;
                next.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }
        await tx.CommitAsync(ct);
        return NoContent();
    }

    private Task<int> LockCustomer(CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM customer_users WHERE \"Id\" = {CustomerId} FOR UPDATE", ct);
    private Task<int> ClearDefaults(CancellationToken ct) =>
        db.CustomerAddresses.Where(x => x.CustomerUserId == CustomerId && x.IsDefault)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false), ct);

    private static bool Valid(CustomerAddressInput input)
    {
        if (new[] { input.Label, input.RecipientName, input.Province, input.City,
            input.District, input.Subdistrict, input.FullAddress }.Any(string.IsNullOrWhiteSpace))
            return false;
        if (!System.Text.RegularExpressions.Regex.IsMatch(input.RecipientPhone?.Trim() ?? "",
            @"^(?:0|62)[0-9]{8,13}$")) return false;
        if ((input.Latitude is null) != (input.Longitude is null)) return false;
        if (input.Latitude is < -90 or > 90 || input.Longitude is < -180 or > 180) return false;
        return input.RajaOngkirDeliveryId > 0;
    }

    private static void Assign(CustomerAddress a, CustomerAddressInput x)
    {
        a.Label = x.Label.Trim();
        a.RecipientName = x.RecipientName.Trim();
        a.RecipientPhone = x.RecipientPhone.Trim();
        a.Email = x.Email?.Trim();
        a.Province = x.Province.Trim();
        a.City = x.City.Trim();
        a.District = x.District.Trim();
        a.Subdistrict = x.Subdistrict.Trim();
        a.PostalCode = x.PostalCode.Trim();
        a.FullAddress = x.FullAddress.Trim();
        a.Note = x.Note?.Trim();
        a.RajaOngkirDeliveryId = x.RajaOngkirDeliveryId;
        a.Latitude = x.Latitude;
        a.Longitude = x.Longitude;
        a.UpdatedAt = DateTimeOffset.UtcNow;
    }
    private static string? ReadString(JsonElement x, string k) =>
        x.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int ReadInt(JsonElement x, string k) =>
        x.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
}
