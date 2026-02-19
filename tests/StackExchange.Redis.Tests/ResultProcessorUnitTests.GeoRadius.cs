using Xunit;

namespace StackExchange.Redis.Tests;

public partial class ResultProcessorUnitTests
{
    [Fact]
    public void GeoRadius_None_ReturnsJustMembers()
    {
        // Without any WITH option: just member names as scalars in array
        var resp = "*2\r\n$7\r\nPalermo\r\n$7\r\nCatania\r\n";
        var result = Execute<GeoRadiusResult[]>(resp, ResultProcessor.GeoRadiusArray(GeoRadiusOptions.None));

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Equal("Palermo", result[0].Member);
        Assert.Null(result[0].Distance);
        Assert.Null(result[0].Hash);
        Assert.Null(result[0].Position);
        Assert.Equal("Catania", result[1].Member);
        Assert.Null(result[1].Distance);
        Assert.Null(result[1].Hash);
        Assert.Null(result[1].Position);
    }

    [Fact]
    public void GeoRadius_WithDistance_ReturnsDistances()
    {
        // With WITHDIST: each element is [member, distance]
        var resp = "*2\r\n" +
                   "*2\r\n$7\r\nPalermo\r\n$8\r\n190.4424\r\n" +
                   "*2\r\n$7\r\nCatania\r\n$7\r\n56.4413\r\n";
        var result = Execute<GeoRadiusResult[]>(resp, ResultProcessor.GeoRadiusArray(GeoRadiusOptions.WithDistance));

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Equal("Palermo", result[0].Member);
        Assert.Equal(190.4424, result[0].Distance);
        Assert.Null(result[0].Hash);
        Assert.Null(result[0].Position);
        Assert.Equal("Catania", result[1].Member);
        Assert.Equal(56.4413, result[1].Distance);
        Assert.Null(result[1].Hash);
        Assert.Null(result[1].Position);
    }

    [Fact]
    public void GeoRadius_WithCoordinates_ReturnsPositions()
    {
        // With WITHCOORD: each element is [member, [longitude, latitude]]
        var resp = "*2\r\n" +
                   "*2\r\n$7\r\nPalermo\r\n*2\r\n$18\r\n13.361389338970184\r\n$16\r\n38.1155563954963\r\n" +
                   "*2\r\n$7\r\nCatania\r\n*2\r\n$18\r\n15.087267458438873\r\n$17\r\n37.50266842333162\r\n";
        var result = Execute<GeoRadiusResult[]>(resp, ResultProcessor.GeoRadiusArray(GeoRadiusOptions.WithCoordinates));

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Equal("Palermo", result[0].Member);
        Assert.Null(result[0].Distance);
        Assert.Null(result[0].Hash);
        Assert.NotNull(result[0].Position);
        Assert.Equal(13.361389338970184, result[0].Position!.Value.Longitude);
        Assert.Equal(38.1155563954963, result[0].Position!.Value.Latitude);
        Assert.Equal("Catania", result[1].Member);
        Assert.Null(result[1].Distance);
        Assert.Null(result[1].Hash);
        Assert.NotNull(result[1].Position);
        Assert.Equal(15.087267458438873, result[1].Position!.Value.Longitude);
        Assert.Equal(37.50266842333162, result[1].Position!.Value.Latitude);
    }

    [Fact]
    public void GeoRadius_WithDistanceAndCoordinates_ReturnsBoth()
    {
        // With WITHDIST WITHCOORD: each element is [member, distance, [longitude, latitude]]
        var resp = "*2\r\n" +
                   "*3\r\n$7\r\nPalermo\r\n$8\r\n190.4424\r\n*2\r\n$18\r\n13.361389338970184\r\n$16\r\n38.1155563954963\r\n" +
                   "*3\r\n$7\r\nCatania\r\n$7\r\n56.4413\r\n*2\r\n$18\r\n15.087267458438873\r\n$17\r\n37.50266842333162\r\n";
        var result = Execute<GeoRadiusResult[]>(resp, ResultProcessor.GeoRadiusArray(GeoRadiusOptions.WithDistance | GeoRadiusOptions.WithCoordinates));

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Equal("Palermo", result[0].Member);
        Assert.Equal(190.4424, result[0].Distance);
        Assert.Null(result[0].Hash);
        Assert.NotNull(result[0].Position);
        Assert.Equal(13.361389338970184, result[0].Position!.Value.Longitude);
        Assert.Equal(38.1155563954963, result[0].Position!.Value.Latitude);
    }

    [Fact]
    public void GeoRadius_WithHash_ReturnsHash()
    {
        // With WITHHASH: each element is [member, hash]
        var resp = "*2\r\n" +
                   "*2\r\n$7\r\nPalermo\r\n:3479099956230698\r\n" +
                   "*2\r\n$7\r\nCatania\r\n:3479447370796909\r\n";
        var result = Execute<GeoRadiusResult[]>(resp, ResultProcessor.GeoRadiusArray(GeoRadiusOptions.WithGeoHash));

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Equal("Palermo", result[0].Member);
        Assert.Null(result[0].Distance);
        Assert.Equal(3479099956230698, result[0].Hash);
        Assert.Null(result[0].Position);
        Assert.Equal("Catania", result[1].Member);
        Assert.Null(result[1].Distance);
        Assert.Equal(3479447370796909, result[1].Hash);
        Assert.Null(result[1].Position);
    }

    [Fact]
    public void GeoRadius_AllOptions_ReturnsEverything()
    {
        // With all options: [member, distance, hash, [longitude, latitude]]
        var resp = "*1\r\n" +
                   "*4\r\n$7\r\nPalermo\r\n$8\r\n190.4424\r\n:3479099956230698\r\n*2\r\n$18\r\n13.361389338970184\r\n$16\r\n38.1155563954963\r\n";
        var result = Execute<GeoRadiusResult[]>(
            resp,
            ResultProcessor.GeoRadiusArray(GeoRadiusOptions.WithDistance | GeoRadiusOptions.WithGeoHash | GeoRadiusOptions.WithCoordinates));

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Palermo", result[0].Member);
        Assert.Equal(190.4424, result[0].Distance);
        Assert.Equal(3479099956230698, result[0].Hash);
        Assert.NotNull(result[0].Position);
        Assert.Equal(13.361389338970184, result[0].Position!.Value.Longitude);
        Assert.Equal(38.1155563954963, result[0].Position!.Value.Latitude);
    }

    [Fact]
    public void GeoRadius_EmptyArray_ReturnsEmptyArray()
    {
        var resp = "*0\r\n";
        var result = Execute<GeoRadiusResult[]>(resp, ResultProcessor.GeoRadiusArray(GeoRadiusOptions.None));

        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
