using System.Threading.Tasks;
using StackExchange.Redis;
using StackExchange.Redis.Configuration;
namespace BasicTest.ReplayLog;

public static class TestDataGenerator
{
    public static async Task GenerateAsync()
    {
        var options = ConfigurationOptions.Parse("127.0.0.1:6379,$subscribe=,");
#pragma warning disable SERED001
        LoggingTunnel.LogToDirectory(options, "ReplayLog");
#pragma warning restore SERED001
        using var muxer = await ConnectionMultiplexer.ConnectAsync(options);
        var db = muxer.GetDatabase();
        RedisKey key = "data";
        await db.KeyDeleteAsync(key);
        var block = new HashEntry[10000];
        for (int i = 0; i < block.Length; i++)
        {
            RedisValue name = $"field{i}", value = $"value{i}";
            await db.HashSetAsync(key, name, value);
            block[i] = new(name, value);
        }
        await db.HashSetAsync(key, block);
        _ = db.HashGetAllAsync(key);
    }
}
