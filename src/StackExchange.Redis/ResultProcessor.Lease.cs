using System.Diagnostics;
using Pipelines.Sockets.Unofficial.Arenas;
using RESPite.Messages;

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
        protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            if (!reader.IsAggregate)
            {
                return false; // not an array
            }

            // deal with null
            if (reader.IsNull)
            {
                SetResult(message, null);
                return true;
            }

            // lease and fill
            var length = reader.AggregateLength();
            if (length == 0)
            {
                SetResult(message, Lease<T>.Empty);
                return true;
            }

            var lease = Lease<T>.Create(length, clear: false);
            try
            {
                var self = this;
                reader.FillAll(lease.Span, ref self, static (ref s, ref r) => s.TryParse(ref r));
                SetResult(message, lease);
                return true;
            }
            catch
            {
                // something went wrong; recycle and quit
                lease.Dispose();
                throw;
            }
        }

        protected abstract T TryParse(ref RespReader reader);
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
        protected override float TryParse(ref RespReader reader) => (float)reader.ReadDouble();
    }

    private sealed class LeaseProcessor : ResultProcessor<Lease<byte>>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            if (reader.IsScalar)
            {
                SetResult(message, reader.AsLease()!);
                return true;
            }
            return false;
        }
    }

    private sealed class LeaseFromArrayProcessor : ResultProcessor<Lease<byte>>
    {
        protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            if (reader.IsAggregate && reader.AggregateLengthIs(1)
                && reader.TryMoveNext() && reader.IsScalar)
            {
                // treat an array of 1 like a single reply
                SetResult(message, reader.AsLease()!);
                return true;
            }
            return false;
        }
    }
}
