using System.Collections.Generic;

namespace StackExchange.Redis;

/// <summary>
/// Builder for bitfield commands that take multiple sub-commands.
/// </summary>
public class BitfieldCommand
{
    private readonly LinkedList<RedisValue> _args = new LinkedList<RedisValue>();
    private bool _eligibleForReadOnly;
    private BitfieldEncoding _currentEncoding;

    /// <summary>
    /// Creates a new builder with the specified encoding.
    /// To change encoding between commands, use <see cref="WithEncoding(bool, byte)"/>.
    /// </summary>
    /// <param name="encodingIsSigned">Whether the encoding integer is signed.</param>
    /// <param name="encodingSize">The encoding size.</param>
    public BitfieldCommand(bool encodingIsSigned, byte encodingSize)
    {
        _currentEncoding = new BitfieldEncoding(encodingIsSigned, encodingSize);
    }

    /// <summary>
    /// Builds a subcommand for a Bitfield GET, which returns the number stored in the specified offset of a bitfield at the given encoding.
    /// </summary>
    /// <param name="offset">The offset into the bitfield for the subcommand.</param>
    /// <param name="offsetByEncoding">Whether the offset is by encoding (false means 0-based).</param>
    public BitfieldCommand Get(int offset, bool offsetByEncoding = false)
    {
        _eligibleForReadOnly = true;
        _args.AddLast(RedisLiterals.GET);
        _args.AddLast(_currentEncoding.RedisValue);
        _args.AddLast(new BitfieldOffset(offset, offsetByEncoding).RedisValue);
        return this;
    }

    /// <summary>
    /// Builds a Bitfield subcommand which SETs the specified range of bits to the specified value.
    /// </summary>
    /// <param name="offset">The offset of the subcommand.</param>
    /// <param name="value">The value to set.</param>
    public BitfieldCommand Set(BitfieldOffset offset, long value)
    {
        _eligibleForReadOnly = false;
        _args.AddLast(RedisLiterals.SET);
        _args.AddLast(_currentEncoding.RedisValue);
        _args.AddLast(offset.RedisValue);
        _args.AddLast(value);
        return this;
    }

    /// <summary>
    /// Builds a Bitfield subcommand which SETs the specified range of bits to the specified value.
    /// </summary>
    /// <param name="offset">The number of either bits or encoded integers to offset into the bitfield.</param>
    /// <param name="value">The value to set.</param>
    /// <param name="offsetByEncoding">Whether or not the BitfieldOffset will work off of the sub-commands integer encoding.</param>
    public BitfieldCommand Set(long offset, long value, bool offsetByEncoding = false) =>
        Set(new BitfieldOffset(offset, offsetByEncoding), value);

    /// <summary>
    /// Builds a subcommand for Bitfield INCRBY, which increments the number at the specified range of bits by the provided value
    /// </summary>
    /// <param name="offset">The offset into the bitfield to increment.</param>
    /// <param name="increment">The value to increment by.</param>
    public BitfieldCommand Incrby(BitfieldOffset offset, long increment)
    {
        _eligibleForReadOnly = false;
        _args.AddLast(RedisLiterals.INCRBY);
        _args.AddLast(_currentEncoding.RedisValue);
        _args.AddLast(offset.RedisValue);
        _args.AddLast(increment);
        return this;
    }

    /// <summary>
    /// Builds a subcommand for Bitfield INCRBY, which increments the number at the specified range of bits by the provided value
    /// </summary>
    /// <param name="offset">The number of either bits or encoded integers to offset into the bitfield.</param>
    /// <param name="increment">The value to increment by.</param>
    /// <param name="offsetByEncoding">Whether or not the BitfieldOffset will work off of the sub-commands integer encoding.</param>
    public BitfieldCommand Incrby(long offset, long increment, bool offsetByEncoding = false) =>
        Incrby(new BitfieldOffset(offset, offsetByEncoding), increment);

    /// <summary>
    /// Sets the encoding for all successive subcommands.
    /// </summary>
    /// <param name="isSigned">Whether the encoding integer is signed.</param>
    /// <param name="size">The encoding size.</param>
    public BitfieldCommand WithEncoding(bool isSigned, byte size)
    {
        _currentEncoding = new BitfieldEncoding(isSigned, size);
        return this;
    }

    /// <summary>
    /// Sets the <c>OVERFLOW</c> behavior for all successive <c>INCRBY</c> and <c>SET</c> subcommands.
    /// </summary>
    /// <param name="overflowHandling">How overflows will be handled when incrementing.</param>
    public BitfieldCommand WithOverflow(BitfieldOverflowHandling overflowHandling)
    {
        _args.AddLast(RedisLiterals.OVERFLOW);
        _args.AddLast(overflowHandling.AsRedisValue());
        return this;
    }

    internal BitfieldCommandMessage Build(int db, RedisKey key, CommandFlags flags, RedisBase redisBase, out ServerEndPoint? server)
    {
        var features = redisBase.GetFeatures(key, flags, out server);
        var command = _eligibleForReadOnly && features.ReadOnlyBitfield ? RedisCommand.BITFIELD_RO : RedisCommand.BITFIELD;
        return new BitfieldCommandMessage(db, flags, key, command, _args);
    }
}

internal class BitfieldCommandMessage : Message
{
    private readonly LinkedList<RedisValue> _args;
    private readonly RedisKey _key;
    public BitfieldCommandMessage(int db, CommandFlags flags, RedisKey key, RedisCommand command, LinkedList<RedisValue> args) : base(db, flags, command)
    {
        _key = key;
        _args = args;
    }

    public override int ArgCount => 1 + _args.Count;

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

/// <summary>
/// The encoding that a sub-command should use. This is either a signed or unsigned integer of a specified length.
/// </summary>
public readonly struct BitfieldEncoding
{
    internal RedisValue RedisValue => $"{(IsSigned ? 'i' : 'u')}{Size}";

    /// <summary>
    /// Whether the integer is signed or not.
    /// </summary>
    public bool IsSigned { get; }

    /// <summary>
    /// The size of the integer.
    /// </summary>
    public byte Size { get; }

    /// <summary>
    /// Initializes the BitfieldEncoding.
    /// </summary>
    /// <param name="isSigned">Whether the encoding is signed.</param>
    /// <param name="size">The size of the integer.</param>
    public BitfieldEncoding(bool isSigned, byte size)
    {
        IsSigned = isSigned;
        Size = size;
    }
}

/// <summary>
/// An offset into a bitfield. This is either a literal offset (number of bits from the beginning of the bitfield) or an
/// encoding based offset, based off the encoding of the sub-command.
/// </summary>
public readonly struct BitfieldOffset
{
    /// <summary>
    /// Returns the BitfieldOffset as a RedisValue.
    /// </summary>
    internal RedisValue RedisValue => ByEncoding ? $"#{Offset}" : Offset;

    /// <summary>
    /// The number of either bits or encoded integers to offset into the bitfield.
    /// </summary>
    public long Offset { get; }

    /// <summary>
    /// Whether or not the BitfieldOffset will work off of the sub-commands integer encoding.
    /// </summary>
    public bool ByEncoding { get; }

    /// <summary>
    /// Initializes a bitfield offset
    /// </summary>
    /// <param name="offset">The number of either bits or encoded integers to offset into the bitfield.</param>
    /// <param name="byEncoding">Whether or not the BitfieldOffset will work off of the sub-commands integer encoding.</param>
    public BitfieldOffset(long offset, bool byEncoding = false)
    {
        ByEncoding = byEncoding;
        Offset = offset;
    }
}
