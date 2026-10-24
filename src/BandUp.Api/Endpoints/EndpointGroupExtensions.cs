using System.Reflection;
using BandUp.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace BandUp.Api.Endpoints;

public static class EndpointGroupExtensions
{
    /// <summary>
    /// Registers every <see cref="IEndpointGroup"/> in the API assembly so a group can take
    /// dependencies through its constructor.
    /// </summary>
    public static IServiceCollection AddEndpointGroups(this IServiceCollection services)
    {
        foreach (Type type in DiscoverGroups())
        {
            services.AddSingleton(typeof(IEndpointGroup), type);
        }

        return services;
    }

    /// <summary>
    /// Maps all registered groups under <c>/api</c>. Groups that need diagnostics are skipped
    /// unless the feature is on, and are never mapped in Production.
    /// </summary>
    public static IEndpointRouteBuilder MapEndpointGroups(this IEndpointRouteBuilder endpoints)
    {
        IOptions<FeaturesSettings> features = endpoints.ServiceProvider
            .GetRequiredService<IOptions<FeaturesSettings>>();

        IHostEnvironment environment = endpoints.ServiceProvider
            .GetRequiredService<IHostEnvironment>();

        bool diagnosticsEnabled = features.Value.Diagnostics && !environment.IsProduction();

        foreach (IEndpointGroup group in endpoints.ServiceProvider.GetServices<IEndpointGroup>())
        {
            if (group.RequiresDiagnostics && !diagnosticsEnabled)
            {
                continue;
            }

            RouteGroupBuilder api = endpoints
                .MapGroup($"/api/{group.Prefix}")
                .WithTags(group.Prefix);

            group.MapEndpoints(api);
        }

        return endpoints;
    }

    private static IEnumerable<Type> DiscoverGroups() =>
        typeof(IEndpointGroup).Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false })
            .Where(type => typeof(IEndpointGroup).IsAssignableFrom(type));
}