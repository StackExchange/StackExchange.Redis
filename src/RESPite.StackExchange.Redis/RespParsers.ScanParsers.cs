using RESPite.Messages;
using StackExchange.Redis;

namespace RESPite.StackExchange.Redis;

public static partial class RespParsers
{
    internal static IRespParser<ScanResult<SortedSetEntry>> ZScanSimple = ScanResultParser.NonLeased;
    internal static IRespParser<ScanResult<SortedSetEntry>> ZScanLeased = ScanResultParser.Leased;

    private sealed class ScanResultParser : IRespParser<ScanResult<SortedSetEntry>>
    {
        public static readonly ScanResultParser NonLeased = new(false);
        public static readonly ScanResultParser Leased = new(true);
        private readonly bool _leased;
        private ScanResultParser(bool leased) => _leased = leased;

        ScanResult<SortedSetEntry> IRespParser<ScanResult<SortedSetEntry>>.Parse(ref RespReader reader)
        {
            reader.DemandAggregate();
            reader.MoveNextScalar();
            var cursor = reader.ReadInt64();
            reader.MoveNextAggregate();
            if (_leased)
            {
                var values = DefaultParser.ReadLeasedSortedSetEntryArray(ref reader, out int count);
                return new(cursor, values, count);
            }
            else
            {
                var values = DefaultParser.ReadSortedSetEntryArray(ref reader);
                return new(cursor, values);
            }
        }
    }
}
