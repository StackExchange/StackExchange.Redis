using System;
using System.Linq;
using System.Threading.Tasks;

namespace StackExchange.Redis.Modules.Throttling
{
    public static class ThrottlingExtensions
    {
        public static ThrottleResult Throttle(
            this IDatabase db, RedisKey key, int maxBurst,
            int maxPerInterval,
            int intervalSeconds = 60, int count = 1)
        {
            return new ThrottleResult(db.Execute("CL.THROTTLE",
                key, maxBurst.Boxed(), maxPerInterval.Boxed(), intervalSeconds.Boxed(), count.Boxed()));
        }
        public async static Task<ThrottleResult> ThrottleAsync(
            this IDatabaseAsync db, RedisKey key, int maxBurst,
            int maxPerInterval,
            int intervalSeconds = 60, int count = 1)
        {
            return new ThrottleResult(await db.ExecuteAsync("CL.THROTTLE",
                key, maxBurst.Boxed(), maxPerInterval.Boxed(), intervalSeconds.Boxed(), count.Boxed()));
        }

        static readonly object[] _boxedInt32 = Enumerable.Range(-1, 128).Select(i => (object)i).ToArray();
        internal static object Boxed(this int value)
            => value >= -1 && value <= 126 ? _boxedInt32[value + 1] : (object)value;
    }
    public struct ThrottleResult
    {
        internal ThrottleResult(RedisResult result)
        {
            var arr = (int[])result;
            Permitted = arr[0] == 0;
            TotalLimit = arr[1];
            RemainingLimit = arr[2];
            RetryAfterSeconds = arr[3];
            ResetAfterSeconds = arr[4];
        }
        /// <summary>Whether the action was limited</summary>
        public bool Permitted {get;}
        /// <summary>The total limit of the key (max_burst + 1). This is equivalent to the common `X-RateLimit-Limit` HTTP header.</summary>
        public int TotalLimit {get;}
        /// <summary>The remaining limit of the key. Equivalent to `X-RateLimit-Remaining`.</summary>
        public int RemainingLimit {get;}
        /// <summary>The number of seconds until the user should retry, and always -1 if the action was allowed. Equivalent to `Retry-After`.</summary>
        public int RetryAfterSeconds {get;}
        /// <summary>The number of seconds until the limit will reset to its maximum capacity. Equivalent to `X-RateLimit-Reset`.</summary>
        public int ResetAfterSeconds {get;}
    } 
}