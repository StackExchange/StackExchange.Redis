using System;
using System.Diagnostics.CodeAnalysis;

namespace Resp;

public static class RespParsers
{
    public static IRespParser<Void> Success => InbuiltInlineParsers.Default;
    public static IRespParser<Void> OK => OKParser.Default;
    public static IRespParser<string?> String => InbuiltParsers.Default;
    public static IRespParser<int> Int32 => InbuiltParsers.Default;
    public static IRespParser<int?> NullableInt32 => InbuiltParsers.Default;
    public static IRespParser<long> Int64 => InbuiltParsers.Default;
    public static IRespParser<long?> NullableInt64 => InbuiltParsers.Default;
    public static IRespParser<float> Single => InbuiltParsers.Default;
    public static IRespParser<float?> NullableSingle => InbuiltParsers.Default;
    public static IRespParser<double> Double => InbuiltParsers.Default;
    public static IRespParser<double?> NullableDouble => InbuiltParsers.Default;

    private sealed class Cache<T>
    {
        public static IRespParser<T>? Instance =
            (InbuiltParsers.Default as IRespParser<T>) ?? (InbuiltInlineParsers.Default as IRespParser<T>);
    }

    public static IRespParser<T> Get<T>()
        => Cache<T>.Instance ??= GetCore<T>();

    public static void Set<T>(IRespParser<T> parser)
    {
        var obj = (InbuiltParsers.Default as IRespParser<T>) ?? (InbuiltInlineParsers.Default as IRespParser<T>);
        if (obj is not null) ThrowInbuiltParser(typeof(T));
        Cache<T>.Instance = parser;
    }

    private static IRespParser<T> GetCore<T>()
    {
        var obj = (InbuiltParsers.Default as IRespParser<T>) ?? (InbuiltInlineParsers.Default as IRespParser<T>);
        if (obj is null)
        {
            ThrowNoParser(typeof(T));
        }
        return Cache<T>.Instance = obj;
    }

    [DoesNotReturn]
    private static void ThrowNoParser(Type type) => throw new InvalidOperationException(
        message: $"No default parser registered for type '{type.FullName}'; a custom parser must be specified via {nameof(RespParsers)}.{nameof(RespParsers.Set)}(...).");

    [DoesNotReturn]
    private static void ThrowInbuiltParser(Type type) => throw new InvalidOperationException(
        message: $"Type '{type.FullName}' has inbuilt handling and cannot be changed.");

    private sealed class InbuiltInlineParsers : IRespParser<Void>, IRespInlineParser
    {
        private InbuiltInlineParsers() { }
        public static readonly InbuiltInlineParsers Default = new();

        public Void Parse(ref RespReader reader) => Void.Instance;
    }

    private sealed class OKParser : IRespParser<Void>, IRespInlineParser
    {
        private OKParser() { }
        public static readonly OKParser Default = new();

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

    private sealed class InbuiltParsers : IRespParser<string?>,
        IRespParser<int>, IRespParser<int?>,
        IRespParser<long>, IRespParser<long?>,
        IRespParser<float>, IRespParser<float?>,
        IRespParser<double>, IRespParser<double?>
    {
        private InbuiltParsers() { }
        public static readonly InbuiltParsers Default = new();

        string? IRespParser<string?>.Parse(ref RespReader reader) => reader.ReadString();
        int IRespParser<int>.Parse(ref RespReader reader) => reader.ReadInt32();
        int? IRespParser<int?>.Parse(ref RespReader reader) => reader.IsNull ? null : reader.ReadInt32();
        long IRespParser<long>.Parse(ref RespReader reader) => reader.ReadInt64();
        long? IRespParser<long?>.Parse(ref RespReader reader) => reader.IsNull ? null : reader.ReadInt64();
        float IRespParser<float>.Parse(ref RespReader reader) => (float)reader.ReadDouble();
        float? IRespParser<float?>.Parse(ref RespReader reader) => reader.IsNull ? null : (float)reader.ReadDouble();
        double IRespParser<double>.Parse(ref RespReader reader) => reader.ReadDouble();
        double? IRespParser<double?>.Parse(ref RespReader reader) => reader.IsNull ? null : reader.ReadDouble();
    }
}
