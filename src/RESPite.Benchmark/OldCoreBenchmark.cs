using System;
using System.Reflection;
using StackExchange.Redis;

namespace RESPite.Benchmark;

public sealed class OldCoreBenchmark(string[] args) : OldCoreBenchmarkBase(args)
{
    private static readonly string withVersion = $"legacy SE.Redis {GetLibVersion()}";
    public override string ToString() => withVersion;
    protected override IConnectionMultiplexer Create(int port) => ConnectionMultiplexer.Connect($"127.0.0.1:{Port}");

    private static string? _libVersion;
    internal static string GetLibVersion()
    {
        if (_libVersion == null)
        {
            var assembly = typeof(ConnectionMultiplexer).Assembly;
            _libVersion = ((AssemblyFileVersionAttribute)Attribute.GetCustomAttribute(assembly, typeof(AssemblyFileVersionAttribute))!)?.Version
                          ?? assembly.GetName().Version!.ToString();
        }
        return _libVersion;
    }
}
