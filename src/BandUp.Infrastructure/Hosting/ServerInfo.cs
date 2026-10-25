using System.Reflection;
using BandUp.Application.Abstractions;
using Microsoft.Extensions.Hosting;

namespace BandUp.Infrastructure.Hosting;

public sealed class ServerInfo(IHostEnvironment environment) : IServerInfo
{
    public string Environment => environment.EnvironmentName;

    public string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        Assembly? entry = Assembly.GetEntryAssembly();

        return entry?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? entry?.GetName().Version?.ToString()
            ?? "unknown";
    }
}
