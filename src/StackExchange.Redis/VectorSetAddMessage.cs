using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace StackExchange.Redis;

internal abstract class VectorSetAddMessage(
    int db,
    CommandFlags flags,
    RedisKey key,
    int? reducedDimensions,
    VectorSetQuantization quantization,
    int? buildExplorationFactor,
    int? maxConnections,
    bool useCheckAndSet) : Message(db, flags, RedisCommand.VADD)
{
    public override int ArgCount => GetArgCount(UseFp32);

    private int GetArgCount(bool packed)
    {
        var count = 2 + GetElementArgCount(packed); // key, element and either "FP32 {vector}" or VALUES {num}"
        if (reducedDimensions.HasValue) count += 2; // [REDUCE {dim}]

        if (useCheckAndSet) count++; // [CAS]
        count += quantization switch
        {
            VectorSetQuantization.None or VectorSetQuantization.Binary => 1, // [NOQUANT] or [BIN]
            VectorSetQuantization.Int8 => 0, // implicit
            _ => throw new ArgumentOutOfRangeException(nameof(quantization)),
        };

        if (buildExplorationFactor.HasValue) count += 2; // [EF {build-exploration-factor}]
        count += GetAttributeArgCount(); // [SETATTR {attributes}]
        if (maxConnections.HasValue) count += 2; // [M {numlinks}]
        return count;
    }

    public abstract int GetElementArgCount(bool packed);
    public abstract int GetAttributeArgCount();

    public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
        => serverSelectionStrategy.HashSlot(key);

    private static readonly bool CanUseFp32 = BitConverter.IsLittleEndian && CheckFp32();

    private static bool CheckFp32() // check endianness with a known value
    {
        // ReSharper disable once CompareOfFloatsByEqualityOperator - expect exact
        return MemoryMarshal.Cast<byte, float>("\0\0(B"u8)[0] == 42;
    }

#if DEBUG
    private static int _fp32Disabled;
    internal static bool UseFp32 => CanUseFp32 & Volatile.Read(ref _fp32Disabled) == 0;
    internal static void SuppressFp32() => Interlocked.Increment(ref _fp32Disabled);
    internal static void RestoreFp32() => Interlocked.Decrement(ref _fp32Disabled);
#else
    internal static bool UseFp32 => CanUseFp32;
    internal static void SuppressFp32() { }
    internal static void RestoreFp32() { }
#endif

    protected abstract void WriteElement(bool packed, PhysicalConnection physical);

    protected override void WriteImpl(PhysicalConnection physical)
    {
        bool packed = UseFp32; // snapshot to avoid race in debug scenarios
        physical.WriteHeader(Command, GetArgCount(packed));
        physical.Write(key);
        if (reducedDimensions.HasValue)
        {
            physical.WriteBulkString("REDUCE"u8);
            physical.WriteBulkString(reducedDimensions.GetValueOrDefault());
        }

        WriteElement(packed, physical);
        if (useCheckAndSet) physical.WriteBulkString("CAS"u8);

        switch (quantization)
        {
            case VectorSetQuantization.Int8:
                break;
            case VectorSetQuantization.None:
                physical.WriteBulkString("NOQUANT"u8);
                break;
            case VectorSetQuantization.Binary:
                physical.WriteBulkString("BIN"u8);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(quantization));
        }

        if (buildExplorationFactor.HasValue)
        {
            physical.WriteBulkString("EF"u8);
            physical.WriteBulkString(buildExplorationFactor.GetValueOrDefault());
        }

        WriteAttributes(physical);

        if (maxConnections.HasValue)
        {
            physical.WriteBulkString("M"u8);
            physical.WriteBulkString(maxConnections.GetValueOrDefault());
        }
    }

    protected abstract void WriteAttributes(PhysicalConnection physical);

    internal sealed class VectorSetAddMemberMessage(
        int db,
        CommandFlags flags,
        RedisKey key,
        int? reducedDimensions,
        VectorSetQuantization quantization,
        int? buildExplorationFactor,
        int? maxConnections,
        bool useCheckAndSet,
        RedisValue element,
        ReadOnlyMemory<float> values,
        string? attributesJson) : VectorSetAddMessage(
        db,
        flags,
        key,
        reducedDimensions,
        quantization,
        buildExplorationFactor,
        maxConnections,
        useCheckAndSet)
    {
        private readonly string? _attributesJson = string.IsNullOrWhiteSpace(attributesJson) ? null : attributesJson;
        public override int GetElementArgCount(bool packed)
            => 2 // "FP32 {vector}" or "VALUES {num}"
               + (packed ? 0 : values.Length); // {vector...}"

        public override int GetAttributeArgCount()
            => _attributesJson is null ? 0 : 2; // [SETATTR {attributes}]

        protected override void WriteElement(bool packed, PhysicalConnection physical)
        {
            if (packed)
            {
                physical.WriteBulkString("FP32"u8);
                physical.WriteBulkString(MemoryMarshal.AsBytes(values.Span));
            }
            else
            {
                physical.WriteBulkString("VALUES"u8);
                physical.WriteBulkString(values.Length);
                foreach (var val in values.Span)
                {
                    physical.WriteBulkString(val);
                }
            }

            physical.WriteBulkString(element);
        }

        protected override void WriteAttributes(PhysicalConnection physical)
        {
            if (_attributesJson is not null)
            {
                physical.WriteBulkString("SETATTR"u8);
                physical.WriteBulkString(_attributesJson);
            }
        }
    }
}
