using Xunit;

namespace StackExchange.Redis.Tests;

public partial class ResultProcessorUnitTests
{
    [Fact]
    public void GeoPosition_ValidPosition_ReturnsGeoPosition()
    {
        var resp = "*1\r\n*2\r\n$18\r\n13.361389338970184\r\n$16\r\n38.1155563954963\r\n";
        var processor = ResultProcessor.RedisGeoPosition;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
        Assert.Equal(13.361389338970184, result.Value.Longitude, 10);
        Assert.Equal(38.1155563954963, result.Value.Latitude, 10);
    }

    [Fact]
    public void GeoPosition_NullElement_ReturnsNull()
    {
        var resp = "*1\r\n$-1\r\n";
        var processor = ResultProcessor.RedisGeoPosition;
        var result = Execute(resp, processor);

        Assert.Null(result);
    }

    [Fact]
    public void GeoPosition_EmptyArray_ReturnsNull()
    {
        var resp = "*0\r\n";
        var processor = ResultProcessor.RedisGeoPosition;
        var result = Execute(resp, processor);

        Assert.Null(result);
    }

    [Fact]
    public void GeoPosition_NullArray_ReturnsNull()
    {
        var resp = "*-1\r\n";
        var processor = ResultProcessor.RedisGeoPosition;
        var result = Execute(resp, processor);

        Assert.Null(result);
    }

    [Fact]
    public void GeoPosition_IntegerCoordinates_ReturnsGeoPosition()
    {
        var resp = "*1\r\n*2\r\n:13\r\n:38\r\n";
        var processor = ResultProcessor.RedisGeoPosition;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
        Assert.Equal(13.0, result.Value.Longitude);
        Assert.Equal(38.0, result.Value.Latitude);
    }

    [Fact]
    public void GeoPositionArray_MultiplePositions_ReturnsArray()
    {
        var resp = "*3\r\n" +
                   "*2\r\n$18\r\n13.361389338970184\r\n$16\r\n38.1155563954963\r\n" +
                   "*2\r\n$18\r\n15.087267458438873\r\n$17\r\n37.50266842333162\r\n" +
                   "$-1\r\n";
        var processor = ResultProcessor.RedisGeoPositionArray;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
        Assert.Equal(3, result.Length);

        Assert.NotNull(result[0]);
        Assert.Equal(13.361389338970184, result[0]!.Value.Longitude, 10);
        Assert.Equal(38.1155563954963, result[0]!.Value.Latitude, 10);

        Assert.NotNull(result[1]);
        Assert.Equal(15.087267458438873, result[1]!.Value.Longitude, 10);
        Assert.Equal(37.50266842333162, result[1]!.Value.Latitude, 10);

        Assert.Null(result[2]);
    }

    [Fact]
    public void GeoPositionArray_EmptyArray_ReturnsEmptyArray()
    {
        var resp = "*0\r\n";
        var processor = ResultProcessor.RedisGeoPositionArray;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void GeoPositionArray_NullArray_ReturnsNull()
    {
        var resp = "*-1\r\n";
        var processor = ResultProcessor.RedisGeoPositionArray;
        var result = Execute(resp, processor);

        Assert.Null(result);
    }

    [Fact]
    public void GeoPositionArray_AllNulls_ReturnsArrayOfNulls()
    {
        var resp = "*2\r\n$-1\r\n$-1\r\n";
        var processor = ResultProcessor.RedisGeoPositionArray;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Null(result[0]);
        Assert.Null(result[1]);
    }
}
