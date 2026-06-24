using System;
using System.Buffers;
using System.Threading.Tasks;

namespace StackExchange.Redis;

public sealed partial class HealthCheck
{
    public partial class HealthCheckProbe
    {
        /// <summary>
        /// Verify that a string can be successfully set and retrieved.
        /// </summary>
        public static HealthCheckProbe StringSet => StringSetProbe.Instance;
    }

    internal sealed class StringSetProbe : KeyWriteHealthCheckProbe
    {
        public static StringSetProbe Instance { get; } = new();
        private StringSetProbe() { }

#if !NET
        private static Random SharedRandom { get; } = new();
#endif

        public override async Task<HealthCheckResult> CheckHealthAsync(HealthCheck healthCheck, IDatabaseAsync database, RedisKey key)
        {
            // note we use the lock API here because that can selectively choose between appropriate strategies for
            // different server versions, including DELEX
            const int LEN = 16;
            var pooled = ArrayPool<byte>.Shared.Rent(LEN);
#if NET
            Random.Shared.NextBytes(pooled.AsSpan(0, LEN));
#else
            SharedRandom.NextBytes(pooled);
#endif
            var payload = (RedisValue)pooled.AsMemory(0, LEN);
            Lease<byte>? lease = null;
            try
            {
                // write a value to the db
                await database.LockTakeAsync(
                    key: key,
                    value: payload,
                    expiry: healthCheck.ProbeTimeout,
                    flags: CommandFlags.FireAndForget).ForAwait();

                // release from the db if matches (otherwise, we have no clue what happened, so: leave alone)
                var success = await database.LockReleaseAsync(key, payload).ForAwait();
                return success ? HealthCheckResult.Healthy : HealthCheckResult.Unhealthy;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pooled);
                lease?.Dispose();
            }
        }
    }
}
