using System;
using System.Collections.Generic;
using System.Linq;

namespace StackExchange.Redis;

/// <summary>
/// Represents a single Bitfield Operation.
/// </summary>
public struct BitfieldOperation
{
    private static string CreateOffset(bool offsetByBit, long offset) => $"{(offsetByBit ? string.Empty : "#")}{offset}";
    private static readonly string[] Encodings = Enumerable.Range(0, 127).Select(x => // 0?
    {
        var size = x % 64;
        var signedness = x < 65 ? "i" : "u";
        return $"{signedness}{size}";
    }).ToArray();

    private static RedisValue CreateEncoding(bool unsigned, byte size)
    {
        if (size == 0)
        {
            throw new ArgumentException("Invalid encoding, size must be non-zero", nameof(size));
        }

        if (unsigned && size > 63)
        {
            throw new ArgumentException(
                $"Invalid Encoding, unsigned bitfield operations support a maximum size of 63, provided size: {size}", nameof(size));
        }

        if (size > 64)
        {
            throw new ArgumentException(
                $"Invalid Encoding, signed bitfield operations support a maximum size of 64, provided size: {size}", nameof(size));
        }

        return Encodings[size + (!unsigned ? 0 : 64)];
    }

    internal string Offset;
    internal long? Value;
    internal BitFieldSubCommand SubCommand;
    internal RedisValue Encoding;
    internal BitfieldOverflowHandling? BitfieldOverflowHandling;

    /// <summary>
    /// Creates a Get Bitfield Subcommand struct to retrieve a single integer from the bitfield.
    /// </summary>
    /// <param name="offset">The offset into the bitfield to address.</param>
    /// <param name="width">The width of the encoding to interpret the bitfield width.</param>
    /// <param name="offsetByBit">Whether or not to offset into the bitfield by bits vs encoding.</param>
    /// <param name="unsigned">Whether or not to interpret the number gotten as an unsigned integer.</param>
    /// <returns></returns>
    public static BitfieldOperation Get(long offset, byte width, bool offsetByBit = true, bool unsigned = false)
    {
        var offsetValue = CreateOffset(offsetByBit, offset);
        return new BitfieldOperation
        {
            Offset = offsetValue,
            Value = null,
            SubCommand = BitFieldSubCommand.Get,
            Encoding = CreateEncoding(unsigned, width)
        };
    }

    /// <summary>
    /// Creates a Set Bitfield SubCommand to set a single integer from the bitfield.
    /// </summary>
    /// <param name="offset">The offset into the bitfield to address.</param>
    /// <param name="width">The width of the encoding to interpret the bitfield width.</param>
    /// <param name="value">The value to set the addressed bits to.</param>
    /// <param name="offsetByBit">Whether or not to offset into the bitfield by bits vs encoding.</param>
    /// <param name="unsigned">Whether or not to interpret the number gotten as an unsigned integer.</param>
    /// <returns></returns>
    public static BitfieldOperation Set(long offset, byte width, long value, bool offsetByBit = true, bool unsigned = false)
    {
        var offsetValue = CreateOffset(offsetByBit, offset);
        return new BitfieldOperation
        {
            Offset = offsetValue,
            Value = value,
            SubCommand = BitFieldSubCommand.Set,
            Encoding = CreateEncoding(unsigned, width)
        };
    }

    /// <summary>
    /// Creates an Increment Bitfield SubCommand to increment a single integer from the bitfield.
    /// </summary>
    /// <param name="offset">The offset into the bitfield to address.</param>
    /// <param name="width">The width of the encoding to interpret the bitfield width.</param>
    /// <param name="increment">The value to set the addressed bits to.</param>
    /// <param name="offsetByBit">Whether or not to offset into the bitfield by bits vs encoding.</param>
    /// <param name="unsigned">Whether or not to interpret the number gotten as an unsigned integer.</param>
    /// <param name="overflowHandling">How to handle overflows.</param>
    /// <returns></returns>
    public static BitfieldOperation Increment(long offset, byte width, long increment, bool offsetByBit = true, bool unsigned = false, BitfieldOverflowHandling overflowHandling = Redis.BitfieldOverflowHandling.Wrap)
    {
        var offsetValue = CreateOffset(offsetByBit, offset);
        return new BitfieldOperation
        {
            Offset = offsetValue,
            Value = increment,
            SubCommand = BitFieldSubCommand.Increment,
            Encoding = CreateEncoding(unsigned, width),
            BitfieldOverflowHandling = overflowHandling
        };
    }

    internal IEnumerable<RedisValue> EnumerateArgs()
    {
        if (SubCommand != BitFieldSubCommand.Get)
        {
            if (BitfieldOverflowHandling is not null && BitfieldOverflowHandling != Redis.BitfieldOverflowHandling.Wrap)
            {
                yield return RedisLiterals.OVERFLOW;
                yield return BitfieldOverflowHandling.Value.AsRedisValue();
            }
        }

        yield return SubCommand.AsRedisValue();
        yield return Encoding;
        yield return Offset;
        if (SubCommand != BitFieldSubCommand.Get)
        {
            if (Value is null)
            {
                throw new ArgumentNullException($"Value must not be null for {SubCommand.AsRedisValue()} commands");
            }

            yield return Value;
        }
    }

    internal int NumArgs()
    {
        var numArgs = 3;
        if (SubCommand != BitFieldSubCommand.Get)
        {
            numArgs += BitfieldOverflowHandling is not null && BitfieldOverflowHandling != Redis.BitfieldOverflowHandling.Wrap ? 3 : 1;
        }

        return numArgs;
    }
}

internal static class BitfieldOperationExtensions
{
    internal static BitfieldCommandMessage BuildMessage(this BitfieldOperation[] subCommands, int db, RedisKey key,
        CommandFlags flags, RedisBase redisBase, out ServerEndPoint? server)
    {
        var eligibleForReadOnly = subCommands.All(x => x.SubCommand == BitFieldSubCommand.Get);
        var features = redisBase.GetFeatures(key, flags, eligibleForReadOnly ? RedisCommand.BITFIELD_RO : RedisCommand.BITFIELD, out server);
        var command = eligibleForReadOnly && features.ReadOnlyBitfield ? RedisCommand.BITFIELD_RO : RedisCommand.BITFIELD;
        return new BitfieldCommandMessage(db, flags, key, command, subCommands.SelectMany(x=>x.EnumerateArgs()).ToArray());
    }

    internal static BitfieldCommandMessage BuildMessage(this BitfieldOperation subCommand, int db, RedisKey key,
        CommandFlags flags, RedisBase redisBase, out ServerEndPoint? server)
    {
        var eligibleForReadOnly = subCommand.SubCommand == BitFieldSubCommand.Get;
        var features = redisBase.GetFeatures(key, flags, eligibleForReadOnly ? RedisCommand.BITFIELD_RO : RedisCommand.BITFIELD, out server);
        var command = eligibleForReadOnly && features.ReadOnlyBitfield ? RedisCommand.BITFIELD_RO : RedisCommand.BITFIELD;
        return new BitfieldCommandMessage(db, flags, key, command, subCommand.EnumerateArgs().ToArray());
    }
}

/// <summary>
/// Bitfield subcommands.
/// </summary>
public enum BitFieldSubCommand
{
    /// <summary>
    /// Subcommand to get the bitfield value.
    /// </summary>
    Get,

    /// <summary>
    /// Subcommand to set the bitfield value.
    /// </summary>
    Set,

    /// <summary>
    /// Subcommand to increment the bitfield value
    /// </summary>
    Increment
}

internal static class BitfieldSubCommandExtensions
{
    internal static RedisValue AsRedisValue(this BitFieldSubCommand subCommand) =>
        subCommand switch
            {
                BitFieldSubCommand.Get => RedisLiterals.GET,
                BitFieldSubCommand.Set => RedisLiterals.SET,
                BitFieldSubCommand.Increment => RedisLiterals.INCRBY,
                _ => throw new ArgumentOutOfRangeException(nameof(subCommand))
            };
}

internal class BitfieldCommandMessage : Message
{
    private readonly IEnumerable<RedisValue> _args;
    private readonly RedisKey _key;
    public BitfieldCommandMessage(int db, CommandFlags flags, RedisKey key, RedisCommand command, RedisValue[] args) : base(db, flags, command)
    {
        _key = key;
        _args = args;
    }

    public override int ArgCount => 1 + _args.Count();

    protected override void WriteImpl(PhysicalConnection physical)
    {
        physical.WriteHeader(Command, ArgCount);
        physical.Write(_key);
        foreach (var arg in _args)
        {
            physical.WriteBulkString(arg);
        }
    }
}
