using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Resp;

public readonly struct ResponseSummary(RespPrefix prefix, int length, long protocolBytes) : IEquatable<ResponseSummary>
{
    public RespPrefix Prefix { get; } = prefix;
    public int Length { get; } = length;
    public long ProtocolBytes { get; } = protocolBytes;

    /// <inheritdoc />
    public override string ToString() => $"{Prefix}, Length: {Length}, Protocol Bytes: {ProtocolBytes}";

    /// <inheritdoc cref="IEquatable{T}.Equals(T)" />
    public bool Equals(ResponseSummary other) => EqualsCore(in other);

    private bool EqualsCore(in ResponseSummary other) =>
        Prefix == other.Prefix && Length == other.Length && ProtocolBytes == other.ProtocolBytes;

    bool IEquatable<ResponseSummary>.Equals(ResponseSummary other) => EqualsCore(in other);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ResponseSummary summary && EqualsCore(in summary);

    /// <inheritdoc />
    public override int GetHashCode() => (int)Prefix ^ Length ^ ProtocolBytes.GetHashCode();

    public static IRespParser<Void, ResponseSummary> Parser => ResponseSummaryParser.Default;

    private sealed class ResponseSummaryParser : IRespParser<Void, ResponseSummary>, IRespInlineParser, IRespMetadataParser
    {
        private ResponseSummaryParser() { }
        public static readonly ResponseSummaryParser Default = new();

        public ResponseSummary Parse(in Void state, ref RespReader reader)
        {
            var protocolBytes = reader.ProtocolBytes;
            int length = 0;
            if (reader.TryMoveNext())
            {
                if (reader.IsScalar) length = reader.ScalarLength();
                else if (reader.IsAggregate) length = reader.AggregateLength();
            }
            return new ResponseSummary(reader.Prefix, length, protocolBytes);
        }
    }
}

public static class RespParsers
{
    public static IRespParser<Void, Void> Success => InbuiltInlineParsers.Default;
    public static IRespParser<Void, Void> OK => OKParser.Default;
    public static IRespParser<Void, string?> String => InbuiltCopyOutParsers.Default;
    public static IRespParser<Void, int> Int32 => InbuiltInlineParsers.Default;
    public static IRespParser<Void, int?> NullableInt32 => InbuiltInlineParsers.Default;
    public static IRespParser<Void, long> Int64 => InbuiltInlineParsers.Default;
    public static IRespParser<Void, long?> NullableInt64 => InbuiltInlineParsers.Default;
    public static IRespParser<Void, float> Single => InbuiltInlineParsers.Default;
    public static IRespParser<Void, float?> NullableSingle => InbuiltInlineParsers.Default;
    public static IRespParser<Void, double> Double => InbuiltInlineParsers.Default;
    public static IRespParser<Void, double?> NullableDouble => InbuiltInlineParsers.Default;
    public static IRespParser<Void, byte[]?> ByteArray => InbuiltCopyOutParsers.Default;
    public static IRespParser<Void, byte[]?[]?> ByteArrayArray => InbuiltCopyOutParsers.Default;
    public static IRespParser<IBufferWriter<byte>, int> BufferWriter => InbuiltCopyOutParsers.Default;

    private sealed class Cache<TResponse>
    {
        public static IRespParser<Void, TResponse>? Instance =
            (InbuiltCopyOutParsers.Default as IRespParser<Void, TResponse>) ??
            (InbuiltInlineParsers.Default as IRespParser<Void, TResponse>);
    }

    public static IRespParser<Void, TResponse> Get<TResponse>()
        => Cache<TResponse>.Instance ??= GetCore<TResponse>();

    public static void Set<TResponse>(IRespParser<Void, TResponse> parser)
    {
        var obj = (InbuiltCopyOutParsers.Default as IRespParser<Void, TResponse>) ??
                  (InbuiltInlineParsers.Default as IRespParser<Void, TResponse>);
        if (obj is not null) ThrowInbuiltParser(typeof(TResponse));
        Cache<TResponse>.Instance = parser;
    }

    private static IRespParser<Void, TResponse> GetCore<TResponse>()
    {
        var obj = (InbuiltCopyOutParsers.Default as IRespParser<Void, TResponse>) ??
                  (InbuiltInlineParsers.Default as IRespParser<Void, TResponse>);
        if (obj is null)
        {
            ThrowNoParser(typeof(TResponse));
        }

        return Cache<TResponse>.Instance = obj;
    }

    [DoesNotReturn]
    private static void ThrowNoParser(Type type) => throw new InvalidOperationException(
        message:
        $"No default parser registered for type '{type.FullName}'; a custom parser must be specified via {nameof(RespParsers)}.{nameof(RespParsers.Set)}(...).");

    [DoesNotReturn]
    private static void ThrowInbuiltParser(Type type) => throw new InvalidOperationException(
        message: $"Type '{type.FullName}' has inbuilt handling and cannot be changed.");

    private sealed class InbuiltInlineParsers : IRespParser<Void, Void>, IRespInlineParser,
        IRespParser<Void, int>, IRespParser<Void, int?>,
        IRespParser<Void, long>, IRespParser<Void, long?>,
        IRespParser<Void, float>, IRespParser<Void, float?>,
        IRespParser<Void, double>, IRespParser<Void, double?>
    {
        private InbuiltInlineParsers() { }
        public static readonly InbuiltInlineParsers Default = new();

        public Void Parse(in Void state, ref RespReader reader) => Void.Instance;

        int IRespParser<Void, int>.Parse(in Void state, ref RespReader reader) => reader.ReadInt32();

        int? IRespParser<Void, int?>.Parse(in Void state, ref RespReader reader) =>
            reader.IsNull ? null : reader.ReadInt32();

        long IRespParser<Void, long>.Parse(in Void state, ref RespReader reader) => reader.ReadInt64();

        long? IRespParser<Void, long?>.Parse(in Void state, ref RespReader reader) =>
            reader.IsNull ? null : reader.ReadInt64();

        float IRespParser<Void, float>.Parse(in Void state, ref RespReader reader) => (float)reader.ReadDouble();

        float? IRespParser<Void, float?>.Parse(in Void state, ref RespReader reader) =>
            reader.IsNull ? null : (float)reader.ReadDouble();

        double IRespParser<Void, double>.Parse(in Void state, ref RespReader reader) => reader.ReadDouble();

        double? IRespParser<Void, double?>.Parse(in Void state, ref RespReader reader) =>
            reader.IsNull ? null : reader.ReadDouble();
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

    private sealed class InbuiltCopyOutParsers : IRespParser<Void, string?>,
        IRespParser<Void, byte[]?>, IRespParser<Void, byte[]?[]?>,
        IRespParser<IBufferWriter<byte>, int>
    {
        private InbuiltCopyOutParsers() { }
        public static readonly InbuiltCopyOutParsers Default = new();

        string? IRespParser<Void, string?>.Parse(in Void state, ref RespReader reader) => reader.ReadString();
        byte[]? IRespParser<Void, byte[]?>.Parse(in Void state, ref RespReader reader) => reader.ReadByteArray();

        byte[]?[]? IRespParser<Void, byte[]?[]?>.Parse(in Void state, ref RespReader reader) =>
            reader.ReadArray(static (ref RespReader reader) => reader.ReadByteArray());

        int IRespParser<IBufferWriter<byte>, int>.Parse(in IBufferWriter<byte> state, ref RespReader reader)
        {
            reader.DemandScalar();
            if (reader.IsNull) return -1;
            return reader.CopyTo(state);
        }
    }
}
