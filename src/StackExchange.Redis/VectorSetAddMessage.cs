using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace StackExchange.Redis;

internal sealed class VectorSetAddMessage(
    int database,
    CommandFlags flags,
    RedisKey key,
    RedisValue element,
    ReadOnlyMemory<float> values,
    int? reducedDimensions,
    VectorSetQuantization quantization,
    int? buildExplorationFactor,
    int? maxConnections,
    bool useCheckAndSet,
    string? attributesJson) : Message(database, flags, RedisCommand.VADD)
{
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

    public override int ArgCount => GetArgCount(UseFp32);

    private int GetArgCount(bool useFp32)
    {
        var count = 4; // key, element and either "FP32 {vector}" or VALUES {num}"
        if (reducedDimensions.HasValue) count += 2; // [REDUCE {dim}]

        if (!useFp32) count += values.Length; // {vector} in the VALUES case

        if (useCheckAndSet) count++; // [CAS]
        count += quantization switch
        {
            VectorSetQuantization.None or VectorSetQuantization.Binary => 1, // [NOQUANT] or [BIN]
            VectorSetQuantization.Int8 => 0, // implicit
            _ => throw new ArgumentOutOfRangeException(nameof(quantization)),
        };

        if (buildExplorationFactor.HasValue) count += 2; // [EF {build-exploration-factor}]
        if (attributesJson is not null) count += 2; // [SETATTR {attributes}]
        if (maxConnections.HasValue) count += 2; // [M {numlinks}]
        return count;
    }

    protected override void WriteImpl(PhysicalConnection physical)
    {
        bool useFp32 = UseFp32; // snapshot to avoid race in debug scenarios
        physical.WriteHeader(Command, GetArgCount(useFp32));
        physical.Write(key);
        if (reducedDimensions.HasValue)
        {
            physical.WriteBulkString("REDUCE"u8);
            physical.WriteBulkString(reducedDimensions.GetValueOrDefault());
        }
        if (useFp32)
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
        if (attributesJson is not null)
        {
            physical.WriteBulkString("SETATTR"u8);
            physical.WriteBulkString(attributesJson);
        }
        if (maxConnections.HasValue)
        {
            physical.WriteBulkString("M"u8);
            physical.WriteBulkString(maxConnections.GetValueOrDefault());
        }
    }

    public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
        => serverSelectionStrategy.HashSlot(key);
}
