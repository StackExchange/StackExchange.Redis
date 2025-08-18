using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Resp;
using RESP.Core.Tests;
using Xunit;

[assembly: AssemblyFixture(typeof(ConnectionFixture))]

namespace RESP.Core.Tests;

public class ConnectionFixture : IDisposable
{
    private readonly ConnectionPool _pool = new(new IPEndPoint(IPAddress.Loopback, 6379));

    public void Dispose() => _pool.Dispose();

    public IRespConnection GetConnection() => _pool.GetConnection();
}
