using System;
using System.Runtime.InteropServices;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis;

public abstract partial class VectorSetAddRequest
{
    /// <summary>How many arguments <see cref="WriteTo"/> will write, the key included.</summary>
    internal abstract int ArgCount { get; }

    /// <summary>
    /// Write the whole <c>VADD</c> request to a frame, for the interpolated surface.
    /// </summary>
    /// <param name="command">The frame being written.</param>
    /// <param name="key">The key to add to.</param>
    /// <remarks>
    /// The same grammar as <c>VectorSetAddMessage</c> writes, in the same order, against the other
    /// writer - as <c>GeoSearchShape</c> does. Both are internal abstract, so the type is effectively
    /// sealed to this assembly and neither can be added to without the other.
    /// </remarks>
    internal abstract void WriteTo(scoped ref RespRequestBuilder command, in RedisKey key);

    /// <summary>The operands common to every element shape, either side of the element itself.</summary>
    private protected void WriteOptions(scoped ref RespRequestBuilder command, bool before)
    {
        if (before)
        {
            if (ReducedDimensions is { } reduce)
            {
                command.AppendFormatted(RespLiterals.Reduce);
                command.AppendFormatted((RedisValue)reduce);
            }

            return;
        }

        if (UseCheckAndSet) command.AppendFormatted(RespLiterals.Cas);

        switch (Quantization)
        {
            case VectorSetQuantization.Int8:
                break; // the server's default, so nothing to say
            case VectorSetQuantization.None:
                command.AppendFormatted(RespLiterals.NoQuant);
                break;
            case VectorSetQuantization.Binary:
                command.AppendFormatted(RespLiterals.Bin);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(Quantization));
        }

        if (BuildExplorationFactor is { } ef)
        {
            command.AppendFormatted(RespLiterals.Ef);
            command.AppendFormatted((RedisValue)ef);
        }
    }

    /// <summary>The trailing <c>M</c> operand, which comes after the attributes.</summary>
    private protected void WriteMaxConnections(scoped ref RespRequestBuilder command)
    {
        if (MaxConnections is { } max)
        {
            command.AppendFormatted(RespLiterals.M);
            command.AppendFormatted((RedisValue)max);
        }
    }

    /// <summary>How many arguments the shared operands account for.</summary>
    private protected int OptionArgCount
        => (ReducedDimensions.HasValue ? 2 : 0)
         + (UseCheckAndSet ? 1 : 0)
         + Quantization switch
         {
             VectorSetQuantization.None or VectorSetQuantization.Binary => 1,
             VectorSetQuantization.Int8 => 0,
             _ => throw new ArgumentOutOfRangeException(nameof(Quantization)),
         }
         + (BuildExplorationFactor.HasValue ? 2 : 0)
         + (MaxConnections.HasValue ? 2 : 0);

    /// <summary>
    /// Write a vector as <c>FP32 &lt;bytes&gt;</c> where the platform allows it, else as
    /// <c>VALUES n v1..vn</c>.
    /// </summary>
    /// <remarks>
    /// <c>FP32</c> is one bulk string of raw little-endian floats rather than a token each, so it is both
    /// smaller and exact - but only where the machine agrees with the wire about endianness, which is
    /// what <c>CanUseFp32</c> settles once at startup.
    /// </remarks>
    private protected static void WriteVector(scoped ref RespRequestBuilder command, ReadOnlyMemory<float> values, bool useFp32)
    {
        if (useFp32)
        {
            command.AppendFormatted(RespLiterals.Fp32);
            command.AppendBulk(MemoryMarshal.AsBytes(values.Span));
        }
        else
        {
            command.AppendFormatted(RespLiterals.Values);
            command.AppendFormatted((RedisValue)values.Length);
            foreach (var value in values.Span)
            {
                command.AppendFormatted((RedisValue)value);
            }
        }
    }

    private sealed partial class VectorSetAddMemberRequest
    {
        private bool Fp32 => UseFp32 & VectorSetAddMessage.CanUseFp32;

        private string? Attributes => string.IsNullOrWhiteSpace(_attributesJson) ? null : _attributesJson;

        internal override int ArgCount
            => 2 // key and element
             + 2 // FP32 {vector} or VALUES {num}
             + (Fp32 ? 0 : _values.Length)
             + (Attributes is null ? 0 : 2)
             + OptionArgCount;

        internal override void WriteTo(scoped ref RespRequestBuilder command, in RedisKey key)
        {
            command.AppendFormatted(key);
            WriteOptions(ref command, before: true);

            // the element comes AFTER its vector, which is the one bit of this grammar that reads
            // backwards from how the method is called
            WriteVector(ref command, _values, Fp32);
            command.AppendFormatted(_element);

            WriteOptions(ref command, before: false);

            if (Attributes is { } attributes)
            {
                command.AppendFormatted(RespLiterals.SetAttr);
                command.AppendFormatted(attributes.AsRedisValue());
            }

            WriteMaxConnections(ref command);
        }
    }
}
