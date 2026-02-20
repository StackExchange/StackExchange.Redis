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
        protected virtual long GetArrayLength(in RespReader reader) => reader.AggregateLength();

        protected virtual bool TryReadOne(ref RespReader reader, out T value)
        {
            value = default!;
            return false;
        }

        protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            if (!reader.IsAggregate)
            {
                return false; // not an array
            }

            // deal with null
            if (reader.IsNull)
            {
                SetResult(message, Lease<T>.Empty);
                return true;
            }

            // First pass: count total elements across all nested arrays
            long totalLength = 0;
            var iter = reader.AggregateChildren();
            while (iter.MoveNext())
            {
                if (iter.Value.IsAggregate && !iter.Value.IsNull)
                {
                    totalLength += GetArrayLength(in iter.Value);
                }
            }

            if (totalLength == 0)
            {
                SetResult(message, Lease<T>.Empty);
                return true;
            }

            // Second pass: fill the lease
            var lease = Lease<T>.Create(checked((int)totalLength), clear: false);
            int index = 0;
            var target = lease.Span;

            try
            {
                iter = reader.AggregateChildren();
                while (iter.MoveNext())
                {
                    if (iter.Value.IsAggregate && !iter.Value.IsNull)
                    {
                        var childReader = iter.Value;
                        while (childReader.TryMoveNext() && index < target.Length)
                        {
                            if (!TryReadOne(ref childReader, out target[index]))
                            {
                                lease.Dispose();
                                return false;
                            }
                            index++;
                        }
                    }
                }

                if (index == totalLength)
                {
                    SetResult(message, lease);
                    return true;
                }
                lease.Dispose(); // failed to fill?
                return false;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
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
