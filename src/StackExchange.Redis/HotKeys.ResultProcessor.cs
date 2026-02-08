using System;
using System.Threading.Tasks;

namespace StackExchange.Redis;

public sealed partial class HotKeysResult
{
    internal static readonly ResultProcessor<HotKeysResult?> Processor = new HotKeysResultProcessor();

    private sealed class HotKeysResultProcessor : ResultProcessor<HotKeysResult?>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, in RawResult result)
        {
            if (result.IsNull)
            {
                SetResult(message, null);
                return true;
            }

            if (result.Resp2TypeBulkString == ResultType.Array)
            {
                var hotKeys = new HotKeysResult(in result);
                SetResult(message, hotKeys);
                return true;
            }

            return false;
        }
    }

    private HotKeysResult(in RawResult result)
    {
        var iter = result.GetItems().GetEnumerator();
        while (iter.MoveNext())
        {
            ref readonly RawResult key = ref iter.Current;
            if (iter.MoveNext())
            {
                ref readonly RawResult value = ref iter.Current;
                var hash = key.Payload.Hash64();
                switch (hash)
                {
                    case tracking_active.Hash when tracking_active.Is(hash, value):
                        TrackingActive = value.GetBoolean();
                        break;
                }
            }
        }
    }

#pragma warning disable SA1134, SA1300
    // ReSharper disable InconsistentNaming
    [FastHash] internal static partial class tracking_active { }
    // ReSharper restore InconsistentNaming
#pragma warning restore SA1134, SA1300
}
