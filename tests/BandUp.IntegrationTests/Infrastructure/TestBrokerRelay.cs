using System.Net;
using System.Net.Sockets;

namespace BandUp.IntegrationTests.Infrastructure;

/// <summary>Lets the real Worker lose and regain access to the same test broker address.</summary>
internal sealed class TestBrokerRelay : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _brokerPort;
    private TcpListener? _listener;
    private CancellationTokenSource? _lifetime;
    private Task? _accepting;

    public TestBrokerRelay(string host, int brokerPort)
    {
        _host = host;
        _brokerPort = brokerPort;
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        Port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
    }

    public int Port { get; }

    public void Open()
    {
        var listener = new TcpListener(IPAddress.Loopback, Port);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Start();
        _listener = listener;
        _lifetime = new CancellationTokenSource();
        _accepting = AcceptAsync(listener, _lifetime.Token);
    }

    public async Task CloseAsync()
    {
        if (_lifetime is not { } lifetime)
        {
            return;
        }
        await lifetime.CancelAsync();
        _listener!.Stop();
        await _accepting!;
        lifetime.Dispose();
        _lifetime = null;
    }

    public async ValueTask DisposeAsync() => await CloseAsync();

    private async Task AcceptAsync(TcpListener listener, CancellationToken ct)
    {
        List<Task> streams = [];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                streams.Add(ForwardAsync(client, ct));
            }
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            await Task.WhenAll(streams);
        }
    }

    private async Task ForwardAsync(TcpClient client, CancellationToken ct)
    {
        using var local = client;
        using var remote = new TcpClient();
        try
        {
            await remote.ConnectAsync(_host, _brokerPort, ct);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var upstream = local.GetStream().CopyToAsync(remote.GetStream(), lifetime.Token);
            var downstream = remote.GetStream().CopyToAsync(local.GetStream(), lifetime.Token);
            await Task.WhenAny(upstream, downstream);
            await lifetime.CancelAsync();
            await Task.WhenAll(upstream, downstream);
        }
        catch (Exception error) when (error is IOException or SocketException or OperationCanceledException)
        {
        }
    }
}
