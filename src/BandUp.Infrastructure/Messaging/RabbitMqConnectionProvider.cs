using System.Net.Sockets;
using System.Threading.Channels;
using BandUp.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace BandUp.Infrastructure.Messaging;

public sealed class RabbitMqConnectionProvider(IOptions<RabbitMqSettings> settings, RabbitMqTopology topology) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile IConnection? _connection;

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true } current)
        {
            return current;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_connection is { IsOpen: true } ready)
            {
                return ready;
            }

            if (_connection is { } closed)
            {
                _connection = null;
                await closed.DisposeAsync();
            }

            RabbitMqSettings options = settings.Value;

            ConnectionFactory factory = new()
            {
                HostName = options.Host,
                Port = options.Port,
                UserName = options.User,
                Password = options.Password,
                VirtualHost = options.VirtualHost,
                // The provider and consumer own recovery. SDK recovery would race channel and consumer replacement.
                AutomaticRecoveryEnabled = false,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
                HandshakeContinuationTimeout = TimeSpan.FromSeconds(5),
                ClientProvidedName = "bandup",
            };

            var connection = await factory.CreateConnectionAsync(cancellationToken);
            try
            {
                // No caller can publish or consume on a replacement connection before its bindings exist.
                await topology.DeclareAsync(connection, cancellationToken);
                _connection = connection;
                return connection;
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public static bool IsTransientFailure(Exception exception) => exception switch
    {
        AuthenticationFailureException or ProtocolVersionMismatchException => false,
        OperationInterruptedException error => error.ShutdownReason?.ReplyCode is 0 or 200 or 320 or 504 or 541,
        // The client can surface a closed System.Threading.Channels transport during setup or teardown.
        ChannelClosedException closed => closed.InnerException is null || IsTransientFailure(closed.InnerException),
        BrokerUnreachableException or PossibleAuthenticationFailureException or ConnectFailureException =>
            exception.InnerException is { } cause && IsTransientFailure(cause),
        AggregateException aggregate => aggregate.InnerExceptions.Count > 0 && aggregate.InnerExceptions.All(IsTransientFailure),
        SocketException or IOException or TimeoutException or OperationCanceledException => true,
        _ => false,
    };

    public async ValueTask DisposeAsync()
    {
        if (_connection is { } connection)
        {
            await connection.DisposeAsync();
        }

        _gate.Dispose();
    }
}
