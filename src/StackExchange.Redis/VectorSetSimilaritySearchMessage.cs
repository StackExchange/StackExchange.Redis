using System;

namespace StackExchange.Redis;

internal sealed class VectorSetSimilaritySearchMessage(
    int db,
    CommandFlags flags,
    RedisKey key,
    RedisValue member,
    ReadOnlyMemory<float> vector,
    int? count,
    bool withScores,
    bool withAttributes,
    double? epsilon,
    int? searchExplorationFactor,
    string? filterExpression,
    int? maxFilteringEffort,
    bool useExactSearch,
    bool disableThreading) : Message(db, flags, RedisCommand.VSIM)
{
    private readonly VsimFlags _flags =
        (count.HasValue ? VsimFlags.Count : 0) |
        (withScores ? VsimFlags.WithScores : 0) |
        (withAttributes ? VsimFlags.WithAttributes : 0) |
        (useExactSearch ? VsimFlags.UseExactSearch : 0) |
        (disableThreading ? VsimFlags.DisableThreading : 0) |
        (epsilon.HasValue ? VsimFlags.Epsilon : 0) |
        (searchExplorationFactor.HasValue ? VsimFlags.SearchExplorationFactor : 0) |
        (maxFilteringEffort.HasValue ? VsimFlags.MaxFilteringEffort : 0);

    private readonly double _epsilon = epsilon.GetValueOrDefault();
    private readonly int _count = count.GetValueOrDefault();
    private readonly int _searchExplorationFactor = searchExplorationFactor.GetValueOrDefault();
    private readonly int _maxFilteringEffort = maxFilteringEffort.GetValueOrDefault();

    [Flags]
    private enum VsimFlags
    {
        None = 0,
        Count = 1 << 0,
        WithScores = 1 << 1,
        WithAttributes = 1 << 2,
        UseExactSearch = 1 << 3,
        DisableThreading = 1 << 4,
        Epsilon = 1 << 5,
        SearchExplorationFactor = 1 << 6,
        MaxFilteringEffort = 1 << 7,
    }

    private bool HasFlag(VsimFlags flag) => (_flags & flag) != 0;

    public override int ArgCount => GetArgCount(VectorSetAddMessage.UseFp32);

    private int GetArgCount(bool useFp32)
    {
        int argCount = 3; // {key} and "ELE {member}", "FP32 {vector}" or "VALUES {num}"
        if (member.IsNull && !useFp32)
        {
            argCount += vector.Length; // {vector} in the VALUES case
        }

        if (HasFlag(VsimFlags.WithScores)) argCount++; // [WITHSCORES]
        if (HasFlag(VsimFlags.WithAttributes)) argCount++; // [WITHATTRIBS]
        if (HasFlag(VsimFlags.Count)) argCount += 2; // [COUNT {count}]
        if (HasFlag(VsimFlags.Epsilon)) argCount += 2; // [EPSILON {epsilon}]
        if (HasFlag(VsimFlags.SearchExplorationFactor)) argCount += 2; // [EF {search-exploration-factor}]
        if (filterExpression is not null) argCount += 2; // [FILTER {filterExpression}]
        if (HasFlag(VsimFlags.MaxFilteringEffort)) argCount += 2; // [FILTER-EF {max-filtering-effort}]
        if (HasFlag(VsimFlags.UseExactSearch)) argCount++; // [TRUTH]
        if (HasFlag(VsimFlags.DisableThreading)) argCount++; // [NOTHREAD]
        return argCount;
    }

    protected override void WriteImpl(PhysicalConnection physical)
    {
        var useFp32 = VectorSetAddMessage.UseFp32; // avoid race in debug mode
        physical.WriteHeader(Command, GetArgCount(useFp32));

        // Write key
        physical.Write(key);

        // Write search target: either "ELE {member}" or vector data
        if (!member.IsNull)
        {
            // Member-based search: "ELE {member}"
            physical.WriteBulkString("ELE"u8);
            physical.WriteBulkString(member);
        }
        else
        {
            // Vector-based search: either "FP32 {vector}" or "VALUES {num} {vector}"
            if (useFp32)
            {
                physical.WriteBulkString("FP32"u8);
                physical.WriteBulkString(System.Runtime.InteropServices.MemoryMarshal.AsBytes(vector.Span));
            }
            else
            {
                physical.WriteBulkString("VALUES"u8);
                physical.WriteBulkString(vector.Length);
                foreach (var val in vector.Span)
                {
                    physical.WriteBulkString(val);
                }
            }
        }

        if (HasFlag(VsimFlags.WithScores))
        {
            physical.WriteBulkString("WITHSCORES"u8);
        }

        if (HasFlag(VsimFlags.WithAttributes))
        {
            physical.WriteBulkString("WITHATTRIBS"u8);
        }

        // Write optional parameters
        if (HasFlag(VsimFlags.Count))
        {
            physical.WriteBulkString("COUNT"u8);
            physical.WriteBulkString(_count);
        }

        if (HasFlag(VsimFlags.Epsilon))
        {
            physical.WriteBulkString("EPSILON"u8);
            physical.WriteBulkString(_epsilon);
        }

        if (HasFlag(VsimFlags.SearchExplorationFactor))
        {
            physical.WriteBulkString("EF"u8);
            physical.WriteBulkString(_searchExplorationFactor);
        }

        if (filterExpression is not null)
        {
            physical.WriteBulkString("FILTER"u8);
            physical.WriteBulkString(filterExpression);
        }

        if (HasFlag(VsimFlags.MaxFilteringEffort))
        {
            physical.WriteBulkString("FILTER-EF"u8);
            physical.WriteBulkString(_maxFilteringEffort);
        }

        if (HasFlag(VsimFlags.UseExactSearch))
        {
            physical.WriteBulkString("TRUTH"u8);
        }

        if (HasFlag(VsimFlags.DisableThreading))
        {
            physical.WriteBulkString("NOTHREAD"u8);
        }
    }

    public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy)
        => serverSelectionStrategy.HashSlot(key);

    /*
     private int GetArgCount(bool useFp32)
     {
         var count = 4; // key, element and either "FP32 {vector}" or VALUES {num}"
         if (reducedDimensions.HasValue) count += 2; // [REDUCE {dim}]

         if (!useFp32) count += values.Length; // {vector} in the VALUES case

         if (useCheckAndSet) count++; // [CAS]
         count += quantizationType switch
         {
             VectorQuantizationType.None or VectorQuantizationType.Binary => 1, // [NOQUANT] or [BIN]
             VectorQuantizationType.Int8 => 0, // implicit
             _ => throw new ArgumentOutOfRangeException(nameof(quantizationType)),
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

         switch (quantizationType)
         {
             case VectorQuantizationType.Int8:
                 break;
             case VectorQuantizationType.None:
                 physical.WriteBulkString("NOQUANT"u8);
                 break;
             case VectorQuantizationType.Binary:
                 physical.WriteBulkString("BIN"u8);
                 break;
             default:
                 throw new ArgumentOutOfRangeException(nameof(quantizationType));
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
         */
}
