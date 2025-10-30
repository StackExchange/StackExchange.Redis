using System;

namespace StackExchange.Redis;

internal partial class RedisDatabase
{
    internal readonly struct ExpiryToken
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

        private readonly ulong _valueAndMode;

        private static void Init(ExpirationMode mode, long value, out ulong valueAndMode)
        {
            // check the caller isn't using the top 3 bits that we have reserved; this includes checking for -ve values
            ulong uValue = (ulong)value;
            if ((uValue & ~ValueMask) != 0) Throw();
            valueAndMode = (uValue & ValueMask) | ((ulong)mode << 61);
            static void Throw() => throw new ArgumentOutOfRangeException(nameof(value));
        }

        private ExpiryToken(ExpirationMode mode, long value) => Init(mode, value, out _valueAndMode);

        private enum ExpirationMode : byte
        {
            None = 0,
            RelativeSeconds = 1,
            RelativeMilliseconds = 2,
            AbsoluteSeconds = 3,
            AbsoluteMilliseconds = 4,
            KeepTtl = 5,
            Persist = 6,
            NotUsed = 7, // just to ensure all 8 possible values are covered
        }

        private const ulong ValueMask = (~0UL) >> 3;
        public long Value => unchecked((long)(_valueAndMode & ValueMask));
        private ExpirationMode Mode => (ExpirationMode)(_valueAndMode >> 61); // note unsigned, no need to mask

        public bool IsKeepTtl => Mode is ExpirationMode.KeepTtl;
        public bool IsPersist => Mode is ExpirationMode.Persist;
        public bool IsNone => Mode is ExpirationMode.None;
        public bool IsNoneOrKeepTtl => Mode is ExpirationMode.None or ExpirationMode.KeepTtl;
        public bool IsAbsolute => Mode is ExpirationMode.AbsoluteSeconds or ExpirationMode.AbsoluteMilliseconds;
        public bool IsRelative => Mode is ExpirationMode.RelativeSeconds or ExpirationMode.RelativeMilliseconds;

        public bool IsMilliseconds =>
            Mode is ExpirationMode.RelativeMilliseconds or ExpirationMode.AbsoluteMilliseconds;

        public bool IsSeconds => Mode is ExpirationMode.RelativeSeconds or ExpirationMode.AbsoluteSeconds;

        public static readonly ExpiryToken None = new(ExpirationMode.None, 0);

        private static readonly ExpiryToken s_KeepTtl = new(ExpirationMode.KeepTtl, 0),
            s_Persist = new(ExpirationMode.Persist, 0);

        private static void ThrowExpiryAndKeepTtl() =>
            // ReSharper disable once NotResolvedInText
            throw new ArgumentException(message: "Cannot specify both expiry and keepTtl.", paramName: "keepTtl");

        private static void ThrowExpiryAndPersist() =>
            // ReSharper disable once NotResolvedInText
            throw new ArgumentException(message: "Cannot specify both expiry and persist.", paramName: "persist");

        public ExpiryToken(TimeSpan ttl)
        {
            if (ttl == TimeSpan.MaxValue)
            {
                _valueAndMode = None._valueAndMode;
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

        public static ExpiryToken Persist(TimeSpan? ttl, bool persist)
        {
            if (persist)
            {
                if (ttl.HasValue) ThrowExpiryAndPersist();
                return s_Persist;
            }

            return ttl.HasValue ? new(ttl.GetValueOrDefault()) : None;
        }

        public static ExpiryToken KeepTtl(TimeSpan? ttl, bool keepTtl)
        {
            if (keepTtl)
            {
                if (ttl.HasValue) ThrowExpiryAndKeepTtl();
                return s_KeepTtl;
            }

            return ttl.HasValue ? new(ttl.GetValueOrDefault()) : None;
        }

        public static long GetUnixTimeMilliseconds(DateTime when)
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

        public ExpiryToken(DateTime when)
        {
            if (when == DateTime.MaxValue)
            {
                _valueAndMode = None._valueAndMode;
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

        public static ExpiryToken Persist(DateTime? when, bool persist)
        {
            if (persist)
            {
                if (when.HasValue) ThrowExpiryAndPersist();
                return s_Persist;
            }

            return when.HasValue ? new(when.GetValueOrDefault()) : None;
        }

        public static ExpiryToken KeepTtl(DateTime? ttl, bool keepTtl)
        {
            if (keepTtl)
            {
                if (ttl.HasValue) ThrowExpiryAndKeepTtl();
                return s_KeepTtl;
            }

            return ttl.HasValue ? new(ttl.GetValueOrDefault()) : None;
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
            ExpirationMode.None or ExpirationMode.NotUsed => "",
            ExpirationMode.KeepTtl => "KEEPTTL",
            ExpirationMode.Persist => "PERSIST",
            _ => $"{Operand} {Value}",
        };

        /// <inheritdoc/>
        public override int GetHashCode() => _valueAndMode.GetHashCode();

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is ExpiryToken other && _valueAndMode == other._valueAndMode;

        internal int Tokens => Mode switch
        {
            ExpirationMode.None or ExpirationMode.NotUsed => 0,
            ExpirationMode.KeepTtl or ExpirationMode.Persist => 1,
            _ => 2,
        };

        internal void WriteTo(PhysicalConnection physical)
        {
            var mode = Mode;
            switch (Mode)
            {
                case ExpirationMode.None or ExpirationMode.NotUsed:
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
}
