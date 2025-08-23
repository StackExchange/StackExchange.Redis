using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Resp;

public static class RespParsers
{
    public static IRespParser<Void, Void> Success => InbuiltInlineParsers.Default;
    public static IRespParser<Void, Void> OK => OKParser.Default;
    public static IRespParser<Void, string?> String => InbuiltParsers.Default;
    public static IRespParser<Void, int> Int32 => InbuiltParsers.Default;
    public static IRespParser<Void, int?> NullableInt32 => InbuiltParsers.Default;
    public static IRespParser<Void, long> Int64 => InbuiltParsers.Default;
    public static IRespParser<Void, long?> NullableInt64 => InbuiltParsers.Default;
    public static IRespParser<Void, float> Single => InbuiltParsers.Default;
    public static IRespParser<Void, float?> NullableSingle => InbuiltParsers.Default;
    public static IRespParser<Void, double> Double => InbuiltParsers.Default;
    public static IRespParser<Void, double?> NullableDouble => InbuiltParsers.Default;
    public static IRespParser<Void, byte[]?> ByteArray => InbuiltParsers.Default;
    public static IRespParser<Void, byte[]?[]?> ByteArrayArray => InbuiltParsers.Default;
    public static IRespParser<IBufferWriter<byte>, int> BufferWriter => InbuiltParsers.Default;

    /// <summary>
    /// For scalar values, returns the length in bytes. For aggregates, returns the count. Returns
    /// <c>-1</c> for <c>null</c> values.
    /// </summary>
    /// <remarks>This is mostly useful for debugging purposes; note that the value is still fetched
    /// over the network, so this is <i>not</i> an efficient way of measuring things - usually,
    /// a native command that only returns the length/count should be used instead.</remarks>
    public static IRespParser<Void, int> Length => LengthParser.Default;

    private sealed class Cache<TResponse>
    {
        public static IRespParser<Void, TResponse>? Instance =
            (InbuiltParsers.Default as IRespParser<Void, TResponse>) ?? (InbuiltInlineParsers.Default as IRespParser<Void, TResponse>);
    }

    public static IRespParser<Void, TResponse> Get<TResponse>()
        => Cache<TResponse>.Instance ??= GetCore<TResponse>();

    public static void Set<TResponse>(IRespParser<Void, TResponse> parser)
    {
        var obj = (InbuiltParsers.Default as IRespParser<Void, TResponse>) ?? (InbuiltInlineParsers.Default as IRespParser<Void, TResponse>);
        if (obj is not null) ThrowInbuiltParser(typeof(TResponse));
        Cache<TResponse>.Instance = parser;
    }

    private static IRespParser<Void, TResponse> GetCore<TResponse>()
    {
        var obj = (InbuiltParsers.Default as IRespParser<Void, TResponse>) ?? (InbuiltInlineParsers.Default as IRespParser<Void, TResponse>);
        if (obj is null)
        {
            ThrowNoParser(typeof(TResponse));
        }
        return Cache<TResponse>.Instance = obj;
    }

    [DoesNotReturn]
    private static void ThrowNoParser(Type type) => throw new InvalidOperationException(
        message: $"No default parser registered for type '{type.FullName}'; a custom parser must be specified via {nameof(RespParsers)}.{nameof(RespParsers.Set)}(...).");

    [DoesNotReturn]
    private static void ThrowInbuiltParser(Type type) => throw new InvalidOperationException(
        message: $"Type '{type.FullName}' has inbuilt handling and cannot be changed.");

    private sealed class InbuiltInlineParsers : IRespParser<Void, Void>, IRespInlineParser
    {
        private InbuiltInlineParsers() { }
        public static readonly InbuiltInlineParsers Default = new();

        public Void Parse(in Void state, ref RespReader reader) => Void.Instance;
    }

    private sealed class OKParser : IRespParser<Void, Void>, IRespInlineParser
    {
        private OKParser() { }
        public static readonly OKParser Default = new();

        public Void Parse(in Void state, ref RespReader reader)
        {
            if (!(reader.Prefix == RespPrefix.SimpleString && reader.IsOK()))
            {
                Throw();
            }

            return default;
            static void Throw() => throw new InvalidOperationException("Expected +OK response");
        }
    }

    private sealed class InbuiltParsers : IRespParser<Void, string?>,
        IRespParser<Void, byte[]?>, IRespParser<Void, byte[]?[]?>,
        IRespParser<Void, int>, IRespParser<Void, int?>,
        IRespParser<Void, long>, IRespParser<Void, long?>,
        IRespParser<Void, float>, IRespParser<Void, float?>,
        IRespParser<Void, double>, IRespParser<Void, double?>,
        IRespParser<IBufferWriter<byte>, int>
    {
        private InbuiltParsers() { }
        public static readonly InbuiltParsers Default = new();

        string? IRespParser<Void, string?>.Parse(in Void state, ref RespReader reader) => reader.ReadString();
        byte[]? IRespParser<Void, byte[]?>.Parse(in Void state, ref RespReader reader) => reader.ReadByteArray();
        byte[]?[]? IRespParser<Void, byte[]?[]?>.Parse(in Void state, ref RespReader reader) => reader.ReadArray(
            static (ref RespReader reader) => reader.ReadByteArray());
        int IRespParser<Void, int>.Parse(in Void state, ref RespReader reader) => reader.ReadInt32();
        int? IRespParser<Void, int?>.Parse(in Void state, ref RespReader reader) => reader.IsNull ? null : reader.ReadInt32();
        long IRespParser<Void, long>.Parse(in Void state, ref RespReader reader) => reader.ReadInt64();
        long? IRespParser<Void, long?>.Parse(in Void state, ref RespReader reader) => reader.IsNull ? null : reader.ReadInt64();
        float IRespParser<Void, float>.Parse(in Void state, ref RespReader reader) => (float)reader.ReadDouble();
        float? IRespParser<Void, float?>.Parse(in Void state, ref RespReader reader) => reader.IsNull ? null : (float)reader.ReadDouble();
        double IRespParser<Void, double>.Parse(in Void state, ref RespReader reader) => reader.ReadDouble();
        double? IRespParser<Void, double?>.Parse(in Void state, ref RespReader reader) => reader.IsNull ? null : reader.ReadDouble();

        int IRespParser<IBufferWriter<byte>, int>.Parse(in IBufferWriter<byte> state, ref RespReader reader)
        {
            reader.DemandScalar();
            if (reader.IsNull) return -1;
            return reader.CopyTo(state);
        }
    }
    private sealed class LengthParser : IRespParser<Void, int>, IRespInlineParser
    {
        private LengthParser() { }
        public static readonly LengthParser Default = new();

        public int Parse(in Void state, ref RespReader reader)
        {
            if (reader.IsNull) return -1;
            return reader.IsScalar ? reader.ScalarLength() : reader.AggregateLength();
        }
    }
}
