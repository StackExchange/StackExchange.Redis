namespace StackExchange.Redis;

internal sealed class SetOperationCardinalityMessage(
    int db,
    CommandFlags flags,
    RedisCommand command,
    RedisKey[] keys,
    long limit) : Message(db, flags, command)
{
    private readonly RedisKey[] _keys = keys.AssertAllNonNull();

    public SetOperationCardinalityMessage(
        int db,
        CommandFlags flags,
        SetOperation operation,
        RedisKey[] keys,
        long limit) : this(db, flags, operation.ToCardinalityCommand(), keys, limit)
    {
    }

    public override int ArgCount => 1 + _keys.Length + (limit > 0 ? 2 : 0);

    public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy) => serverSelectionStrategy.HashSlot(_keys);

    protected override void WriteImpl(PhysicalConnection physical)
    {
        physical.WriteHeader(Command, ArgCount);
        physical.WriteBulkString(_keys.Length);
        for (var i = 0; i < _keys.Length; i++)
        {
            physical.Write(_keys[i]);
        }

        if (limit > 0)
        {
            physical.WriteRaw("$5\r\nLIMIT\r\n"u8);
            physical.WriteBulkString(limit);
        }
    }
}
