namespace StackExchange.Redis;

internal sealed class SetOperationCardinalityMessage(
    int db,
    CommandFlags flags,
    RedisCommand command,
    RedisKey[] keys,
    long limit,
    bool approximate) : Message(db, flags, command)
{
    private readonly RedisKey[] _keys = keys.AssertAllNonNull();

    public override int ArgCount => 1 + _keys.Length + (approximate ? 1 : 0) + (limit > 0 ? 2 : 0);

    public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy) => serverSelectionStrategy.HashSlot(_keys);

    protected override void WriteImpl(in MessageWriter writer)
    {
        writer.WriteHeader(Command, ArgCount);
        writer.WriteBulkString(_keys.Length);
        for (var i = 0; i < _keys.Length; i++)
        {
            writer.Write(_keys[i]);
        }

        if (approximate)
        {
            writer.WriteRaw("$6\r\nAPPROX\r\n"u8);
        }

        if (limit > 0)
        {
            writer.WriteRaw("$5\r\nLIMIT\r\n"u8);
            writer.WriteBulkString(limit);
        }
    }
}
