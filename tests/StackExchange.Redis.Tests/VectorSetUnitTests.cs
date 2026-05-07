using System;
using System.Threading.Tasks;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
public sealed class VectorSetUnitTests(ITestOutputHelper output)
{
    // the aim of this test is to validate that we're sending the right thing - VADD is complex
    [Theory]
    [InlineData(VectorSetQuantization.Int8)]
    [InlineData(VectorSetQuantization.None)]
    [InlineData(VectorSetQuantization.Binary)]
    public async Task VectorSetAdd_WithEverything(VectorSetQuantization quantization)
    {
        using var server = new VectorServer(output);
        await using var conn = await server.ConnectAsync();
        var db = conn.GetDatabase();
        var key = "mykey";

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f, 4.0f };
        var attributes = """{"category":"test","id":123}""";

        var request = VectorSetAddRequest.Member(
            "element1",
            vector.AsMemory(),
            attributes);
        request.Quantization = quantization;
        request.ReducedDimensions = 64;
        request.BuildExplorationFactor = 300;
        request.MaxConnections = 32;
        request.UseCheckAndSet = true;
        output.WriteLine("Storing...");
        var result = await db.VectorSetAddAsync(
            key,
            request);
        Assert.True(result);

        // now: what did we send?
        var req = server.LastRequest.ReadRequest().AsSpan();

        output.WriteLine($"Request: * {req.Length}");
        foreach (var item in req)
        {
            output.WriteLine($"  $ '{item}'");
        }
        Assert.Equal(quantization is VectorSetQuantization.Int8 ? 14 : 15, req.Length);
        Assert.Equal("VADD", req[0]);
        Assert.Equal("mykey", req[1]);
        Assert.Equal("REDUCE", req[2]);
        Assert.Equal(64, req[3]);
        Assert.Equal("FP32", req[4]);
        Assert.Equal("00-00-80-3F-00-00-00-40-00-00-40-40-00-00-80-40", BitConverter.ToString(req[5]!));
        Assert.Equal("element1", req[6]);
        Assert.Equal("CAS", req[7]);

        req = req.Slice(8);
        switch (quantization)
        {
            case VectorSetQuantization.None:
                Assert.Equal("NOQUANT", req[0]);
                req = req.Slice(1);
                break;
            case VectorSetQuantization.Binary:
                Assert.Equal("BIN", req[0]);
                req = req.Slice(1);
                break;
        }
        Assert.Equal("EF", req[0]);
        Assert.Equal(300, req[1]);
        Assert.Equal("SETATTR", req[2]);
        Assert.Equal("""{"category":"test","id":123}""", req[3]);
        Assert.Equal("M", req[4]);
        Assert.Equal(32, req[5]);
    }

    private sealed class VectorServer(ITestOutputHelper log) : InProcessTestServer(log)
    {
        public TypedRedisValue LastRequest { get; private set; } = TypedRedisValue.Nil;

        [RedisCommand(-1)]
        private TypedRedisValue Vadd(RedisClient client, in RedisRequest request)
        {
            LastRequest = request.AsResponse();
            return TypedRedisValue.Integer(1); // spoof success
        }
    }
}
