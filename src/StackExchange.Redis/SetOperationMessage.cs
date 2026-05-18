using System;

namespace StackExchange.Redis;

internal sealed class SetOperationMessage : Message
{
    private readonly RedisKey[] _keys;
    private readonly double[]? _weights;
    private readonly Aggregate _aggregate;
    private readonly bool _withScores;

    public SetOperationMessage(
        int db,
        CommandFlags flags,
        SetOperation operation,
        RedisKey[] keys,
        double[]? weights,
        Aggregate aggregate,
        bool withScores) : base(db, flags, operation.ToBasicCommand())
    {
        _keys = keys.AssertAllNonNull();
        if (operation == SetOperation.Difference && (weights != null || aggregate != Aggregate.Sum))
        {
            throw new ArgumentException("ZDIFF cannot be used with weights or aggregation.");
        }
        if (weights != null && _keys.Length != weights.Length)
        {
            throw new ArgumentException("Keys and weights should have the same number of elements.", nameof(weights));
        }

        ValidateAggregate(aggregate);
        _weights = weights;
        _aggregate = aggregate;
        _withScores = withScores;
    }

    public override int ArgCount =>
        1 + _keys.Length
        + (_weights?.Length > 0 ? 1 + _weights.Length : 0)
        + GetAggregateArgCount(_aggregate)
        + (_withScores ? 1 : 0);

    public override int GetHashSlot(ServerSelectionStrategy serverSelectionStrategy) => serverSelectionStrategy.HashSlot(_keys);

    protected override void WriteImpl(PhysicalConnection physical)
    {
        physical.WriteHeader(Command, ArgCount);
        physical.WriteBulkString(_keys.Length);
        for (var i = 0; i < _keys.Length; i++)
        {
            physical.Write(_keys[i]);
        }

        if (_weights?.Length > 0)
        {
            physical.WriteRaw("$7\r\nWEIGHTS\r\n"u8);
            for (var i = 0; i < _weights.Length; i++)
            {
                physical.WriteBulkString(_weights[i]);
            }
        }

        switch (_aggregate)
        {
            case Aggregate.Sum:
                break;
            case Aggregate.Min:
                physical.WriteRaw("$9\r\nAGGREGATE\r\n$3\r\nMIN\r\n"u8);
                break;
            case Aggregate.Max:
                physical.WriteRaw("$9\r\nAGGREGATE\r\n$3\r\nMAX\r\n"u8);
                break;
            case Aggregate.Count:
                physical.WriteRaw("$9\r\nAGGREGATE\r\n$5\r\nCOUNT\r\n"u8);
                break;
        }

        if (_withScores)
        {
            physical.WriteRaw("$10\r\nWITHSCORES\r\n"u8);
        }
    }

    private static void ValidateAggregate(Aggregate aggregate) => _ = GetAggregateArgCount(aggregate);

    private static int GetAggregateArgCount(Aggregate aggregate) => aggregate switch
    {
        Aggregate.Sum => 0,
        Aggregate.Min or Aggregate.Max or Aggregate.Count => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(aggregate)),
    };
}
