using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Forwards a loopback port to another one, so a test can make a server reachable (or not) at an address of
/// its choosing without touching the shared topology.
/// </summary>
/// <remarks>
/// The alternative - stopping a real server - is not available to a suite that runs against a shared set of
/// them, and "the address was dead and then it was not" is the only way to exercise a connect that has to
/// survive its dependencies not being up yet.
/// </remarks>
public sealed class TcpForwarder : IDisposable
{
    private readonly TcpListener _listener;
    private readonly int _targetPort;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>A port with nothing listening on it, for a test that needs a dead address first.</summary>
    public static int GetFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public TcpForwarder(int listenPort, int targetPort)
    {
        _targetPort = targetPort;
        _listener = new TcpListener(IPAddress.Loopback, listenPort);
        _listener.Start();
        _ = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync().ForAwait();
            }
            catch
            {
                return; // listener stopped
            }
            _ = Task.Run(() => PumpAsync(client));
        }
    }

    private async Task PumpAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var upstream = new TcpClient();
                await upstream.ConnectAsync(IPAddress.Loopback, _targetPort).ForAwait();
                var near = client.GetStream();
                var far = upstream.GetStream();
                // the buffer size is spelled out because the two-argument (stream, token) overload does not
                // exist on .NET Framework, which the test project still targets
                const int BufferSize = 4096;
                await Task.WhenAny(
                    near.CopyToAsync(far, BufferSize, _cts.Token),
                    far.CopyToAsync(near, BufferSize, _cts.Token)).ForAwait();
            }
            catch
            {
                // a forwarded connection going away is normal; the test asserts on the client's behaviour
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        _cts.Dispose();
    }
}
