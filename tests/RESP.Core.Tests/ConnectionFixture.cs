using System;
using System.Net;
using RESP.Core.Tests;
using RESPite;
using RESPite.Connections;
using Xunit;

[assembly: AssemblyFixture(typeof(ConnectionFixture))]

namespace RESP.Core.Tests;

public class ConnectionFixture : IDisposable
{
    private readonly RespConnectionPool _pool = new(new IPEndPoint(IPAddress.Loopback, 6379));

    public void Dispose() => _pool.Dispose();

    public RespConnection GetConnection()
    {
        var template = _pool.Template.WithCancellationToken(TestContext.Current.CancellationToken);
        return _pool.GetConnection(template);
    }
}
