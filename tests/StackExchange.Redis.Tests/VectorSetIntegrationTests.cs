using System;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
public sealed class VectorSetIntegrationTests : TestBase
{
    public VectorSetIntegrationTests(ITestOutputHelper output) : base(output) { }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VectorSetAdd_BasicOperation(bool suppressFp32)
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();

        // Clean up any existing data
        await db.KeyDeleteAsync(key);

        var vector = new float[] { 1.0f, 2.0f, 3.0f, 4.0f };

        if (suppressFp32) VectorSetAddMessage.SuppressFp32();
        try
        {
            var result = await db.VectorSetAddAsync(key, "element1", vector.AsMemory());

            Assert.True(result);
        }
        finally
        {
            if (suppressFp32) VectorSetAddMessage.RestoreFp32();
        }
    }

    [Fact]
    public async Task VectorSetAdd_WithAttributes()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key);

        var vector = new float[] { 1.0f, 2.0f, 3.0f, 4.0f };
        var attributes = """{"category":"test","id":123}""";

        var result = await db.VectorSetAddAsync(key, "element1", vector.AsMemory(), attributesJson: attributes);

        Assert.True(result);

        // Verify attributes were stored
        var retrievedAttributes = await db.VectorSetGetAttributesJsonAsync(key, "element1");
        Assert.Equal(attributes, retrievedAttributes);
    }

    [Fact]
    public async Task VectorSetLength_EmptySet()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key);

        var length = await db.VectorSetLengthAsync(key);
        Assert.Equal(0, length);
    }

    [Fact]
    public async Task VectorSetLength_WithElements()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key);

        var vector1 = new float[] { 1.0f, 2.0f, 3.0f };
        var vector2 = new float[] { 4.0f, 5.0f, 6.0f };

        await db.VectorSetAddAsync(key, "element1", vector1.AsMemory());
        await db.VectorSetAddAsync(key, "element2", vector2.AsMemory());

        var length = await db.VectorSetLengthAsync(key);
        Assert.Equal(2, length);
    }

    [Fact]
    public async Task VectorSetDimension()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key);

        var vector = new float[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f };
        await db.VectorSetAddAsync(key, "element1", vector.AsMemory());

        var dimension = await db.VectorSetDimensionAsync(key);
        Assert.Equal(5, dimension);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VectorSetContains(bool suppressFp32)
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key);

        var vector = new float[] { 1.0f, 2.0f, 3.0f };
        if (suppressFp32) VectorSetAddMessage.SuppressFp32();
        try
        {
            await db.VectorSetAddAsync(key, "element1", vector.AsMemory());

            var exists = await db.VectorSetContainsAsync(key, "element1");
            var notExists = await db.VectorSetContainsAsync(key, "element2");

            Assert.True(exists);
            Assert.False(notExists);
        }
        finally
        {
            if (suppressFp32) VectorSetAddMessage.RestoreFp32();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VectorSetGetApproximateVector(bool suppressFp32)
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key);

        var originalVector = new float[] { 1.0f, 2.0f, 3.0f, 4.0f };
        if (suppressFp32) VectorSetAddMessage.SuppressFp32();
        try
        {
            await db.VectorSetAddAsync(key, "element1", originalVector.AsMemory());

            using var retrievedLease = await db.VectorSetGetApproximateVectorAsync(key, "element1");

            Assert.NotNull(retrievedLease);
            var retrievedVector = retrievedLease.Span;

            Assert.Equal(originalVector.Length, retrievedVector.Length);
            // Note: Due to quantization, values might not be exactly equal
            for (int i = 0; i < originalVector.Length; i++)
            {
                Assert.True(
                    Math.Abs(originalVector[i] - retrievedVector[i]) < 0.1f,
                    $"Vector component {i} differs too much: expected {originalVector[i]}, got {retrievedVector[i]}");
            }
        }
        finally
        {
            if (suppressFp32) VectorSetAddMessage.RestoreFp32();
        }
    }

    [Fact]
    public async Task VectorSetRemove()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key);

        var vector = new float[] { 1.0f, 2.0f, 3.0f };
        await db.VectorSetAddAsync(key, "element1", vector.AsMemory());

        var removed = await db.VectorSetRemoveAsync(key, "element1");
        Assert.True(removed);

        removed = await db.VectorSetRemoveAsync(key, "element1");
        Assert.False(removed);

        var exists = await db.VectorSetContainsAsync(key, "element1");
        Assert.False(exists);
    }

    [Fact]
    public async Task VectorSetInfo()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key);

        var vector = new float[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f };
        await db.VectorSetAddAsync(key, "element1", vector.AsMemory());

        var info = await db.VectorSetInfoAsync(key);

        Assert.NotNull(info);
        Assert.Equal(5, info.Value.Dimension);
        Assert.Equal(1, info.Value.Length);
        Assert.Equal(VectorQuantizationType.Int8, info.Value.QuantizationType);
    }

    [Fact]
    public async Task VectorSetRandomMember()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key);

        var vector1 = new float[] { 1.0f, 2.0f, 3.0f };
        var vector2 = new float[] { 4.0f, 5.0f, 6.0f };

        await db.VectorSetAddAsync(key, "element1", vector1.AsMemory());
        await db.VectorSetAddAsync(key, "element2", vector2.AsMemory());

        var randomMember = await db.VectorSetRandomMemberAsync(key);
        Assert.True(randomMember == "element1" || randomMember == "element2");
    }

    [Fact]
    public async Task VectorSetRandomMembers()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key);

        var vector1 = new float[] { 1.0f, 2.0f, 3.0f };
        var vector2 = new float[] { 4.0f, 5.0f, 6.0f };
        var vector3 = new float[] { 7.0f, 8.0f, 9.0f };

        await db.VectorSetAddAsync(key, "element1", vector1.AsMemory());
        await db.VectorSetAddAsync(key, "element2", vector2.AsMemory());
        await db.VectorSetAddAsync(key, "element3", vector3.AsMemory());

        var randomMembers = await db.VectorSetRandomMembersAsync(key, 2);

        Assert.Equal(2, randomMembers.Length);
        Assert.All(randomMembers, member =>
            Assert.True(member == "element1" || member == "element2" || member == "element3"));
    }

    [Fact]
    public async Task VectorSetSimilaritySearch_WithVector()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key);

        // Add some test vectors
        var vector1 = new float[] { 1.0f, 0.0f, 0.0f };
        var vector2 = new float[] { 0.0f, 1.0f, 0.0f };
        var vector3 = new float[] { 0.9f, 0.1f, 0.0f }; // Similar to vector1

        await db.VectorSetAddAsync(key, "element1", vector1.AsMemory());
        await db.VectorSetAddAsync(key, "element2", vector2.AsMemory());
        await db.VectorSetAddAsync(key, "element3", vector3.AsMemory());

        // Search for vectors similar to vector1
        using var results =
            await db.VectorSetSimilaritySearchByVectorAsync(key, vector1.AsMemory(), count: 2, withScores: true);

        Assert.NotNull(results);
        var resultsArray = results.Span.ToArray();

        Assert.True(resultsArray.Length <= 2);
        Assert.Contains(resultsArray, r => r.Member == "element1");

        // Verify scores are present when withScores is true
        Assert.All(resultsArray, r => Assert.False(double.IsNaN(r.Score)));
    }

    [Fact]
    public async Task VectorSetSimilaritySearch_WithMember()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key);

        var vector1 = new float[] { 1.0f, 0.0f, 0.0f };
        var vector2 = new float[] { 0.0f, 1.0f, 0.0f };

        await db.VectorSetAddAsync(key, "element1", vector1.AsMemory());
        await db.VectorSetAddAsync(key, "element2", vector2.AsMemory());

        using var results =
            await db.VectorSetSimilaritySearchByMemberAsync(key, "element1", count: 1, withScores: true);

        Assert.NotNull(results);
        var resultsArray = results.Span.ToArray();

        Assert.Single(resultsArray);
        Assert.Equal("element1", resultsArray[0].Member);
        Assert.False(double.IsNaN(resultsArray[0].Score));
    }

    [Fact]
    public async Task VectorSetSimilaritySearch_WithAttributes()
    {
        using var conn = Create();
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key);

        var vector = new float[] { 1.0f, 2.0f, 3.0f };
        var attributes = """{"category":"test","priority":"high"}""";

        await db.VectorSetAddAsync(key, "element1", vector.AsMemory(), attributesJson: attributes);

        using var results = await db.VectorSetSimilaritySearchByVectorAsync(
            key,
            vector.AsMemory(),
            count: 1,
            withScores: true,
            withAttributes: true);

        Assert.NotNull(results);
        var result = results.Span[0];

        Assert.Equal("element1", result.Member);
        Assert.False(double.IsNaN(result.Score));
        Assert.Equal(attributes, result.AttributesJson);
    }
}
