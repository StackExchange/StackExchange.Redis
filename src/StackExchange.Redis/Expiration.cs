using System;

namespace StackExchange.Redis;

/// <summary>
/// Configures the expiration behaviour of a command.
/// </summary>
public readonly struct Expiration : Interpolated.IRespArgument
{
    /*
     Redis expiration supports different modes:
    - (nothing) - do nothing; implicit wipe for writes, nothing for reads
    - PERSIST - explicit wipe of expiry
    - KEEPTTL  - sets no expiry, but leaves any existing expiry alone
    - EX {s} - relative expiry in seconds
    - PX {ms} - relative expiry in milliseconds
    - EXAT {s} - absolute expiry in seconds
    - PXAT {ms} - absolute expiry in milliseconds
    - ENX - only apply the expiration if no expiration currently exists

    Historically this packed the mode and value into a single ulong. We now keep the raw long
    separate from explicit flags so we can extend expiration behavior without stealing more bits
    from the numeric payload.
    */

    /// <summary>
    /// Default expiration behaviour. For writes, this is typically no expiration. For reads, this is typically no action.
    /// </summary>
    public static Expiration Default => s_Default;

    /// <summary>
    /// Explicitly retain the existing expiry, if one. This is valid in some (not all) write scenarios.
    /// </summary>
    public static Expiration KeepTtl => s_KeepTtl;

    /// <summary>
    /// Explicitly remove the existing expiry, if one. This is valid in some (not all) read scenarios.
    /// </summary>
    public static Expiration Persist => s_Persist;

    /// <summary>
    /// Expire at the specified absolute time.
    /// </summary>
    public Expiration(DateTime when) : this(when, ExpirationFlags.None) { }

    /// <summary>
    /// Expire at the specified absolute time.
    /// </summary>
    public Expiration(DateTime when, ExpirationFlags flags)
    {
        if (when == DateTime.MaxValue)
        {
            _value = s_Default._value;
            _flags = s_Default._flags;
            return;
        }

        long millis = GetUnixTimeMilliseconds(when);
        var extraFlags = ToStateFlags(flags);
        if ((millis % 1000) == 0)
        {
            Init(ExpirationState.HasExpiration | ExpirationState.IsAbsolute | extraFlags, millis / 1000, out _value, out _flags);
        }
        else
        {
            Init(ExpirationState.HasExpiration | ExpirationState.IsAbsolute | ExpirationState.IsMillis | extraFlags, millis, out _value, out _flags);
        }
    }

    /// <summary>
    /// Expire at the specified absolute time.
    /// </summary>
    public static implicit operator Expiration(DateTime when) => new(when);

    /// <summary>
    /// Expire at the specified absolute time.
    /// </summary>
    public static implicit operator Expiration(TimeSpan ttl) => new(ttl);

    /// <summary>
    /// Expire at the specified relative time.
    /// </summary>
    public Expiration(TimeSpan ttl) : this(ttl, ExpirationFlags.None) { }

    /// <summary>
    /// Expire at the specified relative time.
    /// </summary>
    public Expiration(TimeSpan ttl, ExpirationFlags flags)
    {
        if (ttl == TimeSpan.MaxValue)
        {
            _value = s_Default._value;
            _flags = s_Default._flags;
            return;
        }

        var millis = ttl.Ticks / TimeSpan.TicksPerMillisecond;
        var extraFlags = ToStateFlags(flags);
        if ((millis % 1000) == 0)
        {
            Init(ExpirationState.HasExpiration | extraFlags, millis / 1000, out _value, out _flags);
        }
        else
        {
            Init(ExpirationState.HasExpiration | ExpirationState.IsMillis | extraFlags, millis, out _value, out _flags);
        }
    }

    private readonly long _value;
    private readonly ExpirationState _flags;

    [Flags]
    private enum ExpirationState : byte
    {
        None = 0,
        ExpireIfNotExists = (byte)ExpirationFlags.ExpireIfNotExists,
        HasExpiration = 1 << 1,
        IsMillis = 1 << 2,
        IsAbsolute = 1 << 3,
        KeepTtl = 1 << 4,
        Persist = 1 << 5,
    }

    private static ExpirationState ToStateFlags(ExpirationFlags flags)
    {
        const ExpirationFlags validFlags = ExpirationFlags.ExpireIfNotExists;
        if ((flags & ~validFlags) != 0) Throw();
        return (ExpirationState)flags;

        static void Throw() => throw new ArgumentOutOfRangeException(nameof(flags));
    }

    private static void Init(ExpirationState flags, long value, out long rawValue, out ExpirationState rawFlags)
    {
        if (value < 0) Throw();
        rawValue = value;
        rawFlags = flags;
        static void Throw() => throw new ArgumentOutOfRangeException(nameof(value));
    }

    private Expiration(ExpirationState flags, long value)
    {
        _value = value;
        _flags = flags;
    }

    internal long Value => _value;

    internal bool IsKeepTtl => (_flags & ExpirationState.KeepTtl) != 0;
    internal bool IsPersist => (_flags & ExpirationState.Persist) != 0;
    internal bool IsExpireIfNotExists => (_flags & ExpirationState.ExpireIfNotExists) != 0;
    internal bool IsNone => _flags == ExpirationState.None;
    internal bool IsNoneOrKeepTtl => IsNone || IsKeepTtl;
    internal bool IsAbsolute => (_flags & ExpirationState.IsAbsolute) != 0;
    internal bool IsRelative => (_flags & ExpirationState.HasExpiration) != 0 && !IsAbsolute;

    internal bool IsMilliseconds => (_flags & ExpirationState.IsMillis) != 0;

    internal bool IsSeconds => (_flags & (ExpirationState.HasExpiration | ExpirationState.IsMillis)) == ExpirationState.HasExpiration;

    private static readonly Expiration s_Default = new(ExpirationState.None, 0);

    private static readonly Expiration s_KeepTtl = new(ExpirationState.KeepTtl, 0),
        s_Persist = new(ExpirationState.Persist, 0);

    private static void ThrowExpiryAndKeepTtl() =>
        // ReSharper disable once NotResolvedInText
        throw new ArgumentException(message: "Cannot specify both expiry and keepTtl.", paramName: "keepTtl");

    private static void ThrowExpiryAndPersist() =>
        // ReSharper disable once NotResolvedInText
        throw new ArgumentException(message: "Cannot specify both expiry and persist.", paramName: "persist");

    internal static Expiration CreateOrPersist(in TimeSpan? ttl, bool persist)
    {
        if (persist)
        {
            if (ttl.HasValue) ThrowExpiryAndPersist();
            return s_Persist;
        }

        return ttl.HasValue ? new(ttl.GetValueOrDefault()) : s_Default;
    }

    internal static Expiration CreateOrKeepTtl(in TimeSpan? ttl, bool keepTtl)
    {
        if (keepTtl)
        {
            if (ttl.HasValue) ThrowExpiryAndKeepTtl();
            return s_KeepTtl;
        }

        return ttl.HasValue ? new(ttl.GetValueOrDefault()) : s_Default;
    }

    internal static long GetUnixTimeMilliseconds(DateTime when)
    {
        return when.Kind switch
        {
            DateTimeKind.Local or DateTimeKind.Utc => (when.ToUniversalTime() - RedisBase.UnixEpoch).Ticks /
                                                      TimeSpan.TicksPerMillisecond,
            _ => ThrowKind(),
        };

        static long ThrowKind() =>
            throw new ArgumentException("Expiry time must be either Utc or Local", nameof(when));
    }

    internal static Expiration CreateOrPersist(in DateTime? when, bool persist)
    {
        if (persist)
        {
            if (when.HasValue) ThrowExpiryAndPersist();
            return s_Persist;
        }

        return when.HasValue ? new(when.GetValueOrDefault()) : s_Default;
    }

    internal static Expiration CreateOrKeepTtl(in DateTime? ttl, bool keepTtl)
    {
        if (keepTtl)
        {
            if (ttl.HasValue) ThrowExpiryAndKeepTtl();
            return s_KeepTtl;
        }

        return ttl.HasValue ? new(ttl.GetValueOrDefault()) : s_Default;
    }

    internal RedisValue Operand => GetOperand(out _);

    internal RedisValue GetOperand(out long value)
    {
        value = Value;
        if (IsKeepTtl) return RedisLiterals.KEEPTTL;
        if (IsPersist) return RedisLiterals.PERSIST;
        if ((_flags & ExpirationState.HasExpiration) == 0) return RedisValue.Null;

        return (_flags & (ExpirationState.IsAbsolute | ExpirationState.IsMillis)) switch
        {
            ExpirationState.IsAbsolute | ExpirationState.IsMillis => RedisLiterals.PXAT,
            ExpirationState.IsAbsolute => RedisLiterals.EXAT,
            ExpirationState.IsMillis => RedisLiterals.PX,
            _ => RedisLiterals.EX,
        };
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        if (IsNone) return "";
        if (IsKeepTtl) return "KEEPTTL";
        if (IsPersist) return "PERSIST";
        return IsExpireIfNotExists ? $"{Operand} {Value} ENX" : $"{Operand} {Value}";
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        unchecked
        {
            return (_value.GetHashCode() * 397) ^ (int)_flags;
        }
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Expiration other && _value == other._value && _flags == other._flags;

    internal int GetTokenCount(bool allowEnx)
    {
        if (!allowEnx && IsExpireIfNotExists) return ThrowEnxNotSupported();
        return IsNone ? 0 : (IsKeepTtl || IsPersist ? 1 : (IsExpireIfNotExists ? 3 : 2));

        static int ThrowEnxNotSupported() => throw new NotSupportedException("ENX is not supported for this command.");
    }

    /// <summary>
    /// The already-framed RESP token naming the mode - <c>EX</c>, <c>PXAT</c>, <c>KEEPTTL</c>, ... - or
    /// empty when this expiration contributes no arguments at all.
    /// </summary>
    /// <remarks>
    /// Shared by every writer rather than restated per writer: this switch is the whole of the mode
    /// selection, and it is the part that would silently diverge if each writer kept its own copy.
    /// <see cref="HasExpirationValue"/> says whether a numeric operand follows it.
    /// </remarks>
    internal ReadOnlySpan<byte> OperandResp
    {
        get
        {
            if (IsNone) return default;
            if (IsKeepTtl) return "$7\r\nKEEPTTL\r\n"u8;
            if (IsPersist) return "$7\r\nPERSIST\r\n"u8;
            return (_flags & (ExpirationState.IsAbsolute | ExpirationState.IsMillis)) switch
            {
                ExpirationState.IsAbsolute | ExpirationState.IsMillis => "$4\r\nPXAT\r\n"u8,
                ExpirationState.IsAbsolute => "$4\r\nEXAT\r\n"u8,
                ExpirationState.IsMillis => "$2\r\nPX\r\n"u8,
                _ => "$2\r\nEX\r\n"u8,
            };
        }
    }

    /// <summary>Whether <see cref="OperandResp"/> is followed by a numeric <see cref="Value"/>.</summary>
    /// <remarks>False for KEEPTTL and PERSIST, which are complete in themselves.</remarks>
    internal bool HasExpirationValue => (_flags & ExpirationState.HasExpiration) != 0;

    /// <summary>The already-framed RESP token for ENX, or empty when it does not apply.</summary>
    internal ReadOnlySpan<byte> ExpireIfNotExistsResp
        => HasExpirationValue && IsExpireIfNotExists ? "$3\r\nENX\r\n"u8 : default;

    /// <inheritdoc/>
    /// <remarks>
    /// Explicit, so it does not clutter the type for callers who will never write a RESP frame by hand;
    /// reached only through a command hole - <c>$"{...}{expiry}"</c> - which is the one place it means
    /// anything. The generic <c>AppendFormatted&lt;T&gt;</c> funnel is what binds it there, in preference
    /// to a dedicated overload: if the extension mechanism is good enough for other libraries' types it is
    /// good enough for ours, and this is the proof.
    /// </remarks>
    void Interpolated.IRespArgument.WriteTo(scoped ref Interpolated.RespCommandHandler handler)
    {
        var operand = OperandResp;
        if (operand.IsEmpty) return; // Expiration.Default contributes no arguments

        // SER011 gates hand-written pre-framed fragments, because nothing validates the claim that the
        // bytes are correctly framed. These are compile-time constants owned by this type and shared with
        // the MessageWriter path (OperandResp), so the claim is as checked as it can be - and they are
        // ALREADY framed, so AppendBulk, which frames what it is given, is not the right primitive.
#pragma warning disable SER011
        handler.AppendFormatted(new Interpolated.RespFragment(operand));
        if (HasExpirationValue)
        {
            handler.AppendFormatted((RedisValue)Value);
            var enx = ExpireIfNotExistsResp;
            if (!enx.IsEmpty) handler.AppendFormatted(new Interpolated.RespFragment(enx));
        }
#pragma warning restore SER011
    }

    internal void WriteTo(in MessageWriter writer)
    {
        var operand = OperandResp;
        if (operand.IsEmpty) return;

        writer.WriteRaw(operand);
        if (HasExpirationValue)
        {
            writer.WriteBulkString(Value);
            var enx = ExpireIfNotExistsResp;
            if (!enx.IsEmpty) writer.WriteRaw(enx);
        }
    }
}
