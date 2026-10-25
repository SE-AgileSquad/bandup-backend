namespace BandUp.Application.Abstractions;

/// <summary>
/// The process's environment name and version. Application cannot reference ASP.NET Core, so
/// the host details reach a handler through this instead of <c>IHostEnvironment</c>.
/// </summary>
public interface IServerInfo
{
    string Environment { get; }

    string Version { get; }
}
