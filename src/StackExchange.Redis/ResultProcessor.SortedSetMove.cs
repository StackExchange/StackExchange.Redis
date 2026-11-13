using RESPite.Messages;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

internal abstract partial class ResultProcessor
{
    /// <summary>
    /// Parses the reply of <c>ZMOVE</c> / <c>ZMOVEMEMBER</c>: an array of <c>[member, score]</c> pairs, or
    /// <see langword="null"/> when nothing moved. The null reply is preserved as <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Unlike <c>ZPOPMIN</c> and <c>ZRANGE ... WITHSCORES</c>, the pairs are nested in RESP2 as well as in RESP3 (only the
    /// score differs: a bulk string in RESP2, a double in RESP3), so <see cref="SortedSetWithScores"/> - which reads
    /// RESP2 as a flat, interleaved list - does not fit.
    /// </remarks>
    public static readonly ResultProcessor<SortedSetEntry[]?>
        NullableSortedSetEntryArray = new NullableSortedSetEntryArrayProcessor();

    private sealed class NullableSortedSetEntryArrayProcessor : ResultProcessor<SortedSetEntry[]?>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            if (reader.IsNull)
            {
                SetResult(message, null);
                return true;
            }

            if (!reader.IsAggregate) return false; // scalar / other => unexpected

            var entries = new SortedSetEntry[reader.AggregateLength()];
            var iter = reader.AggregateChildren();
            for (int i = 0; i < entries.Length; i++)
            {
                iter.DemandNext();
                if (!(iter.Value.IsAggregate && iter.Value.AggregateLengthIs(2))) return false; // not a pair

                var pair = iter.Value.AggregateChildren();
                pair.DemandNext();
                var member = pair.Value;
                pair.DemandNext();
                var score = pair.Value;
                if (!(member.IsScalar && score.IsScalar)) return false;

                entries[i] = new SortedSetEntry(member.ReadRedisValue(), score.TryReadDouble(out double val) ? val : double.NaN);
            }

            SetResult(message, entries);
            return true;
        }
    }
}
