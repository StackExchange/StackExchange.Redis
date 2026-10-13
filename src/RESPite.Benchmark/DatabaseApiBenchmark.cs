using System;
using System.Reflection;
using StackExchange.Redis;

namespace RESPite.Benchmark;

public sealed class DatabaseApiBenchmark(string[] args) : DatabaseApiBenchmarkBase(args)
{
    private static readonly string withVersion = $"IDatabase API, SE.Redis {GetLibVersion()}";
    public override string ToString() => withVersion;

    protected override IConnectionMultiplexer Create(int port)
    {
        var options = ConfigurationOptions.Parse($"{HostName}:{Port}");
        // No config channel: under RESP3 its subscription rides the interactive connection, and the server
        // then gives that connection the pub/sub output-buffer limit (32 MiB hard), which a deep pipeline of
        // large replies (--queue, LRANGE_*) exceeds - and the server closes it mid-run.
        options.ConfigurationChannel = "";
        if (WriteMode is { } wm && options.GetType().GetProperty(
            "WriteMode",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is { CanWrite: true, CanRead: true } prop)
        {
            prop.SetValue(options, wm);
            Console.WriteLine($"Set WriteMode to {prop.GetValue(options)}");
        }
        return ConnectionMultiplexer.Connect(options);
    }

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
