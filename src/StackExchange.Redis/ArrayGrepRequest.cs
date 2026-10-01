using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using RESPite;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis;

/// <summary>
/// Describes an array grep operation.
/// </summary>
public class ArrayGrepRequest
{
    [Flags]
    private enum LocalFlags : byte
    {
        None = 0,
        IsFrozen = 1 << 0,
        CaseInsensitive = 1 << 1,
        IsIntersection = 1 << 2,
        StartSpecified = 1 << 3,
        EndSpecified = 1 << 4,
        LimitSpecified = 1 << 5,
        IncludeValues = 1 << 6,
        Reversed = 1 << 7,
        // warning: next flag needs : ushort
    }

    private void Freeze() => _flags |= LocalFlags.IsFrozen;

    private void ThrowIfFrozen()
    {
        if (GetFlag(LocalFlags.IsFrozen)) Throw();
        static void Throw() => throw new InvalidOperationException("Cannot modify a frozen request");
    }

    private LocalFlags _flags;
    private bool GetFlag(LocalFlags flag) => (_flags & flag) != 0;

    private void SetFlag(LocalFlags flag, bool value)
    {
        if (GetFlag(flag) == value) return;

        ThrowIfFrozen();
        if (value)
        {
            _flags |= flag;
        }
        else
        {
            _flags &= ~flag;
        }
    }

    private RedisArrayIndex _start, _end;

    /// <summary>
    /// The start index for the search, or <see langword="null"/> to use the server's open-ended lower bound.
    /// </summary>
    public RedisArrayIndex? Start
    {
        get => GetFlag(LocalFlags.StartSpecified) ? _start : null;
        set
        {
            if (value.HasValue)
            {
                var newValue = value.GetValueOrDefault();
                if (!GetFlag(LocalFlags.StartSpecified) || _start != newValue)
                {
                    ThrowIfFrozen();
                    _start = newValue;
                }
                SetFlag(LocalFlags.StartSpecified, true);
            }
            else
            {
                SetFlag(LocalFlags.StartSpecified, false);
            }
        }
    }

    /// <summary>
    /// The end index for the search, or <see langword="null"/> to use the server's open-ended upper bound.
    /// </summary>
    public RedisArrayIndex? End
    {
        get => GetFlag(LocalFlags.EndSpecified) ? _end : null;
        set
        {
            if (value.HasValue)
            {
                var newValue = value.GetValueOrDefault();
                if (!GetFlag(LocalFlags.EndSpecified) || _end != newValue)
                {
                    ThrowIfFrozen();
                    _end = newValue;
                }
                SetFlag(LocalFlags.EndSpecified, true);
            }
            else
            {
                SetFlag(LocalFlags.EndSpecified, false);
            }
        }
    }

    /// <summary>
    /// When specified, provide an upper bound to the matches returned.
    /// </summary>
    /// <remarks>Corresponds to the <c>LIMIT</c> parameter.</remarks>
    public long? Limit
    {
        get => GetFlag(LocalFlags.LimitSpecified) ? _limit : null;
        set
        {
            if (value.HasValue)
            {
                var newValue = value.GetValueOrDefault();
                if (!GetFlag(LocalFlags.LimitSpecified) || _limit != newValue)
                {
                    ThrowIfFrozen();
                    _limit = newValue;
                }
                SetFlag(LocalFlags.LimitSpecified, true);
            }
            else
            {
                SetFlag(LocalFlags.LimitSpecified, false);
            }
        }
    }

    private long _limit;

    /// <summary>
    /// Indicates whether matches are performed in a case-sensitive manner.
    /// </summary>
    /// <remarks>Corresponds to the <c>NOCASE</c> parameter.</remarks>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("Prefer " + nameof(IsCaseInsensitive))]
    public bool IsCaseSensitive
    {
        get => !IsCaseInsensitive;
        set => IsCaseInsensitive = !value;
    }

    /// <summary>
    /// Indicates whether matches are performed in a case-insensitive manner.
    /// </summary>
    /// <remarks>Corresponds to the <c>NOCASE</c> parameter.</remarks>
    public bool IsCaseInsensitive
    {
        get => GetFlag(LocalFlags.CaseInsensitive);
        set => SetFlag(LocalFlags.CaseInsensitive, value);
    }

    /// <summary>
    /// Indicates whether the query order should be reversed; this is equivalent to
    /// reversing the order of <see cref="Start"/> and <see cref="End"/>.
    /// </summary>
    /// <remarks>Corresponds to the <c>NOCASE</c> parameter.</remarks>
    public bool IsReversed
    {
        get => GetFlag(LocalFlags.Reversed);
        set => SetFlag(LocalFlags.Reversed, value);
    }

    /// <summary>
    /// When multiple predicates are provided, this indicates whether they should be combined with a logical <c>AND</c> (true) or <c>OR</c> (false).
    /// </summary>
    /// <remarks>Corresponds to the <c>AND</c>/<c>OR</c> parameter.</remarks>
    public bool IsIntersection
    {
        get => GetFlag(LocalFlags.IsIntersection);
        set => SetFlag(LocalFlags.IsIntersection, value);
    }

    /// <summary>
    /// Indicates whether to fetch values as part of the query.
    /// </summary>
    /// <remarks>Corresponds to the <c>WITHVALUES</c> parameter.</remarks>
    public bool IncludeValues
    {
        get => GetFlag(LocalFlags.IncludeValues);
        set => SetFlag(LocalFlags.IncludeValues, value);
    }

    private object? _predicates;

    /// <summary>
    /// Gets the predicate at the specified index.
    /// </summary>
    /// <param name="index">The predicate index.</param>
    public Predicate this[int index]
    {
        get
        {
            return _predicates switch
            {
                Predicate p when index is 0 => p,
                List<Predicate> list => list[index],
                _ => Throw(),
            };

            static Predicate Throw() => throw new IndexOutOfRangeException();
        }
    }

    /// <summary>
    /// The number of predicates in this request.
    /// </summary>
    public int Count => _predicates switch
    {
        null => 0,
        Predicate p => 1,
        List<Predicate> list => list.Count,
        _ => 0,
    };

    /// <summary>
    /// Adds a predicate to this request.
    /// </summary>
    /// <param name="predicate">The predicate to add.</param>
    public void AddPredicate(Predicate predicate)
    {
        ThrowIfFrozen();
        switch (_predicates)
        {
            case null:
                _predicates = predicate;
                break;
            case Predicate existing:
                _predicates = new List<Predicate> { existing, predicate };
                break;
            default:
                ((List<Predicate>)_predicates).Add(predicate);
                break;
        }
    }

    internal Message CreateMessage(int db, RedisKey key, CommandFlags flags)
    {
        Freeze();
        return new ArrayGrepMessage(db, key, this, flags);
    }

    /// <summary>
    /// Describes a predicate used by an array grep operation.
    /// </summary>
    public abstract class Predicate
    {
        internal virtual int ArgCount => 2;
        internal abstract void WriteTo(in MessageWriter writer);

        /// <summary>The same predicate, written through the interpolated builder.</summary>
        /// <remarks>
        /// A second spelling rather than a shared one, because the two writers have no common interface -
        /// <see cref="MessageWriter"/> is a class the classic pipeline owns and
        /// <see cref="RespRequestBuilder"/> is a <c>ref struct</c>. Two lines each, and
        /// <c>RespSurfaceArraysParityTests</c> is what keeps them agreeing.
        /// </remarks>
        internal abstract void WriteTo(scoped ref RespRequestBuilder handler);

        private protected Predicate() { }

        /// <summary>
        /// Creates an exact-value predicate.
        /// </summary>
        /// <param name="value">The value to match.</param>
        public static Predicate Exact(RedisValue value) => new ExactPredicate(value);

        /// <summary>
        /// Creates a pattern-match predicate.
        /// </summary>
        /// <param name="value">The pattern to match.</param>
        public static Predicate Match(string value) => new MatchPredicate(value);

        /// <summary>
        /// Creates a glob predicate.
        /// </summary>
        /// <param name="value">The glob pattern to match.</param>
        public static Predicate Glob(string value) => new GlobPredicate(value);

        /// <summary>
        /// Creates a regular expression predicate.
        /// </summary>
        /// <param name="value">The regular expression to match.</param>
        public static Predicate Regex(
            #if NET7_0_OR_GREATER
            [StringSyntax(StringSyntaxAttribute.Regex)]
            #endif
            string value) => new RegexPredicate(value);

        private sealed class ExactPredicate(RedisValue value) : Predicate
        {
            public override string ToString() => $"EXACT '{value}'";

            internal override void WriteTo(in MessageWriter writer)
            {
                writer.WriteRaw("$5\r\nEXACT\r\n"u8);
                writer.WriteBulkString(value);
            }

            internal override void WriteTo(scoped ref RespRequestBuilder handler)
            {
                handler.AppendFormatted(RespLiterals.Exact);
                handler.AppendFormatted(value);
            }
        }

        private sealed class MatchPredicate(string pattern) : Predicate
        {
            public override string ToString() => $"MATCH '{pattern}'";

            internal override void WriteTo(in MessageWriter writer)
            {
                writer.WriteRaw("$5\r\nMATCH\r\n"u8);
                writer.WriteBulkString(pattern);
            }

            internal override void WriteTo(scoped ref RespRequestBuilder handler)
            {
                handler.AppendFormatted(RespLiterals.Match);
                handler.AppendFormatted(pattern);
            }
        }

        private sealed class GlobPredicate(string pattern) : Predicate
        {
            public override string ToString() => $"GLOB '{pattern}'";

            internal override void WriteTo(in MessageWriter writer)
            {
                writer.WriteRaw("$4\r\nGLOB\r\n"u8);
                writer.WriteBulkString(pattern);
            }

            internal override void WriteTo(scoped ref RespRequestBuilder handler)
            {
                handler.AppendFormatted(RespLiterals.Glob);
                handler.AppendFormatted(pattern);
            }
        }

        private sealed class RegexPredicate(string re) : Predicate
        {
            public override string ToString() => $"RE '{re}'";

            internal override void WriteTo(in MessageWriter writer)
            {
                writer.WriteRaw("$2\r\nRE\r\n"u8);
                writer.WriteBulkString(re);
            }

            internal override void WriteTo(scoped ref RespRequestBuilder handler)
            {
                handler.AppendFormatted(RespLiterals.Re);
                handler.AppendFormatted(re);
            }
        }
    }

    /// <summary>How many arguments this request renders, including the key.</summary>
    /// <remarks>
    /// On the request rather than on the message, because both writers need it: the classic one for its
    /// header and the interpolated one to size the buffer. One count means the two cannot disagree about
    /// how many tokens they are about to write, which leaves only their order to drift.
    /// </remarks>
    internal int ArgCount
    {
        get
        {
            var count = 3; // key, start, end
            var pCount = Count;
            for (int i = 0; i < pCount; i++)
            {
                count += this[i].ArgCount;
            }

            if (IsIntersection) count++;
            if (IsCaseInsensitive) count++;
            if (IncludeValues) count++;
            if (Limit.HasValue) count += 2;
            return count;
        }
    }

    /// <summary>Write everything after the key, through the interpolated builder.</summary>
    /// <remarks>
    /// <b>The token order is <see cref="ArrayGrepMessage.WriteImpl"/>'s, and must stay so</b>: the bounds
    /// swap when the request is reversed, the predicates follow in the order they were added, and the
    /// three switches then <c>LIMIT</c> come last. <c>ArgCount</c> is shared between the two writers, so
    /// only the order can drift - which is what the parity test compares.
    /// </remarks>
    internal void WriteTail(scoped ref RespRequestBuilder handler)
    {
        if (IsReversed)
        {
            WriteIndex(ref handler, End, isStart: false);
            WriteIndex(ref handler, Start, isStart: true);
        }
        else
        {
            WriteIndex(ref handler, Start, isStart: true);
            WriteIndex(ref handler, End, isStart: false);
        }

        var count = Count;
        for (var i = 0; i < count; i++)
        {
            this[i].WriteTo(ref handler);
        }

        if (IsIntersection) handler.AppendFormatted(RespLiterals.And);
        if (IsCaseInsensitive) handler.AppendFormatted(RespLiterals.NoCase);
        if (IncludeValues) handler.AppendFormatted(RespLiterals.WithValues);

        var limit = Limit;
        if (limit.HasValue)
        {
            handler.AppendFormatted(RespLiterals.Limit);
            handler.AppendFormatted(limit.GetValueOrDefault());
        }

        static void WriteIndex(scoped ref RespRequestBuilder handler, RedisArrayIndex? index, bool isStart)
        {
            if (index.HasValue)
            {
                handler.AppendFormatted(index.GetValueOrDefault());
            }
            else
            {
                handler.AppendFormatted(isStart ? RespLiterals.RangeStart : RespLiterals.RangeEnd);
            }
        }
    }

    private sealed class ArrayGrepMessage(int db, RedisKey key, ArrayGrepRequest request, CommandFlags flags)
        : Message(db, flags, RedisCommand.ARGREP)
    {
        public override int ArgCount => request.ArgCount;

        private static void AddIndex(in MessageWriter writer, RedisArrayIndex? index, ReadOnlySpan<byte> fallback)
        {
            if (index.HasValue)
            {
                writer.WriteBulkString(index.GetValueOrDefault().Value);
            }
            else
            {
                writer.WriteRaw(fallback);
            }
        }

        protected override void WriteImpl(in MessageWriter writer)
        {
            writer.WriteHeader(Command, ArgCount);
            writer.Write(key);
            if (request.IsReversed)
            {
                AddIndex(writer, request.End, "$1\r\n+\r\n"u8);
                AddIndex(writer, request.Start, "$1\r\n-\r\n"u8);
            }
            else
            {
                AddIndex(writer, request.Start, "$1\r\n-\r\n"u8);
                AddIndex(writer, request.End, "$1\r\n+\r\n"u8);
            }
            var pCount = request.Count;
            for (int i = 0; i < pCount; i++)
            {
                request[i].WriteTo(in writer);
            }

            if (request.IsIntersection) writer.WriteRaw("$3\r\nAND\r\n"u8);
            if (request.IsCaseInsensitive) writer.WriteRaw("$6\r\nNOCASE\r\n"u8);
            if (request.IncludeValues) writer.WriteRaw("$10\r\nWITHVALUES\r\n"u8);
            var limit = request.Limit;
            if (limit.HasValue)
            {
                writer.WriteRaw("$5\r\nLIMIT\r\n"u8);
                writer.WriteBulkString(limit.GetValueOrDefault());
            }
        }
    }
}
