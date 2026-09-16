using System;
using System.Runtime.InteropServices;
using StackExchange.Redis.Interpolated;

namespace StackExchange.Redis;

public abstract partial class VectorSetSimilaritySearchRequest
{
    /// <summary>How many arguments <see cref="WriteTo"/> will write, the key included.</summary>
    internal int ArgCount
        => 1 + SearchTargetArgCount
         + (WithScores ? 1 : 0)
         + (WithAttributes ? 1 : 0)
         + (Count.HasValue ? 2 : 0)
         + (Epsilon.HasValue ? 2 : 0)
         + (SearchExplorationFactor.HasValue ? 2 : 0)
         + (HasFilter ? 2 : 0)
         + (MaxFilteringEffort.HasValue ? 2 : 0)
         + (UseExactSearch ? 1 : 0)
         + (DisableThreading ? 1 : 0);

    private protected abstract int SearchTargetArgCount { get; }

    /// <summary>
    /// Whether a filter is actually going out. The property keeps whatever it was given, but the request
    /// treats blank as absent - so an empty string is NOT a filter that matches everything, it is no
    /// filter at all, and writing one would be a syntax error at the server.
    /// </summary>
    private bool HasFilter => !string.IsNullOrWhiteSpace(FilterExpression);

    /// <summary>
    /// Write the whole <c>VSIM</c> request to a frame, for the interpolated surface.
    /// </summary>
    /// <param name="command">The frame being written.</param>
    /// <param name="key">The key to search.</param>
    /// <remarks><inheritdoc cref="VectorSetAddRequest.WriteTo" path="/remarks"/></remarks>
    internal void WriteTo(scoped ref RespRequestBuilder command, in RedisKey key)
    {
        command.AppendFormatted(key);
        WriteSearchTarget(ref command);

        if (WithScores) command.AppendFormatted(RespLiterals.WithScores);
        if (WithAttributes) command.AppendFormatted(RespLiterals.WithAttribs);

        if (Count is { } count)
        {
            command.AppendFormatted(RespLiterals.Count);
            command.AppendFormatted((RedisValue)count);
        }

        if (Epsilon is { } epsilon)
        {
            command.AppendFormatted(RespLiterals.Epsilon);
            command.AppendFormatted((RedisValue)epsilon);
        }

        if (SearchExplorationFactor is { } searchEffort)
        {
            command.AppendFormatted(RespLiterals.Ef);
            command.AppendFormatted((RedisValue)searchEffort);
        }

        if (HasFilter)
        {
            command.AppendFormatted(RespLiterals.Filter);
            command.AppendFormatted(FilterExpression!.AsRedisValue());
        }

        if (MaxFilteringEffort is { } filterEffort)
        {
            command.AppendFormatted(RespLiterals.FilterEf);
            command.AppendFormatted((RedisValue)filterEffort);
        }

        if (UseExactSearch) command.AppendFormatted(RespLiterals.Truth);
        if (DisableThreading) command.AppendFormatted(RespLiterals.NoThread);
    }

    private protected abstract void WriteSearchTarget(scoped ref RespRequestBuilder command);

    private sealed partial class VectorSetSimilarityByMemberSearchRequest
    {
        private protected override int SearchTargetArgCount => 2; // ELE {member}

        private protected override void WriteSearchTarget(scoped ref RespRequestBuilder command)
        {
            command.AppendFormatted(RespLiterals.Ele);
            command.AppendFormatted(_member);
        }
    }

    private sealed partial class VectorSetSimilarityVectorSingleSearchRequest
    {
        private bool Fp32 => UseFp32 & VectorSetAddMessage.CanUseFp32;

        private protected override int SearchTargetArgCount => Fp32 ? 2 : 2 + _vector.Length;

        /// <remarks><inheritdoc cref="VectorSetAddRequest.WriteVector" path="/remarks"/></remarks>
        private protected override void WriteSearchTarget(scoped ref RespRequestBuilder command)
        {
            if (Fp32)
            {
                command.AppendFormatted(RespLiterals.Fp32);
                command.AppendBulk(MemoryMarshal.AsBytes(_vector.Span));
            }
            else
            {
                command.AppendFormatted(RespLiterals.Values);
                command.AppendFormatted((RedisValue)_vector.Length);
                foreach (var value in _vector.Span)
                {
                    command.AppendFormatted((RedisValue)value);
                }
            }
        }
    }
}
