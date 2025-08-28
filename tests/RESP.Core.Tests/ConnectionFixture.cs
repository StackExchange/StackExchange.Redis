using System;
using System.Net;
using Resp;
using RESP.Core.Tests;
using Xunit;

[assembly: AssemblyFixture(typeof(ConnectionFixture))]

namespace RESP.Core.Tests;

public class ConnectionFixture : IDisposable
{
    private readonly RespConnectionPool _pool = new(new IPEndPoint(IPAddress.Loopback, 6379));

    public void Dispose() => _pool.Dispose();

    public IRespConnection GetConnection() => _pool.GetConnection();
}
