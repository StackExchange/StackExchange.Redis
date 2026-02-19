using Xunit;

namespace StackExchange.Redis.Tests;

public partial class ResultProcessorUnitTests
{
    [Theory]
    [InlineData("*0\r\n")] // empty array
    [InlineData("*12\r\n$4\r\nsize\r\n:100\r\n$8\r\nvset-uid\r\n:42\r\n$9\r\nmax-level\r\n:5\r\n$10\r\nvector-dim\r\n:128\r\n$10\r\nquant-type\r\n$4\r\nint8\r\n$17\r\nhnsw-max-node-uid\r\n:99\r\n")] // full info with int8
    public void VectorSetInfo_ValidInput(string resp)
    {
        var processor = ResultProcessor.VectorSetInfo;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
    }

    [Fact]
    public void VectorSetInfo_EmptyArray_ReturnsDefaults()
    {
        // Empty array should return VectorSetInfo with default values
        var resp = "*0\r\n";
        var processor = ResultProcessor.VectorSetInfo;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
        Assert.Equal(VectorSetQuantization.Unknown, result.Value.Quantization);
        Assert.Null(result.Value.QuantizationRaw);
        Assert.Equal(0, result.Value.Dimension);
        Assert.Equal(0, result.Value.Length);
        Assert.Equal(0, result.Value.MaxLevel);
        Assert.Equal(0, result.Value.VectorSetUid);
        Assert.Equal(0, result.Value.HnswMaxNodeUid);
    }

    [Theory]
    [InlineData("*-1\r\n")] // null array (RESP2)
    [InlineData("_\r\n")] // null (RESP3)
    public void VectorSetInfo_NullArray(string resp)
    {
        var processor = ResultProcessor.VectorSetInfo;
        var result = Execute(resp, processor);

        Assert.Null(result);
    }

    [Fact]
    public void VectorSetInfo_ValidatesContent_Int8()
    {
        // VINFO response with int8 quantization
        var resp = "*12\r\n$4\r\nsize\r\n:100\r\n$8\r\nvset-uid\r\n:42\r\n$9\r\nmax-level\r\n:5\r\n$10\r\nvector-dim\r\n:128\r\n$10\r\nquant-type\r\n$4\r\nint8\r\n$17\r\nhnsw-max-node-uid\r\n:99\r\n";
        var processor = ResultProcessor.VectorSetInfo;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
        Assert.Equal(VectorSetQuantization.Int8, result.Value.Quantization);
        Assert.Null(result.Value.QuantizationRaw);
        Assert.Equal(128, result.Value.Dimension);
        Assert.Equal(100, result.Value.Length);
        Assert.Equal(5, result.Value.MaxLevel);
        Assert.Equal(42, result.Value.VectorSetUid);
        Assert.Equal(99, result.Value.HnswMaxNodeUid);
    }

    [Fact]
    public void VectorSetInfo_SkipsNonScalarValues()
    {
        // Response with a non-scalar value (array) that should be skipped
        // Format: size:100, unknown-field:[1,2,3], vset-uid:42
        var resp = "*6\r\n$4\r\nsize\r\n:100\r\n$13\r\nunknown-field\r\n*3\r\n:1\r\n:2\r\n:3\r\n$8\r\nvset-uid\r\n:42\r\n";
        var processor = ResultProcessor.VectorSetInfo;
        var result = Execute(resp, processor);

        Assert.NotNull(result);
        Assert.Equal(100, result.Value.Length);
        Assert.Equal(42, result.Value.VectorSetUid);
        // Other fields should have default values
        Assert.Equal(VectorSetQuantization.Unknown, result.Value.Quantization);
        Assert.Equal(0, result.Value.Dimension);
    }
}
