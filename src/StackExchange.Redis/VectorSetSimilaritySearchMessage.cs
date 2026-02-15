using System;

namespace StackExchange.Redis;

internal abstract class VectorSetSimilaritySearchMessage(
    int db,
    CommandFlags flags,
    VectorSetSimilaritySearchMessage.VsimFlags vsimFlags,
    RedisKey key,
    int count,
    double epsilon,
    int searchExplorationFactor,
    string? filterExpression,
    int maxFilteringEffort) : Message(db, flags, RedisCommand.VSIM)
{
    // For "FP32" and "VALUES" scenarios; in the future we might want other vector sizes / encodings - for
    // example, there could be some "FP16" or "FP8" transport that requires a ROM-short or ROM-sbyte from
    // the calling code. Or, as a convenience, we might want to allow ROM-double input, but transcode that
    // to FP32 on the way out.
    internal sealed class VectorSetSimilaritySearchBySingleVectorMessage(
        int db,
        CommandFlags flags,
        VsimFlags vsimFlags,
        RedisKey key,
        ReadOnlyMemory<float> vector,
        int count,
        double epsilon,
        int searchExplorationFactor,
        string? filterExpression,
        int maxFilteringEffort) : VectorSetSimilaritySearchMessage(db, flags, vsimFlags, key, count, epsilon,
        searchExplorationFactor, filterExpression, maxFilteringEffort)
    {
        internal override int GetSearchTargetArgCount(bool packed) =>
            packed ? 2 : 2 + vector.Length; // FP32 {vector} or VALUES {num} {vector}

        internal override void WriteSearchTarget(bool packed, PhysicalConnection physical)
        {
            if (packed)
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
    }

    // for "ELE" scenarios
    internal sealed class VectorSetSimilaritySearchByMemberMessage(
        int db,
        CommandFlags flags,
        VsimFlags vsimFlags,
        RedisKey key,
        RedisValue member,
        int count,
        double epsilon,
        int searchExplorationFactor,
        string? filterExpression,
        int maxFilteringEffort) : VectorSetSimilaritySearchMessage(db, flags, vsimFlags, key, count, epsilon,
        searchExplorationFactor, filterExpression, maxFilteringEffort)
    {
        internal override int GetSearchTargetArgCount(bool packed) => 2; // ELE {member}

        internal override void WriteSearchTarget(bool packed, PhysicalConnection physical)
        {
            physical.WriteBulkString("ELE"u8);
            physical.WriteBulkString(member);
        }
    }

    internal abstract int GetSearchTargetArgCount(bool packed);
    internal abstract void WriteSearchTarget(bool packed, PhysicalConnection physical);

    public ResultProcessor<Lease<VectorSetSimilaritySearchResult>?> GetResultProcessor() =>
        VectorSetSimilaritySearchProcessor.Instance;

    private sealed class VectorSetSimilaritySearchProcessor : ResultProcessor<Lease<VectorSetSimilaritySearchResult>?>
    {
        // keep local, since we need to know what flags were being sent
        public static readonly VectorSetSimilaritySearchProcessor Instance = new();
        private VectorSetSimilaritySearchProcessor() { }

        protected override bool SetResultCore(PhysicalConnection connection, Message message, RawResult result)
        {
            if (result.Resp2TypeArray == ResultType.Array && message is VectorSetSimilaritySearchMessage vssm)
            {
                if (result.IsNull)
                {
                    SetResult(message, null);
                    return true;
                }

                bool withScores = vssm.HasFlag(VsimFlags.WithScores);
                bool withAttribs = vssm.HasFlag(VsimFlags.WithAttributes);

                // in RESP3 mode (only), when both are requested, we get a sub-array per item; weird, but true
                bool internalNesting = withScores && withAttribs && connection.Protocol is RedisProtocol.Resp3;

                int rowsPerItem = internalNesting
                    ? 2
                    : 1 + ((withScores ? 1 : 0) + (withAttribs ? 1 : 0)); // each value is separate root element

                var items = result.GetItems();
                var length = checked((int)items.Length) / rowsPerItem;
                var lease = Lease<VectorSetSimilaritySearchResult>.Create(length, clear: false);
                var target = lease.Span;
                int count = 0;
                var iter = items.GetEnumerator();
                for (int i = 0; i < target.Length && iter.MoveNext(); i++)
                {
                    var member = iter.Current.AsRedisValue();
                    double score = double.NaN;
                    string? attributesJson = null;

                    if (internalNesting)
                    {
                        if (!iter.MoveNext() || iter.Current.Resp2TypeArray != ResultType.Array) break;
                        if (!iter.Current.IsNull)
                        {
                            var subArray = iter.Current.GetItems();
                            if (subArray.Length >= 1 && !subArray[0].TryGetDouble(out score)) break;
                            if (subArray.Length >= 2) attributesJson = subArray[1].GetString();
                        }
                    }
                    else
                    {
                        if (withScores)
                        {
                            if (!iter.MoveNext() || !iter.Current.TryGetDouble(out score)) break;
                        }

                        if (withAttribs)
                        {
                            if (!iter.MoveNext()) break;
                            attributesJson = iter.Current.GetString();
                        }
                    }

                    target[i] = new VectorSetSimilaritySearchResult(member, score, attributesJson);
                    count++;
                }

                if (count == target.Length)
                {
                    SetResult(message, lease);
                    return true;
                }

                lease.Dispose(); // failed to fill?
            }

            return false;
        }
    }

    [Flags]
    internal enum VsimFlags
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
        FilterExpression = 1 << 8,
    }

    private bool HasFlag(VsimFlags flag) => (vsimFlags & flag) != 0;

    public override int ArgCount => GetArgCount(VectorSetAddMessage.UseFp32);

    private int GetArgCount(bool packed)
    {
        int argCount = 1 + GetSearchTargetArgCount(packed); // {key} and whatever we need for the vector/element portion
        if (HasFlag(VsimFlags.WithScores)) argCount++; // [WITHSCORES]
        if (HasFlag(VsimFlags.WithAttributes)) argCount++; // [WITHATTRIBS]
        if (HasFlag(VsimFlags.Count)) argCount += 2; // [COUNT {count}]
        if (HasFlag(VsimFlags.Epsilon)) argCount += 2; // [EPSILON {epsilon}]
        if (HasFlag(VsimFlags.SearchExplorationFactor)) argCount += 2; // [EF {search-exploration-factor}]
        if (HasFlag(VsimFlags.FilterExpression)) argCount += 2; // [FILTER {filterExpression}]
        if (HasFlag(VsimFlags.MaxFilteringEffort)) argCount += 2; // [FILTER-EF {max-filtering-effort}]
        if (HasFlag(VsimFlags.UseExactSearch)) argCount++; // [TRUTH]
        if (HasFlag(VsimFlags.DisableThreading)) argCount++; // [NOTHREAD]
        return argCount;
    }

    protected override void WriteImpl(PhysicalConnection physical)
    {
        // snapshot to avoid race in debug scenarios
        bool packed = VectorSetAddMessage.UseFp32;
        physical.WriteHeader(Command, GetArgCount(packed));

        // Write key
        physical.Write(key);

        // Write search target: either "ELE {member}" or vector data
        WriteSearchTarget(packed, physical);

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
            physical.WriteBulkString(count);
        }

        if (HasFlag(VsimFlags.Epsilon))
        {
            physical.WriteBulkString("EPSILON"u8);
            physical.WriteBulkString(epsilon);
        }

        if (HasFlag(VsimFlags.SearchExplorationFactor))
        {
            physical.WriteBulkString("EF"u8);
            physical.WriteBulkString(searchExplorationFactor);
        }

        if (HasFlag(VsimFlags.FilterExpression))
        {
            physical.WriteBulkString("FILTER"u8);
            physical.WriteBulkString(filterExpression);
        }

        if (HasFlag(VsimFlags.MaxFilteringEffort))
        {
            physical.WriteBulkString("FILTER-EF"u8);
            physical.WriteBulkString(maxFilteringEffort);
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
}
