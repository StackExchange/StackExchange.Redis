using System.Diagnostics;
using Pipelines.Sockets.Unofficial.Arenas;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

internal abstract partial class ResultProcessor
{
    // Lease result processors
    public static readonly ResultProcessor<Lease<float>?> LeaseFloat32 = new LeaseFloat32Processor();

    public static readonly ResultProcessor<Lease<byte>>
        Lease = new LeaseProcessor();

    public static readonly ResultProcessor<Lease<byte>>
        LeaseFromArray = new LeaseFromArrayProcessor();

    private abstract class LeaseProcessor<T> : ResultProcessor<Lease<T>?>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, RawResult result)
        {
            if (result.Resp2TypeArray != ResultType.Array)
            {
                return false; // not an array
            }

            // deal with null
            if (result.IsNull)
            {
                SetResult(message, Lease<T>.Empty);
                return true;
            }

            // lease and fill
            var items = result.GetItems();
            var length = checked((int)items.Length);
            var lease = Lease<T>.Create(length, clear: false); // note this handles zero nicely
            var target = lease.Span;
            int index = 0;
            foreach (ref RawResult item in items)
            {
                if (!TryParse(item, out target[index++]))
                {
                    // something went wrong; recycle and quit
                    lease.Dispose();
                    return false;
                }
            }
            Debug.Assert(index == length, "length mismatch");
            SetResult(message, lease);
            return true;
        }

        protected abstract bool TryParse(in RawResult raw, out T parsed);
    }

    private abstract class InterleavedLeaseProcessor<T> : ResultProcessor<Lease<T>?>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, RawResult result)
        {
            if (result.Resp2TypeArray != ResultType.Array)
            {
                return false; // not an array
            }

            // deal with null
            if (result.IsNull)
            {
                SetResult(message, Lease<T>.Empty);
                return true;
            }

            // lease and fill
            var items = result.GetItems();
            var length = checked((int)items.Length) / 2;
            var lease = Lease<T>.Create(length, clear: false); // note this handles zero nicely
            var target = lease.Span;

            var iter = items.GetEnumerator();
            for (int i = 0; i < target.Length; i++)
            {
                bool ok = iter.MoveNext();
                if (ok)
                {
                    ref readonly RawResult first = ref iter.Current;
                    ok = iter.MoveNext() && TryParse(in first, in iter.Current, out target[i]);
                }
                if (!ok)
                {
                    lease.Dispose();
                    return false;
                }
            }
            SetResult(message, lease);
            return true;
        }

        protected abstract bool TryParse(in RawResult first, in RawResult second, out T parsed);
    }

    // takes a nested vector of the form [[A],[B,C],[D]] and exposes it as [A,B,C,D]; this is
    // especially useful for VLINKS
    private abstract class FlattenedLeaseProcessor<T> : ResultProcessor<Lease<T>?>
    {
        protected virtual long GetArrayLength(in RawResult array) => array.GetItems().Length;

        protected virtual bool TryReadOne(ref Sequence<RawResult>.Enumerator reader, out T value)
        {
            if (reader.MoveNext())
            {
                return TryReadOne(in reader.Current, out value);
            }
            value = default!;
            return false;
        }

        protected virtual bool TryReadOne(in RawResult result, out T value)
        {
            value = default!;
            return false;
        }

        protected override bool SetResultCore(PhysicalConnection connection, Message message, RawResult result)
        {
            if (result.Resp2TypeArray != ResultType.Array)
            {
                return false; // not an array
            }
            if (result.IsNull)
            {
                SetResult(message, Lease<T>.Empty);
                return true;
            }
            var items = result.GetItems();
            long length = 0;
            foreach (ref RawResult item in items)
            {
                if (item.Resp2TypeArray == ResultType.Array && !item.IsNull)
                {
                    length += GetArrayLength(in item);
                }
            }

            if (length == 0)
            {
                SetResult(message, Lease<T>.Empty);
                return true;
            }
            var lease = Lease<T>.Create(checked((int)length), clear: false);
            int index = 0;
            var target = lease.Span;
            foreach (ref RawResult item in items)
            {
                if (item.Resp2TypeArray == ResultType.Array && !item.IsNull)
                {
                    var iter = item.GetItems().GetEnumerator();
                    while (index < target.Length && TryReadOne(ref iter, out target[index]))
                    {
                        index++;
                    }
                }
            }

            if (index == length)
            {
                SetResult(message, lease);
                return true;
            }
            lease.Dispose(); // failed to fill?
            return false;
        }
    }

    private sealed class LeaseFloat32Processor : LeaseProcessor<float>
    {
        protected override bool TryParse(in RawResult raw, out float parsed)
        {
            var result = raw.TryGetDouble(out double val);
            parsed = (float)val;
            return result;
        }
    }

    private sealed class LeaseProcessor : ResultProcessor<Lease<byte>>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, RawResult result)
        {
            switch (result.Resp2TypeBulkString)
            {
                case ResultType.Integer:
                case ResultType.SimpleString:
                case ResultType.BulkString:
                    SetResult(message, result.AsLease()!);
                    return true;
            }
            return false;
        }
    }

    private sealed class LeaseFromArrayProcessor : ResultProcessor<Lease<byte>>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, RawResult result)
        {
            switch (result.Resp2TypeBulkString)
            {
                case ResultType.Array:
                    var items = result.GetItems();
                    if (items.Length == 1)
                    { // treat an array of 1 like a single reply
                        SetResult(message, items[0].AsLease()!);
                        return true;
                    }
                    break;
            }
            return false;
        }
    }
}
