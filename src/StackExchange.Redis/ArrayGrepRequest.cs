using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Describes an array grep operation.
/// </summary>
[Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
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
    [Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
    public abstract class Predicate
    {
        internal virtual int ArgCount => 2;
        internal abstract void WriteTo(in MessageWriter writer);
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
        }

        private sealed class MatchPredicate(string pattern) : Predicate
        {
            public override string ToString() => $"MATCH '{pattern}'";

            internal override void WriteTo(in MessageWriter writer)
            {
                writer.WriteRaw("$5\r\nMATCH\r\n"u8);
                writer.WriteBulkString(pattern);
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
        }

        private sealed class RegexPredicate(string re) : Predicate
        {
            public override string ToString() => $"RE '{re}'";

            internal override void WriteTo(in MessageWriter writer)
            {
                writer.WriteRaw("$2\r\nRE\r\n"u8);
                writer.WriteBulkString(re);
            }
        }
    }

    private sealed class ArrayGrepMessage(int db, RedisKey key, ArrayGrepRequest request, CommandFlags flags)
        : Message(db, flags, RedisCommand.ARGREP)
    {
        public override int ArgCount
        {
            get
            {
                var count = 3; // key, start, end
                var pCount = request.Count;
                for (int i = 0; i < pCount; i++)
                {
                    count += request[i].ArgCount;
                }

                if (request.IsIntersection) count++;
                if (request.IsCaseInsensitive) count++;
                if (request.IncludeValues) count++;
                var limit = request.Limit;
                if (limit.HasValue) count += 2;
                return count;
            }
        }

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
            writer.WriteBulkString(key);
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
