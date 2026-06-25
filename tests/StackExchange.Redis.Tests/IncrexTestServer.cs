using System;
using System.Globalization;
using RESPite.Messages;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

public class IncrexTestServer(ITestOutputHelper? log = null) : InProcessTestServer(log)
{
    public sealed class IncrexRequestSnapshot
    {
        public RedisKey Key { get; set; }
        public bool IsFloat { get; set; }
        public string Increment { get; set; } = "";
        public string? LowerBound { get; set; }
        public string? UpperBound { get; set; }
        public bool Saturate { get; set; }
        public string? ExpiryMode { get; set; }
        public string? ExpiryValue { get; set; }
        public bool Enx { get; set; }
    }

    public IncrexRequestSnapshot? LastRequest { get; private set; }

    [RedisCommand(-4, "INCREX")]
    protected virtual TypedRedisValue Increx(RedisClient client, in RedisRequest request)
    {
        var snapshot = ParseRequest(in request);
        LastRequest = snapshot;

        return snapshot.IsFloat
            ? ExecuteDouble(client.Database, snapshot)
            : ExecuteInt64(client.Database, snapshot);
    }

    private IncrexRequestSnapshot ParseRequest(in RedisRequest request)
    {
        var snapshot = new IncrexRequestSnapshot { Key = request.GetKey(1) };
        int index = 2;
        while (index < request.Count)
        {
            switch (request.GetString(index++))
            {
                case "BYINT":
                    snapshot.IsFloat = false;
                    snapshot.Increment = request.GetString(index++);
                    break;
                case "BYFLOAT":
                    snapshot.IsFloat = true;
                    snapshot.Increment = request.GetString(index++);
                    break;
                case "LBOUND":
                    snapshot.LowerBound = request.GetString(index++);
                    break;
                case "UBOUND":
                    snapshot.UpperBound = request.GetString(index++);
                    break;
                case "SATURATE":
                    snapshot.Saturate = true;
                    break;
                case "EX":
                case "PX":
                case "EXAT":
                case "PXAT":
                    snapshot.ExpiryMode = request.GetString(index - 1);
                    snapshot.ExpiryValue = request.GetString(index++);
                    break;
                case "ENX":
                    snapshot.Enx = true;
                    break;
                default:
                    throw new InvalidOperationException("Unknown INCREX token: " + request.GetString(index - 1));
            }
        }
        return snapshot;
    }

    private TypedRedisValue ExecuteInt64(int database, IncrexRequestSnapshot snapshot)
    {
        var raw = Get(database, snapshot.Key);
        bool existed = !raw.IsNull;
        long current = raw.IsNull ? 0 : (long)raw;
        long delta = long.Parse(snapshot.Increment, CultureInfo.InvariantCulture);
        long? lowerBound = snapshot.LowerBound is null ? null : long.Parse(snapshot.LowerBound, CultureInfo.InvariantCulture);
        long? upperBound = snapshot.UpperBound is null ? null : long.Parse(snapshot.UpperBound, CultureInfo.InvariantCulture);

        GetInt64Result(current, delta, lowerBound, upperBound, snapshot.Saturate, out var next, out var applied, out var ignored);

        if (ignored)
        {
            return MakeResult(current, 0);
        }

        ApplyValueAndExpiry(database, snapshot, existed, next);
        return MakeResult(next, applied);
    }

    private TypedRedisValue ExecuteDouble(int database, IncrexRequestSnapshot snapshot)
    {
        var raw = Get(database, snapshot.Key);
        bool existed = !raw.IsNull;
        double current = raw.IsNull ? 0D : (double)raw;
        double delta = double.Parse(snapshot.Increment, CultureInfo.InvariantCulture);
        double? lowerBound = snapshot.LowerBound is null ? null : double.Parse(snapshot.LowerBound, CultureInfo.InvariantCulture);
        double? upperBound = snapshot.UpperBound is null ? null : double.Parse(snapshot.UpperBound, CultureInfo.InvariantCulture);

        GetDoubleResult(current, delta, lowerBound, upperBound, snapshot.Saturate, out var next, out var applied, out var ignored);

        if (ignored)
        {
            return MakeResult(current, 0);
        }

        ApplyValueAndExpiry(database, snapshot, existed, next);
        return MakeResult(next, applied);
    }

    private void ApplyValueAndExpiry(int database, IncrexRequestSnapshot snapshot, bool existed, RedisValue value)
    {
        var priorTtl = existed ? Ttl(database, snapshot.Key) : null;
        Set(database, snapshot.Key, value);

        if (snapshot.ExpiryMode is null)
        {
            if (priorTtl.HasValue && priorTtl.Value != TimeSpan.MaxValue)
            {
                _ = Expire(database, snapshot.Key, priorTtl.Value);
            }
            return;
        }

        if (snapshot.Enx && priorTtl.HasValue && priorTtl.Value != TimeSpan.MaxValue)
        {
            _ = Expire(database, snapshot.Key, priorTtl.Value);
            return;
        }

        var ttl = snapshot.ExpiryMode switch
        {
            "EX" => TimeSpan.FromSeconds(long.Parse(snapshot.ExpiryValue!, CultureInfo.InvariantCulture)),
            "PX" => TimeSpan.FromMilliseconds(long.Parse(snapshot.ExpiryValue!, CultureInfo.InvariantCulture)),
            "EXAT" => DateTimeOffset.FromUnixTimeSeconds(long.Parse(snapshot.ExpiryValue!, CultureInfo.InvariantCulture)).UtcDateTime - Time(),
            "PXAT" => DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(snapshot.ExpiryValue!, CultureInfo.InvariantCulture)).UtcDateTime - Time(),
            _ => throw new InvalidOperationException("Unknown expiry mode: " + snapshot.ExpiryMode),
        };
        _ = Expire(database, snapshot.Key, ttl);
    }

    private static void GetInt64Result(
        long current,
        long delta,
        long? lowerBound,
        long? upperBound,
        bool saturate,
        out long next,
        out long applied,
        out bool ignored)
    {
        ignored = false;

        if (!TryAdd(current, delta, out var candidate))
        {
            HandleInt64Overflow(current, delta, lowerBound, upperBound, saturate, out next, out applied, out ignored);
            return;
        }

        if (saturate)
        {
            next = Clamp(candidate, lowerBound, upperBound);
            applied = next - current;
            return;
        }

        if (IsInBounds(candidate, lowerBound, upperBound))
        {
            next = candidate;
            applied = delta;
            return;
        }

        next = current;
        applied = 0;
        ignored = true;
    }

    private static void HandleInt64Overflow(
        long current,
        long delta,
        long? lowerBound,
        long? upperBound,
        bool saturate,
        out long next,
        out long applied,
        out bool ignored)
    {
        if (saturate)
        {
            ignored = false;
            next = delta >= 0 ? upperBound ?? long.MaxValue : lowerBound ?? long.MinValue;
            applied = next - current;
            return;
        }

        next = current;
        applied = 0;
        ignored = true;
    }

    private static void GetDoubleResult(
        double current,
        double delta,
        double? lowerBound,
        double? upperBound,
        bool saturate,
        out double next,
        out double applied,
        out bool ignored)
    {
        ignored = false;
        var candidate = current + delta;

        if (saturate)
        {
            next = IsFinite(candidate)
                ? Clamp(candidate, lowerBound, upperBound)
                : delta >= 0 ? upperBound.GetValueOrDefault(double.MaxValue) : lowerBound.GetValueOrDefault(double.MinValue);
            applied = next - current;
            return;
        }

        if (IsFinite(candidate) && IsInBounds(candidate, lowerBound, upperBound))
        {
            next = candidate;
            applied = delta;
            return;
        }

        next = current;
        applied = 0;
        ignored = true;
    }

    private static bool TryAdd(long left, long right, out long value)
    {
        try
        {
            value = checked(left + right);
            return true;
        }
        catch (OverflowException)
        {
            value = default;
            return false;
        }
    }

    private static bool IsInBounds(long value, long? lowerBound, long? upperBound)
        => (!lowerBound.HasValue || value >= lowerBound.GetValueOrDefault())
            && (!upperBound.HasValue || value <= upperBound.GetValueOrDefault());

    private static bool IsInBounds(double value, double? lowerBound, double? upperBound)
        => (!lowerBound.HasValue || value >= lowerBound.GetValueOrDefault())
            && (!upperBound.HasValue || value <= upperBound.GetValueOrDefault());

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static long Clamp(long value, long? lowerBound, long? upperBound)
    {
        if (lowerBound.HasValue && value < lowerBound.GetValueOrDefault()) return lowerBound.GetValueOrDefault();
        if (upperBound.HasValue && value > upperBound.GetValueOrDefault()) return upperBound.GetValueOrDefault();
        return value;
    }

    private static double Clamp(double value, double? lowerBound, double? upperBound)
    {
        if (lowerBound.HasValue && value < lowerBound.GetValueOrDefault()) return lowerBound.GetValueOrDefault();
        if (upperBound.HasValue && value > upperBound.GetValueOrDefault()) return upperBound.GetValueOrDefault();
        return value;
    }

    private static TypedRedisValue MakeResult(long value, long appliedIncrement)
    {
        var result = TypedRedisValue.Rent(2, out var span, RespPrefix.Array);
        span[0] = TypedRedisValue.Integer(value);
        span[1] = TypedRedisValue.Integer(appliedIncrement);
        return result;
    }

    private static TypedRedisValue MakeResult(double value, double appliedIncrement)
    {
        var result = TypedRedisValue.Rent(2, out var span, RespPrefix.Array);
        span[0] = TypedRedisValue.Number(value);
        span[1] = TypedRedisValue.Number(appliedIncrement);
        return result;
    }

    public override void ResetCounters()
    {
        LastRequest = null;
        base.ResetCounters();
    }
}
