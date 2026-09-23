using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Zetruv.Api.Features.Auth;

public sealed class CustomerEmailSender(
    IOptions<CustomerEmailOptions> options,
    IOptions<CustomerAuthOptions> authOptions,
    IHostEnvironment environment,
    IHttpClientFactory httpClientFactory)
{
    private readonly CustomerEmailOptions _options = options.Value;

    public bool IsReady =>
        Uri.TryCreate(authOptions.Value.FrontendBaseUrl, UriKind.Absolute, out var frontend)
        && (environment.IsDevelopment() || frontend.Scheme == Uri.UriSchemeHttps)
        && (_options.Provider.ToLowerInvariant() switch
        {
            "resend" => !string.IsNullOrWhiteSpace(_options.ResendApiKey)
                && MailAddress.TryCreate(_options.FromAddress, out _),
            "smtp" => !string.IsNullOrWhiteSpace(_options.SmtpHost)
                && MailAddress.TryCreate(_options.FromAddress, out _),
            "capture" => environment.IsDevelopment()
                && !string.IsNullOrWhiteSpace(_options.CaptureDirectory),
            _ => false
        });

    public async Task SendAsync(string to, string subject, string html, CancellationToken ct)
    {
        if (!IsReady) throw new InvalidOperationException("Customer email delivery is unavailable.");

        switch (_options.Provider.ToLowerInvariant())
        {
            case "capture":
                Directory.CreateDirectory(_options.CaptureDirectory);
                var file = Path.Combine(_options.CaptureDirectory, $"{Guid.NewGuid():N}.json");
                await File.WriteAllTextAsync(file,
                    JsonSerializer.Serialize(new { To = to, Subject = subject, Html = html }), ct);
                break;
            case "resend":
                using (var client = httpClientFactory.CreateClient())
                using (var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails"))
                {
                    request.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", _options.ResendApiKey);
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            from = $"{_options.FromName} <{_options.FromAddress}>",
                            to = new[] { to },
                            subject,
                            html
                        }), Encoding.UTF8, "application/json");
                    using var response = await client.SendAsync(request, ct);
                    response.EnsureSuccessStatusCode();
                }
                break;
            case "smtp":
                using (var message = new MailMessage())
                using (var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort))
                {
                    message.From = new MailAddress(_options.FromAddress, _options.FromName);
                    message.To.Add(to);
                    message.Subject = subject;
                    message.Body = html;
                    message.IsBodyHtml = true;
                    client.EnableSsl = true;
                    if (!string.IsNullOrWhiteSpace(_options.SmtpUsername))
                        client.Credentials = new NetworkCredential(
                            _options.SmtpUsername, _options.SmtpPassword);
                    await client.SendMailAsync(message, ct);
                }
                break;
        }
    }
}
