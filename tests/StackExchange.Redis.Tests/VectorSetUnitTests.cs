using System;
using System.Threading.Tasks;
using StackExchange.Redis.Server;
using Xunit;

namespace StackExchange.Redis.Tests;

[Collection(NonParallelCollection.Name)] // because of the FP32 suppression
public sealed class VectorSetUnitTests(ITestOutputHelper output)
{
    // the aim of this test is to validate that we're sending the right thing - VADD is complex
    [Theory]
    [InlineData(VectorSetQuantization.Int8, false)]
    [InlineData(VectorSetQuantization.None, false)]
    [InlineData(VectorSetQuantization.Binary, false)]
    [InlineData(VectorSetQuantization.Int8, true)]
    [InlineData(VectorSetQuantization.None, true)]
    [InlineData(VectorSetQuantization.Binary, true)]
    public async Task VectorSetAdd_WithEverything(VectorSetQuantization quantization, bool disableFp32)
    {
        using var server = new VectorServer(output);
        await using var conn = await server.ConnectAsync();
        var db = conn.GetDatabase();
        var key = "mykey";

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f, 4.0f };
        var attributes = """{"category":"test","id":123}""";

        try
        {
            if (disableFp32) VectorSetAddMessage.SuppressFp32();
            Assert.Equal(!disableFp32, VectorSetAddMessage.UseFp32);
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
        }
        finally
        {
            if (disableFp32) VectorSetAddMessage.RestoreFp32();
        }

        // now: what did we send?
        var req = server.LastRequest.ReadRequest().AsSpan();

        output.WriteLine($"Request: * {req.Length}");
        foreach (var item in req)
        {
            output.WriteLine($"  $ '{item}'");
        }

        Assert.Equal("VADD", req[0]);
        Assert.Equal("mykey", req[1]);
        Assert.Equal("REDUCE", req[2]);
        Assert.Equal(64, req[3]);
        req = req.Slice(4);

        if (disableFp32)
        {
            Assert.Equal("VALUES", req[0]);
            Assert.Equal(4, req[1]);
            Assert.Equal(1.0f, (float)req[2], precision: 3);
            Assert.Equal(2.0f, (float)req[3], precision: 3);
            Assert.Equal(3.0f, (float)req[4], precision: 3);
            Assert.Equal(4.0f, (float)req[5], precision: 3);
            req = req.Slice(6);
        }
        else
        {
            Assert.Equal("FP32", req[0]);
            Assert.Equal("00-00-80-3F-00-00-00-40-00-00-40-40-00-00-80-40", BitConverter.ToString(req[1]!));
            req = req.Slice(2);
        }

        Assert.Equal("element1", req[0]);
        Assert.Equal("CAS", req[1]);
        req = req.Slice(2);

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
        req = req.Slice(6);

        Assert.True(req.IsEmpty);
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
