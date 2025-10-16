using System.Runtime.CompilerServices;
using RESPite.Internal;

namespace RESPite.Connections.Internal;

internal sealed class RoutedConnection : RespConnection
{
    private Shard[] _shards = [];

    private Shard[] _primaries = [], _replicas = [];

    public void SetRoutingTable(ReadOnlySpan<Shard> shards)
    {
        if (shards.Length == _shards.Length)
        {
            bool match = true;
            int index = 0;
            Shard previous = default;
            foreach (ref readonly Shard shard in shards)
            {
                if (index != 0 && previous.CompareTo(shard) > 0) ThrowNotSorted();
                if (!shard.Equals(_shards[index++]))
                {
                    match = false;
                    break;
                }

                previous = shard;
            }

            if (match) return; // nothing has changed
        }

        _shards = shards.ToArray();

        static void ThrowNotSorted() =>
            throw new InvalidOperationException($"The input to {nameof(SetRoutingTable)} must be pre-sorted.");
    }

    public override event EventHandler<RespConnectionErrorEventArgs>? ConnectionError
    {
        add => throw new NotSupportedException();
        remove => throw new NotSupportedException();
    }

    internal override int OutstandingOperations
    {
        get
        {
            int count = 0;
            foreach (var shard in _shards)
            {
                if (shard.GetConnection() is { } conn) count += conn.OutstandingOperations;
            }

            return count;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(in RespOperation message)
    {
        // simplest thing possible for now; long term, we could do bunching.
        var conn = Select(
            replicas: (message.Flags & RespMessageBase.StateFlags.Replica) != 0,
            slot: message.Slot);
        if (conn is null)
        {
            WriteNonPreferred(message);
        }
        else
        {
            // this is the happy path
            conn.Write(in message);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void WriteNonPreferred(in RespOperation message)
    {
        var flags = message.Flags;
        var conn = (flags & RespMessageBase.StateFlags.Demand) == 0
            ? Select((flags & RespMessageBase.StateFlags.Replica) == 0, message.Slot)
            : null;
        if (conn is null)
        {
            message.TrySetException(
                new InvalidOperationException("No connection is available to handle this request."));
        }
        else
        {
            conn.Write(in message);
        }
    }

    private RespConnection? Select(bool replicas, int slot)
    {
        var shards = replicas ? _replicas : _primaries;
        foreach (var shard in shards)
        {
            if ((shard.From <= slot & shard.To >= slot)
                && shard.GetConnection() is { IsHealthy: true } conn)
            {
                return conn;
            }
        }

        return null;
    }
}
