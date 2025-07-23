using System;

namespace StackExchange.Redis;

internal partial class RedisDatabase
{
    /// <summary>
    /// Parses, validates and represents, for example: "EX 10", "KEEPTTL" or "".
    /// </summary>
    internal readonly struct ExpiryToken
    {
        private static readonly ExpiryToken s_Persist = new(RedisLiterals.PERSIST), s_KeepTtl = new(RedisLiterals.KEEPTTL), s_Null = new(RedisValue.Null);

        public RedisValue Operand { get; }
        public long Value { get; }
        public int Tokens => Value == long.MinValue ? (Operand.IsNull ? 0 : 1) : 2;
        public bool HasValue => Value != long.MinValue;
        public bool HasOperand => !Operand.IsNull;

        public static ExpiryToken Persist(TimeSpan? expiry, bool persist)
        {
            if (expiry.HasValue)
            {
                if (persist) throw new ArgumentException("Cannot specify both expiry and persist", nameof(persist));
                return new(expiry.GetValueOrDefault()); // EX 10
            }

            return persist ? s_Persist : s_Null; // PERSIST (or nothing)
        }

        public static ExpiryToken KeepTtl(TimeSpan? expiry, bool keepTtl)
        {
            if (expiry.HasValue)
            {
                if (keepTtl) throw new ArgumentException("Cannot specify both expiry and keepTtl", nameof(keepTtl));
                return new(expiry.GetValueOrDefault()); // EX 10
            }

            return keepTtl ? s_KeepTtl : s_Null; // KEEPTTL (or nothing)
        }

        private ExpiryToken(RedisValue operand, long value = long.MinValue)
        {
            Operand = operand;
            Value = value;
        }

        public ExpiryToken(TimeSpan expiry)
        {
            long milliseconds = expiry.Ticks / TimeSpan.TicksPerMillisecond;
            var useSeconds = milliseconds % 1000 == 0;

            Operand = useSeconds ? RedisLiterals.EX : RedisLiterals.PX;
            Value = useSeconds ? (milliseconds / 1000) : milliseconds;
        }

        public ExpiryToken(DateTime expiry)
        {
            long milliseconds = GetUnixTimeMilliseconds(expiry);
            var useSeconds = milliseconds % 1000 == 0;

            Operand = useSeconds ? RedisLiterals.EXAT : RedisLiterals.PXAT;
            Value = useSeconds ? (milliseconds / 1000) : milliseconds;
        }

        public override string ToString() => Tokens switch
        {
            2 => $"{Operand} {Value}",
            1 => Operand.ToString(),
            _ => "",
        };

        public override int GetHashCode() => throw new NotSupportedException();
        public override bool Equals(object? obj) => throw new NotSupportedException();
    }
}
