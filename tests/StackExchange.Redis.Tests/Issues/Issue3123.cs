using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests.Issues;

public class Issue3123(ITestOutputHelper output, SharedConnectionFixture? fixture) : TestBase(output, fixture)
{
    [Fact]
    public async Task Run()
    {
        await using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();
        await db.KeyDeleteAsync(key, flags: CommandFlags.FireAndForget);

        Guid guid = Guid.NewGuid();
        byte[] payload = guid.ToByteArray();

        await db.GeoAddAsync(
            key,
            longitude: -77.0365,
            latitude: 38.8977,
            member: payload,
            flags: CommandFlags.FireAndForget);

        GeoSearchCircle commonSearchCircle = new(1, GeoUnit.Kilometers);

        GeoRadiusResult[] results =
            await db.GeoSearchAsync(
                key,
                longitude: -77.0365,
                latitude: 38.8977,
                shape: commonSearchCircle);
        var result = Assert.Single(results);

        byte[] final = (byte[])result.Member!;
        Assert.True(final.SequenceEqual(payload));
        Assert.Equal(guid, new Guid(final));
    }
}
