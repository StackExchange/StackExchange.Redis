using System;

namespace StackExchange.Redis;

/// <summary>
/// Configures the expiration behaviour of a command.
/// </summary>
public readonly struct Expiration
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

    We need to distinguish between these 6 scenarios, which we can logically do with 3 bits (8 options).
    So; we'll use a ulong for the value, reserving the top 3 bits for the mode.
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
    public Expiration(DateTime when)
    {
        if (when == DateTime.MaxValue)
        {
            _valueAndMode = s_Default._valueAndMode;
            return;
        }

        long millis = GetUnixTimeMilliseconds(when);
        if ((millis % 1000) == 0)
        {
            Init(ExpirationMode.AbsoluteSeconds, millis / 1000, out _valueAndMode);
        }
        else
        {
            Init(ExpirationMode.AbsoluteMilliseconds, millis, out _valueAndMode);
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
    public Expiration(TimeSpan ttl)
    {
        if (ttl == TimeSpan.MaxValue)
        {
            _valueAndMode = s_Default._valueAndMode;
            return;
        }

        var millis = ttl.Ticks / TimeSpan.TicksPerMillisecond;
        if ((millis % 1000) == 0)
        {
            Init(ExpirationMode.RelativeSeconds, millis / 1000, out _valueAndMode);
        }
        else
        {
            Init(ExpirationMode.RelativeMilliseconds, millis, out _valueAndMode);
        }
    }

    private readonly ulong _valueAndMode;

    private static void Init(ExpirationMode mode, long value, out ulong valueAndMode)
    {
        // check the caller isn't using the top 3 bits that we have reserved; this includes checking for -ve values
        ulong uValue = (ulong)value;
        if ((uValue & ~ValueMask) != 0) Throw();
        valueAndMode = (uValue & ValueMask) | ((ulong)mode << 61);
        static void Throw() => throw new ArgumentOutOfRangeException(nameof(value));
    }

    private Expiration(ExpirationMode mode, long value) => Init(mode, value, out _valueAndMode);

    private enum ExpirationMode : byte
    {
        Default = 0,
        RelativeSeconds = 1,
        RelativeMilliseconds = 2,
        AbsoluteSeconds = 3,
        AbsoluteMilliseconds = 4,
        KeepTtl = 5,
        Persist = 6,
        NotUsed = 7, // just to ensure all 8 possible values are covered
    }

    private const ulong ValueMask = (~0UL) >> 3;
    internal long Value => unchecked((long)(_valueAndMode & ValueMask));
    private ExpirationMode Mode => (ExpirationMode)(_valueAndMode >> 61); // note unsigned, no need to mask

    internal bool IsKeepTtl => Mode is ExpirationMode.KeepTtl;
    internal bool IsPersist => Mode is ExpirationMode.Persist;
    internal bool IsNone => Mode is ExpirationMode.Default;
    internal bool IsNoneOrKeepTtl => Mode is ExpirationMode.Default or ExpirationMode.KeepTtl;
    internal bool IsAbsolute => Mode is ExpirationMode.AbsoluteSeconds or ExpirationMode.AbsoluteMilliseconds;
    internal bool IsRelative => Mode is ExpirationMode.RelativeSeconds or ExpirationMode.RelativeMilliseconds;

    internal bool IsMilliseconds =>
        Mode is ExpirationMode.RelativeMilliseconds or ExpirationMode.AbsoluteMilliseconds;

    internal bool IsSeconds => Mode is ExpirationMode.RelativeSeconds or ExpirationMode.AbsoluteSeconds;

    private static readonly Expiration s_Default = new(ExpirationMode.Default, 0);

    private static readonly Expiration s_KeepTtl = new(ExpirationMode.KeepTtl, 0),
        s_Persist = new(ExpirationMode.Persist, 0);

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
        var mode = Mode;
        return mode switch
        {
            ExpirationMode.KeepTtl => RedisLiterals.KEEPTTL,
            ExpirationMode.Persist => RedisLiterals.PERSIST,
            ExpirationMode.RelativeSeconds => RedisLiterals.EX,
            ExpirationMode.RelativeMilliseconds => RedisLiterals.PX,
            ExpirationMode.AbsoluteSeconds => RedisLiterals.EXAT,
            ExpirationMode.AbsoluteMilliseconds => RedisLiterals.PXAT,
            _ => RedisValue.Null,
        };
    }

    private static void ThrowMode(ExpirationMode mode) =>
        throw new InvalidOperationException("Unknown mode: " + mode);

    /// <inheritdoc/>
    public override string ToString() => Mode switch
    {
        ExpirationMode.Default or ExpirationMode.NotUsed => "",
        ExpirationMode.KeepTtl => "KEEPTTL",
        ExpirationMode.Persist => "PERSIST",
        _ => $"{Operand} {Value}",
    };

    /// <inheritdoc/>
    public override int GetHashCode() => _valueAndMode.GetHashCode();

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Expiration other && _valueAndMode == other._valueAndMode;

    internal int Tokens => Mode switch
    {
        ExpirationMode.Default or ExpirationMode.NotUsed => 0,
        ExpirationMode.KeepTtl or ExpirationMode.Persist => 1,
        _ => 2,
    };

    internal void WriteTo(PhysicalConnection physical)
    {
        var mode = Mode;
        switch (Mode)
        {
            case ExpirationMode.Default or ExpirationMode.NotUsed:
                break;
            case ExpirationMode.KeepTtl:
                physical.WriteBulkString("KEEPTTL"u8);
                break;
            case ExpirationMode.Persist:
                physical.WriteBulkString("PERSIST"u8);
                break;
            default:
                physical.WriteBulkString(mode switch
                {
                    ExpirationMode.RelativeSeconds => "EX"u8,
                    ExpirationMode.RelativeMilliseconds => "PX"u8,
                    ExpirationMode.AbsoluteSeconds => "EXAT"u8,
                    ExpirationMode.AbsoluteMilliseconds => "PXAT"u8,
                    _ => default,
                });
                physical.WriteBulkString(Value);
                break;
        }
    }
}
