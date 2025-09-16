using System;
using System.Net;
using RESPite.Connections;
using Xunit;

[assembly: AssemblyFixture(typeof(RESPite.Tests.ConnectionFixture))]

namespace RESPite.Tests;

public class ConnectionFixture : IDisposable
{
    private readonly RespConnectionPool _pool = new();

    public void Dispose() => _pool.Dispose();

    public RespConnection GetConnection()
    {
        var template = _pool.Template.WithCancellationToken(TestContext.Current.CancellationToken);
        return _pool.GetConnection(template);
    }
}
