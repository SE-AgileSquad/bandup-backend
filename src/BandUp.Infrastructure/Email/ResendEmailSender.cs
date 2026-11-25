using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Serialization;
using BandUp.Application.Abstractions;
using BandUp.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace BandUp.Infrastructure.Email;

public sealed class ResendEmailSender(IHttpClientFactory clients, IOptions<EmailSettings> options) : IEmailSender
{
    public const string ClientName = "BandUp.Resend";

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        EmailSettings settings = options.Value;
        using HttpClient client = clients.CreateClient(ClientName);
        using HttpRequestMessage request = new(HttpMethod.Post, "emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ResendApiKey);
        request.Content = JsonContent.Create(new
        {
            from = new MailAddress(settings.FromEmail, settings.FromName).ToString(),
            to = new[] { message.To },
            subject = message.Subject,
            text = message.TextBody,
        });

        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Provider bodies can echo addresses/content. Never read or include them in errors.
                throw new HttpRequestException("Resend rejected the email submission.", null, response.StatusCode);
            }

            Receipt? receipt = await response.Content.ReadFromJsonAsync<Receipt>(cancellationToken);
            if (string.IsNullOrWhiteSpace(receipt?.Id))
            {
                throw new HttpRequestException("Resend returned an invalid submission response.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Resend email submission timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new HttpRequestException("Resend email submission failed.", null, exception.StatusCode);
        }
        catch (JsonException)
        {
            throw new HttpRequestException("Resend returned an invalid submission response.");
        }
    }

    private sealed record Receipt([property: JsonPropertyName("id")] string? Id);
}
