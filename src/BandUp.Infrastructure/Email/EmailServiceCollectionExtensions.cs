using System.Net.Mail;
using BandUp.Application.Abstractions;
using BandUp.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BandUp.Infrastructure.Email;

internal static class EmailServiceCollectionExtensions
{
    internal static IServiceCollection AddEmailDelivery(this IServiceCollection services)
    {
        services.AddOptions<EmailSettings>().BindConfiguration(EmailSettings.SectionName)
            .Validate(settings => ServiceProviders.Email.Contains(settings.Provider, StringComparer.OrdinalIgnoreCase),
                "Email:Provider must be InMemory, Mailpit or Resend.")
            .Validate(settings => settings.QueueCapacity is >= 1 and <= 10_000, "Email:QueueCapacity must be between 1 and 10000.")
            .Validate(settings => settings.TimeoutSeconds is >= 1 and <= 30, "Email:TimeoutSeconds must be between 1 and 30.")
            .Validate(settings => MailAddress.TryCreate(settings.FromEmail, out _), "Email:FromEmail must be a valid email address.")
            .Validate(settings => settings.FromName is not null && !settings.FromName.Any(char.IsControl),
                "Email:FromName must not contain control characters.")
            .Validate(settings => !string.Equals(settings.Provider, ServiceProviders.Resend, StringComparison.OrdinalIgnoreCase) ||
                EmailSettings.IsResendKey(settings.ResendApiKey), "Email:ResendApiKey must be a Resend key starting with re_ and contain only letters, digits, underscores or hyphens.")
            .Validate(settings => !string.Equals(settings.Provider, ServiceProviders.Resend, StringComparison.OrdinalIgnoreCase) ||
                EmailSettings.IsResendSender(settings.FromEmail), "Email:FromEmail must be a plain sender address on your Resend domain, or onboarding@resend.dev for account-only testing; .local addresses cannot send through Resend.")
            .Validate(settings => !string.Equals(settings.Provider, ServiceProviders.Mailpit, StringComparison.OrdinalIgnoreCase) ||
                EmailSettings.IsLocalEndpoint(settings.MailpitBaseUrl), "Email:MailpitBaseUrl must be a loopback HTTP(S) URL without credentials, query or fragment.")
            .ValidateOnStart();
        services.AddHttpClient(MailpitEmailSender.ClientName, (provider, client) =>
        {
            EmailSettings settings = provider.GetRequiredService<IOptions<EmailSettings>>().Value;
            client.BaseAddress = new Uri(settings.MailpitBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient(ResendEmailSender.ClientName, (provider, client) =>
        {
            client.BaseAddress = new Uri("https://api.resend.com/");
            client.Timeout = TimeSpan.FromSeconds(provider.GetRequiredService<IOptions<EmailSettings>>().Value.TimeoutSeconds);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .RedactLoggedHeaders(["Authorization"]);
        services.AddSingleton<InMemoryEmailSender>();
        services.AddSingleton<MailpitEmailSender>();
        services.AddSingleton<ResendEmailSender>();
        services.AddSingleton<IEmailSender>(provider =>
        {
            string selected = provider.GetRequiredService<IOptions<EmailSettings>>().Value.Provider;
            if (string.Equals(selected, ServiceProviders.Resend, StringComparison.OrdinalIgnoreCase))
            {
                return provider.GetRequiredService<ResendEmailSender>();
            }
            return string.Equals(selected, ServiceProviders.Mailpit, StringComparison.OrdinalIgnoreCase)
                ? provider.GetRequiredService<MailpitEmailSender>() : provider.GetRequiredService<InMemoryEmailSender>();
        });
        services.AddSingleton<EmailDeliveryQueue>();
        services.AddScoped<IPostCommitEmailQueue, PostCommitEmailQueue>();
        services.AddHostedService<EmailDeliveryWorker>();
        return services;
    }
}
