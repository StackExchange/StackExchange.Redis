extern alias respite;
using System.Globalization;
using respite::RESPite.Messages;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

public class IncrexTestServer(StringIncrementResult<string> expectedResult, ITestOutputHelper? log = null) : InProcessTestServer(log)
{
    public sealed class IncrexRequestSnapshot
    {
        public RedisKey Key { get; set; }
        public bool IsFloat { get; set; }
        public string Increment { get; set; } = "";
        public string? LowerBound { get; set; }
        public string? UpperBound { get; set; }
        public string? ExpiryMode { get; set; }
        public string? ExpiryValue { get; set; }
        public bool Enx { get; set; }
    }

    public IncrexRequestSnapshot? LastRequest { get; private set; }

    [RedisCommand(-4, "INCREX")]
    protected virtual TypedRedisValue Increx(RedisClient client, in RedisRequest request)
    {
        var snapshot = new IncrexRequestSnapshot
        {
            Key = request.GetKey(1),
        };

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
            }
        }

        LastRequest = snapshot;

        var result = TypedRedisValue.Rent(2, out var span, RespPrefix.Array);
        span[0] = TypedRedisValue.BulkString(expectedResult.Value);
        span[1] = TypedRedisValue.BulkString(expectedResult.AppliedIncrement);
        return result;
    }

    public override void ResetCounters()
    {
        LastRequest = null;
        base.ResetCounters();
    }
}
