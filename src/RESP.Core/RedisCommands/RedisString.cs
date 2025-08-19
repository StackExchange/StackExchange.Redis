using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Resp.RedisCommands;

public readonly struct RedisString
{
    private readonly IRespConnection _connection;
    private readonly string _key;
    private readonly TimeSpan _timeout;
    private readonly CancellationToken _cancellationToken;

    public RedisString(IRespConnection connection, string key)
    {
        _connection = connection;
        _key = key;
        _timeout = TimeSpan.Zero;
        _cancellationToken = CancellationToken.None;
    }

    public RedisString(IRespConnection connection, string key, TimeSpan timeout)
    {
        _connection = connection;
        _key = key;
        _timeout = timeout;
        _cancellationToken = CancellationToken.None;
    }

    public RedisString(IRespConnection connection, string key, CancellationToken cancellationToken)
    {
        _connection = connection;
        _key = key;
        _cancellationToken = cancellationToken;
        _timeout = TimeSpan.Zero;
    }

    public string? Get() => _connection.Send("get"u8, _key).ParseAndDispose<string?>(timeout: _timeout);

    public void Set(string value) => _connection.Send("set"u8, (key: _key, value)).ParseAndDispose<Void>(timeout: _timeout);

    public Task<string?> GetAsync()
        => _connection.SendAsync("get"u8, _key).ParseAndDisposeAsync<string?>(cancellationToken: _cancellationToken);
}

internal static class AsyncRespExtensions
{
    public static Task<T> ParseAndDisposeAsync<T>(
        this ValueTask<RespPayload> pending,
        IRespParser<T>? parser = null,
        CancellationToken cancellationToken = default)
    {
        if (pending.IsCompletedSuccessfully)
        {
            return Task.FromResult(pending.GetAwaiter().GetResult().ParseAndDispose(parser));
        }
        return cancellationToken.CanBeCanceled ? AwaitedCancel(pending, parser, cancellationToken) : AwaitedNoCancel(pending, parser);

        static async Task<T> AwaitedNoCancel(ValueTask<RespPayload> pending, IRespParser<T>? parser)
            => (await pending.ConfigureAwait(false)).ParseAndDispose(parser);

        static Task<T> AwaitedCancel(ValueTask<RespPayload> pending, IRespParser<T>? parser, CancellationToken cancellationToken) =>
            pending.AsTask().ContinueWith<T>(
                static (task, state) => task.Result.ParseAndDispose((IRespParser<T>?)state), parser, cancellationToken);
    }
}

internal struct Void
{
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
    public static readonly Void Instance;
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value
}

internal static class DefaultFormatters
{
    static DefaultFormatters()
    {
        FormatterCache<Void>.Instance = VoidFormatter.Instance;
        FormatterCache<string>.Instance = StringFormatter.Instance;
        FormatterCache<(string, string)>.Instance = StringStringFormatter.Instance;
    }

    private static class FormatterCache<T>
    {
        public static IRespFormatter<T>? Instance;
    }

    public static IRespFormatter<T> Get<T>()
    {
        var formatter = FormatterCache<T>.Instance;
        if (formatter is null) ThrowFormatter(nameof(formatter), typeof(T));
        return formatter;
    }

    [DoesNotReturn]
    private static void ThrowFormatter(string paramName, Type type) => throw new ArgumentNullException(
        paramName: paramName,
        message: $"No default formatter registered for type '{type.FullName}'; a custom formatter must be specified");
}
internal static class DefaultParsers
{
    static DefaultParsers()
    {
        ParserCache<Void>.Instance = VoidParser.Instance;
        ParserCache<string?>.Instance = StringParser.Instance;
    }

    private static class ParserCache<T>
    {
        public static IRespParser<T>? Instance;
    }

    public static IRespParser<T> Get<T>()
    {
        var parser = ParserCache<T>.Instance;
        if (parser is null) ThrowParser(nameof(parser), typeof(T));
        return parser;
    }

    [DoesNotReturn]
    private static void ThrowParser(string paramName, Type type) => throw new ArgumentNullException(
        paramName: paramName,
        message: $"No default parser registered for type '{type.FullName}'; a custom parser must be specified");
}

internal sealed class VoidFormatter : IRespFormatter<Void>
{
    public static readonly VoidFormatter Instance = new();
    public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in Void value)
    {
        writer.WriteCommand(command, 0);
    }
}
internal sealed class VoidParser : IRespParser<Void>
{
    public static readonly VoidParser Instance = new();
    public Void Parse(ref RespReader reader) => default;
}

internal sealed class StringFormatter : IRespFormatter<string>
{
    public static readonly StringFormatter Instance = new();

    public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in string value)
    {
        writer.WriteCommand(command, 1);
        writer.WriteBulkString(value);
    }
}

internal sealed class StringStringFormatter : IRespFormatter<(string Arg0, string Arg1)>
{
    public static readonly StringStringFormatter Instance = new();

    public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in (string Arg0, string Arg1) value)
    {
        writer.WriteCommand(command, 2);
        writer.WriteBulkString(value.Arg0);
        writer.WriteBulkString(value.Arg1);
    }
}

internal sealed class StringParser : IRespParser<string?>
{
    public static readonly StringParser Instance = new();
    public string? Parse(ref RespReader reader) => reader.ReadString();
}
