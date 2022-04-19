using System;
using System.Collections.Generic;

namespace StackExchange.Redis;

/// <summary>
/// A subcommand for a bitfield.
/// </summary>
public abstract class BitfieldSubCommand
{
    internal abstract int NumArgs { get; }
    internal abstract void AddArgs(IList<RedisValue> args);
    internal virtual bool IsReadonly => false;
    /// <summary>
    /// The encoding of the sub-command. This might be a signed or unsigned integer.
    /// </summary>
    public BitfieldEncoding Encoding { get; }
    /// <summary>
    /// The offset into the bitfield the subcommand will traverse.
    /// </summary>
    public BitfieldOffset Offset { get; }

    internal BitfieldSubCommand(BitfieldEncoding encoding, BitfieldOffset offset)
    {
        Encoding = encoding;
        Offset = offset;
    }

    internal BitfieldSubCommand(string encoding, string offset)
    {
        Encoding = BitfieldEncoding.Parse(encoding);
        Offset = BitfieldOffset.Parse(offset);
    }

}

/// <summary>
/// Represents a Bitfield GET, which returns the specified bitfield.
/// </summary>
public sealed class BitfieldGet : BitfieldSubCommand
{
    /// <summary>
    /// Initializes a bitfield get subcommand
    /// </summary>
    /// <param name="encoding">the encoding of the subcommand.</param>
    /// <param name="offset">The offset into the bitfield of the subcommand</param>
    public BitfieldGet(BitfieldEncoding encoding, BitfieldOffset offset) : base(encoding, offset)
    {
    }

    /// <summary>
    /// Initializes a bitfield get subcommand
    /// </summary>
    /// <param name="encoding">the encoding of the subcommand.</param>
    /// <param name="offset">The offset into the bitfield of the subcommand</param>
    public BitfieldGet(string encoding, string offset) : base(encoding, offset)
    {
    }

    internal override bool IsReadonly => true;

    internal override int NumArgs => 3;

    internal override void AddArgs(IList<RedisValue> args)
    {
        args.Add(RedisLiterals.GET);
        args.Add(Encoding.AsRedisValue);
        args.Add(Offset.AsRedisValue);
    }
}

/// <summary>
/// Bitfield sub-command which set's the specified range of bits to the specified value.
/// </summary>
public sealed class BitfieldSet : BitfieldSubCommand
{
    /// <summary>
    /// The value to set.
    /// </summary>
    public long Value { get; }

    /// <summary>
    /// Initializes a sub-command for a Bitfield Set.
    /// </summary>
    /// <param name="encoding">The number's encoding.</param>
    /// <param name="offset">The offset into the bitfield to set.</param>
    /// <param name="value">The value to set.</param>
    public BitfieldSet(BitfieldEncoding encoding, BitfieldOffset offset, long value) : base(encoding, offset)
    {
        Value = value;
    }

    /// <summary>
    /// Initializes a sub-command for a Bitfield Set.
    /// </summary>
    /// <param name="encoding">The number's encoding.</param>
    /// <param name="offset">The offset into the bitfield to set.</param>
    /// <param name="value">The value to set.</param>
    public BitfieldSet(string encoding, string offset, long value) : base(encoding, offset)
    {
        Value = value;
    }

    internal override int NumArgs => 4;

    internal override void AddArgs(IList<RedisValue> args)
    {
        args.Add(RedisLiterals.SET);
        args.Add(Encoding.AsRedisValue);
        args.Add(Offset.AsRedisValue);
        args.Add(Value);
    }
}

/// <summary>
/// Bitfield sub-command which increments the number at the specified range of bits by the provided value
/// </summary>
public sealed class BitfieldIncrby : BitfieldSubCommand
{
    /// <summary>
    /// The value to set.
    /// </summary>
    public long Increment { get; }

    /// <summary>
    /// Determines how overflows are handled for the bitfield.
    /// </summary>
    public BitfieldOverflowHandling OverflowHandling { get; }

    /// <summary>
    /// Initializes a sub-command for a Bitfield Set.
    /// </summary>
    /// <param name="encoding">The number's encoding.</param>
    /// <param name="offset">The offset into the bitfield to set.</param>
    /// <param name="increment">The value to set.</param>
    /// <param name="overflowHandling">How overflows will be handled when incrementing.</param>
    public BitfieldIncrby(BitfieldEncoding encoding, BitfieldOffset offset, long increment, BitfieldOverflowHandling overflowHandling = BitfieldOverflowHandling.Wrap) : base(encoding, offset)
    {
        Increment = increment;
        OverflowHandling = overflowHandling;
    }

    /// <summary>
    /// Initializes a sub-command for a Bitfield Set.
    /// </summary>
    /// <param name="encoding">The number's encoding.</param>
    /// <param name="offset">The offset into the bitfield to set.</param>
    /// <param name="increment">The value to set.</param>
    public BitfieldIncrby(string encoding, string offset, long increment) : base(encoding, offset)
    {
        Increment = increment;
    }

    internal override int NumArgs => OverflowHandling == BitfieldOverflowHandling.Wrap ? 4 : 6;

    internal override void AddArgs(IList<RedisValue> args)
    {
        if (OverflowHandling != BitfieldOverflowHandling.Wrap)
        {
            args.Add(RedisLiterals.OVERFLOW);
            args.Add(OverflowHandling.AsRedisValue());
        }
        args.Add(RedisLiterals.INCRBY);
        args.Add(Encoding.AsRedisValue);
        args.Add(Offset.AsRedisValue);
        args.Add(Increment);
    }
}



/// <summary>
/// An offset into a bitfield. This is either a literal offset (number of bits from the beginning of the bitfield) or an
/// encoding based offset, based off the encoding of the sub-command.
/// </summary>
public readonly struct BitfieldOffset
{
    /// <summary>
    /// Returns the BitfieldOffset as a RedisValue
    /// </summary>
    internal RedisValue AsRedisValue => $"{(ByEncoding ? "#" : string.Empty)}{Offset}";

    /// <summary>
    /// Whether or not the BitfieldOffset will work off of the sub-commands integer encoding.
    /// </summary>
    public bool ByEncoding { get; }

    /// <summary>
    /// The number of either bits or encoded integers to offset into the bitfield.
    /// </summary>
    public long Offset { get; }

    /// <summary>
    /// Initializes a bitfield offset
    /// </summary>
    /// <param name="byEncoding">Whether or not the BitfieldOffset will work off of the sub-commands integer encoding.</param>
    /// <param name="offset">The number of either bits or encoded integers to offset into the bitfield.</param>
    public BitfieldOffset(bool byEncoding, long offset)
    {
        ByEncoding = byEncoding;
        Offset = offset;
    }

    internal static BitfieldOffset Parse(string str)
    {
        if (str.IsNullOrEmpty())
        {
            throw new ArgumentException($"Cannot parse {nameof(BitfieldOffset)} from an empty or null string.", nameof(str));
        }

        long offset;

        if (str[0] == '#')
        {
            if (long.TryParse(str.Substring(1), out offset))
            {
                return new BitfieldOffset(true, offset);
            }
        }
        else
        {
            if (long.TryParse(str, out offset))
            {
                return new BitfieldOffset(false, offset);
            }
        }

        throw new ArgumentException($"{str} could not be parsed into a {nameof(BitfieldOffset)}.", nameof(str));
    }
}

/// <summary>
/// The encoding that a sub-command should use. This is either a signed or unsigned integer.
/// </summary>
public readonly struct BitfieldEncoding
{
    internal RedisValue AsRedisValue => $"{Signedness.SignChar()}{Size}";
    /// <summary>
    /// The signedness of the integer.
    /// </summary>
    public Signedness Signedness { get; }
    /// <summary>
    /// The size of the integer.
    /// </summary>
    public byte Size { get; }

    /// <summary>
    /// Initializes the BitfieldEncoding.
    /// </summary>
    /// <param name="signedness">The encoding's <see cref="Signedness"/></param>
    /// <param name="size">The size of the integer.</param>
    public BitfieldEncoding(Signedness signedness, byte size)
    {
        Signedness = signedness;
        Size = size;
    }

    internal static BitfieldEncoding Parse(string str)
    {
        if (str.IsNullOrEmpty())
        {
            throw new ArgumentException($"Cannot parse {nameof(BitfieldEncoding)} from an empty or null String", nameof(str));
        }

        if (!byte.TryParse(str.Substring(1), out byte size))
        {
            throw new ArgumentException($"Could not parse {nameof(BitfieldEncoding)} from {str}", nameof(str));
        }

        if (char.ToLowerInvariant('i') == char.ToLowerInvariant(str[0]))
        {
            return new BitfieldEncoding(Signedness.Signed, size);
        }

        if (char.ToLowerInvariant('u') == char.ToLowerInvariant(str[0]))
        {
            return new BitfieldEncoding(Signedness.Unsigned, size);
        }

        throw new ArgumentException($"Could not parse {nameof(BitfieldEncoding)} from {str}", nameof(str));
    }
}
