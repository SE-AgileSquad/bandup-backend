using System.Net.Http.Json;
using System.Text.Json;
using BandUp.Application.Abstractions;
using BandUp.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace BandUp.Infrastructure.Email;

public sealed class MailpitEmailSender(IHttpClientFactory clients, IOptions<EmailSettings> options) : IEmailSender
{
    public const string ClientName = "BandUp.Mailpit";

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        EmailSettings settings = options.Value;
        using HttpClient client = clients.CreateClient(ClientName);
        using HttpResponseMessage response = await client.PostAsJsonAsync("api/v1/send", new
        {
            From = new { Email = settings.FromEmail, Name = settings.FromName },
            To = new[] { new { Email = message.To } },
            Subject = message.Subject,
            Text = message.TextBody,
        }, JsonSerializerOptions.Default, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
