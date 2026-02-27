using System;
using StackExchange.Redis.Configuration;
using Xunit;

[assembly: AssemblyFixture(typeof(StackExchange.Redis.Tests.InProcServerFixture))]

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis.Tests;

public class InProcServerFixture : IDisposable
{
    private readonly InProcessTestServer _server = new();
    private readonly ConfigurationOptions _config;
    public InProcServerFixture()
    {
        _config = _server.GetClientConfig();
        Configuration = _config.ToString();
    }

    public ConfigurationOptions Config => _config;

    public string Configuration { get; }

    public Tunnel? Tunnel => _server.Tunnel;

    public void Dispose() => _server.Dispose();
}
