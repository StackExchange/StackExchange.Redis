using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Resp.RedisCommands;

public readonly struct RedisStrings
{
    private readonly IRespConnection _connection;
    private readonly TimeSpan _timeout;
    private readonly CancellationToken _cancellationToken;

    public RedisStrings(IRespConnection connection)
    {
        _connection = connection;
        _timeout = TimeSpan.Zero;
        _cancellationToken = CancellationToken.None;
    }

    public RedisStrings(IRespConnection connection, TimeSpan timeout)
    {
        _connection = connection;
        _timeout = timeout;
        _cancellationToken = CancellationToken.None;
    }

    public RedisStrings(IRespConnection connection, CancellationToken cancellationToken)
    {
        _connection = connection;
        _cancellationToken = cancellationToken;
        _timeout = TimeSpan.Zero;
    }

    public string? Get(string key) => RespMessage.Create("get"u8, key).Wait<string?>(_connection, timeout: _timeout);
    public Task<string?> GetAsync(string key) => RespMessage.Create("get"u8, key).WaitAsync<string?>(_connection, cancellationToken: _cancellationToken);

    public void Set(string key, string value) => RespMessage.Create("set"u8, (key: key, value)).Wait(_connection, timeout: _timeout);
    public Task SetAsync(string key, string value) => RespMessage.Create("set"u8, (key: key, value)).WaitAsync(_connection, cancellationToken: _cancellationToken);
}

public readonly struct Void
{
}

internal static class DefaultFormatters
{
    static DefaultFormatters()
    {
        FormatterCache<Void>.Instance = VoidFormatter.Default;
        FormatterCache<string>.Instance = StringFormatter.Instance;
        FormatterCache<(string, string)>.Instance = StringStringFormatter.Instance;
    }

    private static class FormatterCache<T>
#if NET9_0_OR_GREATER
        where T : allows ref struct
#endif
    {
        public static IRespFormatter<T>? Instance;
    }

    public static IRespFormatter<T> Get<T>()
#if NET9_0_OR_GREATER
        where T : allows ref struct
#endif
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
        ParserCache<Void>.Instance = VoidParser.Default;
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
    public static readonly VoidFormatter Default = new();
    public void Format(scoped ReadOnlySpan<byte> command, ref RespWriter writer, in Void value)
    {
        writer.WriteCommand(command, 0);
    }
}
internal sealed class VoidParser : IRespParser<Void>, IRespInlineParser
{
    public static readonly VoidParser Default = new();

    public Void Parse(ref RespReader reader)
    {
        if (!(reader.Prefix == RespPrefix.SimpleString && reader.IsOK()))
        {
            Throw();
        }
        return default;
        static void Throw() => throw new InvalidOperationException("Expected +OK response");
    }
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
