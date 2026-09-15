using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis.Configuration;

namespace StackExchange.Redis.Tests;

/// <summary>
/// Wraps another <see cref="Tunnel"/> and sends chosen endpoints to a real socket instead, so they are
/// refused.
/// </summary>
/// <remarks>
/// The fixture for "a node that has gone away", which is harder to model than it looks. Removing a node from
/// the in-process fake is not enough: <c>InProcTunnel</c> only intercepts endpoints <c>TryGetNode</c> still
/// resolves, but an already-established in-process pipe survives the removal, so the client never reconnects
/// and therefore never fails. Falling through to a real socket against a loopback port that was bound and
/// released gives connection-refused on every attempt, which is what a departed node actually does.
/// <para>
/// Endpoints can be black-holed after connecting, which is what lets a test establish a connection, get the
/// client into the state it wants, and only then take the node away.
/// </para>
/// </remarks>
internal sealed class BlackHoleTunnel(Tunnel inner) : Tunnel
{
    private readonly HashSet<EndPoint> _blackHoled = [];

    /// <summary>A loopback port that has been bound and released, so connecting to it is refused rather than dropped.</summary>
    /// <remarks>
    /// Refused rather than timing out is the point: a dropped SYN would exercise connect *timeouts*, which is
    /// a different failure mode with very different timing, and would make any test built on it slow.
    /// </remarks>
    public static IPEndPoint GetRefusingEndPoint()
    {
        using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return (IPEndPoint)probe.LocalEndPoint!;
    }

    /// <summary>Stops intercepting this endpoint, so connecting to it is refused from now on.</summary>
    public void BlackHole(EndPoint endpoint)
    {
        lock (_blackHoled) _blackHoled.Add(endpoint);
    }

    private bool IsBlackHoled(EndPoint endpoint)
    {
        lock (_blackHoled) return _blackHoled.Contains(endpoint);
    }

    public override ValueTask<EndPoint?> GetSocketConnectEndpointAsync(EndPoint endpoint, CancellationToken cancellationToken)
        => IsBlackHoled(endpoint)
            ? base.GetSocketConnectEndpointAsync(endpoint, cancellationToken)
            : inner.GetSocketConnectEndpointAsync(endpoint, cancellationToken);

    public override ValueTask<Stream?> BeforeAuthenticateAsync(EndPoint endpoint, ConnectionType connectionType, Socket? socket, CancellationToken cancellationToken)
        => IsBlackHoled(endpoint)
            ? base.BeforeAuthenticateAsync(endpoint, connectionType, socket, cancellationToken)
            : inner.BeforeAuthenticateAsync(endpoint, connectionType, socket, cancellationToken);
}
