using System;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis;

public readonly partial struct BitFieldOperation
{
    /// <summary>
    /// The already-framed sub-command token - <c>GET</c>, <c>SET</c>, <c>INCRBY</c> - or empty for a
    /// default operation, which is not a valid one.
    /// </summary>
    /// <remarks>
    /// Shared by both writers - the <c>MessageWriter</c> path and the interpolated one - rather than
    /// restated in each; the same arrangement <see cref="Expiration"/> uses, and for the same reason.
    /// </remarks>
    internal ReadOnlySpan<byte> KindResp => _kind switch
    {
        OperationKind.Get => "$3\r\nGET\r\n"u8,
        OperationKind.Set => "$3\r\nSET\r\n"u8,
        OperationKind.IncrementBy => "$6\r\nINCRBY\r\n"u8,
        _ => default,
    };

    /// <summary>
    /// The already-framed <c>OVERFLOW &lt;mode&gt;</c> pair - two arguments, emitted only when the sticky
    /// mode changes.
    /// </summary>
    /// <remarks>
    /// <c>WRAP</c> for anything unrecognised: the argument count comes from the transition rather than
    /// from the mode, so the shape is the same either way, and the server's own default is the safe answer.
    /// </remarks>
    internal static ReadOnlySpan<byte> OverflowResp(BitFieldOverflow overflow) => overflow switch
    {
        BitFieldOverflow.Saturate => "$8\r\nOVERFLOW\r\n$3\r\nSAT\r\n"u8,
        BitFieldOverflow.Fail => "$8\r\nOVERFLOW\r\n$4\r\nFAIL\r\n"u8,
        _ => "$8\r\nOVERFLOW\r\n$4\r\nWRAP\r\n"u8,
    };

    /// <summary>
    /// Write this operation into a command being composed, emitting the <c>OVERFLOW</c> pair only when
    /// the sticky mode changes.
    /// </summary>
    /// <param name="handler">The command being written.</param>
    /// <param name="overflow">The mode currently in force; updated when this operation changes it.</param>
    /// <remarks>
    /// <b>Not an <see cref="IRespArgument"/>, and it cannot be one</b>: the <c>OVERFLOW</c> mode is sticky
    /// across the operations of one <c>BITFIELD</c>, so an operation does not know what it has to write
    /// until it is told what the previous one left in force. An interface whose method takes only the
    /// handler has nowhere to put that, which is why this is a plain internal method and why the group
    /// method loops rather than passing a span into a hole.
    /// </remarks>
    internal void WriteTo(scoped ref RespRequestBuilder handler, ref BitFieldOverflow overflow)
    {
        if (_kind != OperationKind.Get && Overflow != overflow)
        {
            overflow = Overflow;
#pragma warning disable SER011 // pre-framed constants owned by this type; see Expiration for the reasoning
            handler.AppendFormatted(new RespFragment(OverflowResp(overflow), argCount: 2));
#pragma warning restore SER011
        }

        var kind = KindResp;
        if (kind.IsEmpty)
        {
            // unreachable: the caller counts the operations before anything is written. Guessing here
            // would be worse than failing - a wrong sub-command corrupts data, one of the wrong arity
            // corrupts the connection
            throw new InvalidOperationException($"A default {nameof(BitFieldOperation)} is not a valid operation.");
        }

#pragma warning disable SER011 // as above
        handler.AppendFormatted(new RespFragment(kind));
#pragma warning restore SER011
        Encoding.WriteTo(ref handler);
        Offset.WriteTo(ref handler);
        if (_kind != OperationKind.Get) handler.AppendFormatted(Value);
    }

    /// <summary>How many RESP arguments a run of operations writes, including the key.</summary>
    /// <param name="operations">The operations to count.</param>
    /// <param name="paramName">The caller's parameter name, for the exception.</param>
    internal static int CountArgs(ReadOnlySpan<BitFieldOperation> operations, string paramName)
    {
        var count = 1; // the key
        var overflow = BitFieldOverflow.Wrap;
        foreach (ref readonly var op in operations)
        {
            count += op.CountArgs(ref overflow, paramName);
        }

        return count;
    }

    /// <inheritdoc cref="CountArgs(ReadOnlySpan{BitFieldOperation}, string)"/>
    internal static int CountArgs(in BitFieldOperation operation, string paramName)
    {
        var overflow = BitFieldOverflow.Wrap;
        return 1 + operation.CountArgs(ref overflow, paramName); // the key, plus this operation
    }

    /// <summary>How many arguments this one operation writes, given the mode currently in force.</summary>
    private int CountArgs(ref BitFieldOverflow overflow, string paramName)
    {
        switch (_kind)
        {
            case OperationKind.Get:
                return 3; // GET, encoding, offset
            case OperationKind.Set:
            case OperationKind.IncrementBy:
                var count = 4; // SET/INCRBY, encoding, offset, value
                if (Overflow != overflow)
                {
                    overflow = Overflow;
                    count += 2; // OVERFLOW, mode
                }

                return count;
            default:
                throw new ArgumentException($"A default {nameof(BitFieldOperation)} is not a valid operation.", paramName);
        }
    }
}

public readonly partial struct BitFieldEncoding
{
    /// <summary>Write this encoding as one bulk string - <c>i8</c>, <c>u63</c> - into a command being composed.</summary>
    /// <param name="handler">The command being written.</param>
    internal void WriteTo(scoped ref RespRequestBuilder handler)
    {
        if (IsDefault)
        {
            throw new ArgumentException(
                $"A {nameof(BitFieldEncoding)} must be created via {nameof(Signed)}, {nameof(Unsigned)}, or one of the named encodings.",
                nameof(BitFieldEncoding));
        }

        // payload only: the handler frames what it is given, unlike the MessageWriter path, which is
        // handed bytes that already carry their own $len
        Span<byte> payload = stackalloc byte[3]; // sign, plus at most two digits: widths run 1-64
        int width = Width, len = 1;
        payload[0] = IsSigned ? (byte)'i' : (byte)'u';
        if (width >= 10)
        {
            payload[len++] = (byte)('0' + (width / 10));
            payload[len++] = (byte)('0' + (width % 10));
        }
        else
        {
            payload[len++] = (byte)('0' + width);
        }

        handler.AppendBulk(payload.Slice(0, len));
    }
}

public readonly partial struct BitFieldOffset
{
    /// <summary>Write this offset - a bit position, or the <c>#</c> element form - into a command being composed.</summary>
    /// <param name="handler">The command being written.</param>
    internal void WriteTo(scoped ref RespRequestBuilder handler)
    {
        if (!_isElement)
        {
            handler.AppendFormatted(_value);
            return;
        }

        Span<byte> payload = stackalloc byte[Format.MaxInt64TextLen + 1];
        payload[0] = (byte)'#';
        var len = Format.FormatInt64(_value, payload.Slice(1)) + 1;
        handler.AppendBulk(payload.Slice(0, len));
    }
}
