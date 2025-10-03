using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using RESPite.Internal;
using RESPite.Messages;

namespace RESPite;

public static class RespParsers
{
    public static IRespParser<bool> Success => InbuiltInlineParsers.Default;
    public static IRespParser<bool> OK => OKParser.Default;
    public static IRespParser<string?> String => InbuiltCopyOutParsers.Default;
    public static IRespParser<int> Int32 => InbuiltInlineParsers.Default;
    public static IRespParser<int?> NullableInt32 => InbuiltInlineParsers.Default;
    public static IRespParser<long> Int64 => InbuiltInlineParsers.Default;
    public static IRespParser<long?> NullableInt64 => InbuiltInlineParsers.Default;
    public static IRespParser<float> Single => InbuiltInlineParsers.Default;
    public static IRespParser<float?> NullableSingle => InbuiltInlineParsers.Default;
    public static IRespParser<double> Double => InbuiltInlineParsers.Default;
    public static IRespParser<double?> NullableDouble => InbuiltInlineParsers.Default;
    public static IRespParser<byte[]?> ByteArray => InbuiltCopyOutParsers.Default;
    public static IRespParser<byte[]?[]?> ByteArrayArray => InbuiltCopyOutParsers.Default;
    public static IRespParser<IBufferWriter<byte>, int> BufferWriter => InbuiltCopyOutParsers.Default;

    private static class StatelessCache<TResponse>
    {
        public static IRespParser<TResponse>? Instance =
            (InbuiltCopyOutParsers.Default as IRespParser<TResponse>) ?? // regular (may allocate, etc)
            (InbuiltInlineParsers.Default as IRespParser<TResponse>) ?? // inline
            (ResponseSummary.Parser as IRespParser<TResponse>); // inline+metadata
    }

    private static class StatefulCache<TState, TResponse>
    {
        public static IRespParser<TState, TResponse>? Instance =
            InbuiltCopyOutParsers.Default as IRespParser<TState, TResponse>; // ?? // regular (may allocate, etc)
        // (InbuiltInlineParsers.Default as IRespParser<TState, TResponse>) ?? // inline
        // (ResponseSummary.Parser as IRespParser<TState, TResponse>); // inline+metadata
    }

    private static bool IsInbuilt(object? obj) => obj is InbuiltCopyOutParsers or InbuiltInlineParsers
        or ResponseSummary.ResponseSummaryParser;

    public static IRespParser<TResponse> Get<TResponse>()
    {
        var obj = StatelessCache<TResponse>.Instance;
        if (obj is null) ThrowNoParser();
        return obj;
    }

    public static IRespParser<TState, TResponse> Get<TState, TResponse>()
    {
        var obj = StatefulCache<TState, TResponse>.Instance;
        if (obj is null) ThrowNoParser();
        return obj;
    }

    public static void Set<TResponse>(IRespParser<TResponse> parser)
    {
        if (IsInbuilt(StatelessCache<TResponse>.Instance)) ThrowInbuiltParser();
        StatelessCache<TResponse>.Instance = parser;
    }

    public static void Set<TState, TResponse>(IRespParser<TState, TResponse> parser)
    {
        if (IsInbuilt(StatefulCache<TState, TResponse>.Instance)) ThrowInbuiltParser();
        StatefulCache<TState, TResponse>.Instance = parser;
    }

    [DoesNotReturn]
    private static void ThrowNoParser() => throw new InvalidOperationException(
        message:
        $"No default parser registered for this type; a custom parser must be specified via {nameof(RespParsers)}.{nameof(Set)}(...).");

    [DoesNotReturn]
    private static void ThrowInbuiltParser() => throw new InvalidOperationException(
        message: $"This type has inbuilt handling and cannot be changed.");

    private sealed class InbuiltInlineParsers : IRespInlineParser,
        IRespParser<bool>,
        IRespParser<int>, IRespParser<int?>,
        IRespParser<long>, IRespParser<long?>,
        IRespParser<float>, IRespParser<float?>,
        IRespParser<double>, IRespParser<double?>
    {
        private InbuiltInlineParsers() { }
        public static readonly InbuiltInlineParsers Default = new();

        bool IRespParser<bool>.Parse(ref RespReader reader) => reader.ReadBoolean();
        int IRespParser<int>.Parse(ref RespReader reader) => reader.ReadInt32();

        int? IRespParser<int?>.Parse(ref RespReader reader) => reader.IsNull ? null : reader.ReadInt32();

        long IRespParser<long>.Parse(ref RespReader reader) => reader.ReadInt64();

        long? IRespParser<long?>.Parse(ref RespReader reader) => reader.IsNull ? null : reader.ReadInt64();

        float IRespParser<float>.Parse(ref RespReader reader) => (float)reader.ReadDouble();

        float? IRespParser<float?>.Parse(ref RespReader reader) => reader.IsNull ? null : (float)reader.ReadDouble();

        double IRespParser<double>.Parse(ref RespReader reader) => reader.ReadDouble();

        double? IRespParser<double?>.Parse(ref RespReader reader) => reader.IsNull ? null : reader.ReadDouble();
    }

    private sealed class OKParser : IRespParser<bool>, IRespInlineParser
    {
        private OKParser() { }
        public static readonly OKParser Default = new();

        public bool Parse(ref RespReader reader)
        {
            if (!(reader.Prefix == RespPrefix.SimpleString && reader.IsOK()))
            {
                Throw();
            }

            return true;
            static void Throw() => throw new InvalidOperationException("Expected +OK response or similar.");
        }
    }

    private sealed class InbuiltCopyOutParsers : IRespParser<string?>,
        IRespParser<byte[]?>, IRespParser<byte[]?[]?>,
        IRespParser<IBufferWriter<byte>, int>
    {
        private InbuiltCopyOutParsers() { }
        public static readonly InbuiltCopyOutParsers Default = new();

        string? IRespParser<string?>.Parse(ref RespReader reader) => reader.ReadString();
        byte[]? IRespParser<byte[]?>.Parse(ref RespReader reader) => reader.ReadByteArray();

        byte[]?[]? IRespParser<byte[]?[]?>.Parse(ref RespReader reader) =>
            reader.ReadArray(static (ref RespReader reader) => reader.ReadByteArray());

        int IRespParser<IBufferWriter<byte>, int>.Parse(in IBufferWriter<byte> state, ref RespReader reader)
        {
            reader.DemandScalar();
            if (reader.IsNull) return -1;
            return reader.CopyTo(state);
        }
    }

    public readonly struct ResponseSummary(RespPrefix prefix, int length, long protocolBytes)
        : IEquatable<ResponseSummary>
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

        public static IRespParser<ResponseSummary> Parser => ResponseSummaryParser.Default;

        internal sealed class ResponseSummaryParser : IRespParser<ResponseSummary>, IRespInlineParser,
            IRespMetadataParser
        {
            private ResponseSummaryParser() { }
            public static readonly ResponseSummaryParser Default = new();

            public ResponseSummary Parse(ref RespReader reader)
            {
                var protocolBytes = reader.ProtocolBytesRemaining;
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
}
