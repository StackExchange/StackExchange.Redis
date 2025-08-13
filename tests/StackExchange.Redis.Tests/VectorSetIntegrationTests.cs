using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests;

[RunPerProtocol]
public sealed class VectorSetIntegrationTests(ITestOutputHelper output) : TestBase(output)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VectorSetAdd_BasicOperation(bool suppressFp32)
    {
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
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
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
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

    [Theory]
    [InlineData(VectorSetQuantization.Int8)]
    [InlineData(VectorSetQuantization.None)]
    [InlineData(VectorSetQuantization.Binary)]
    public async Task VectorSetAdd_WithEverything(VectorSetQuantization quantization)
    {
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key);

        var vector = new float[] { 1.0f, 2.0f, 3.0f, 4.0f };
        var attributes = """{"category":"test","id":123}""";

        var result = await db.VectorSetAddAsync(
            key,
            "element1",
            vector.AsMemory(),
            attributesJson: attributes,
            useCheckAndSet: true,
            quantization: quantization,
            reducedDimensions: 64,
            buildExplorationFactor: 300,
            maxConnections: 32);

        Assert.True(result);

        // Verify attributes were stored
        var retrievedAttributes = await db.VectorSetGetAttributesJsonAsync(key, "element1");
        Assert.Equal(attributes, retrievedAttributes);
    }

    [Fact]
    public async Task VectorSetLength_EmptySet()
    {
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key);

        var length = await db.VectorSetLengthAsync(key);
        Assert.Equal(0, length);
    }

    [Fact]
    public async Task VectorSetLength_WithElements()
    {
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
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
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
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
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
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
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
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
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
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

    [Theory]
    [InlineData(VectorSetQuantization.Int8)]
    [InlineData(VectorSetQuantization.Binary)]
    [InlineData(VectorSetQuantization.None)]
    public async Task VectorSetInfo(VectorSetQuantization quantization)
    {
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key);

        var vector = new float[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f };
        await db.VectorSetAddAsync(key, "element1", vector.AsMemory(), quantization: quantization);

        var info = await db.VectorSetInfoAsync(key);

        Assert.NotNull(info);
        var v = info.GetValueOrDefault();
        Assert.Equal(5, v.Dimension);
        Assert.Equal(1, v.Length);
        Assert.Equal(quantization, v.Quantization);
        Assert.Null(v.QuantizationRaw); // Should be null for known quant types

        Assert.NotEqual(0, v.VectorSetUid);
        Assert.NotEqual(0, v.HnswMaxNodeUid);
    }

    [Fact]
    public async Task VectorSetRandomMember()
    {
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
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
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task VectorSetSimilaritySearch_ByVector(bool withScores, bool withAttributes)
    {
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
        var db = conn.GetDatabase();
        var disambiguator = (withScores ? 1 : 0) + (withAttributes ? 2 : 0);
        var key = Me() + disambiguator;

        await db.KeyDeleteAsync(key);

        // Add some test vectors
        var vector1 = new float[] { 1.0f, 0.0f, 0.0f };
        var vector2 = new float[] { 0.0f, 1.0f, 0.0f };
        var vector3 = new float[] { 0.9f, 0.1f, 0.0f }; // Similar to vector1

        await db.VectorSetAddAsync(key, "element1", vector1.AsMemory(), attributesJson: """{"category":"x"}""");
        await db.VectorSetAddAsync(key, "element2", vector2.AsMemory(), attributesJson: """{"category":"y"}""");
        await db.VectorSetAddAsync(key, "element3", vector3.AsMemory(), attributesJson: """{"category":"z"}""");

        // Search for vectors similar to vector1
        using var results =
            await db.VectorSetSimilaritySearchByVectorAsync(
                key,
                vector1.AsMemory(),
                count: 2,
                withScores: withScores,
                withAttributes: withAttributes);

        Assert.NotNull(results);
        foreach (var result in results.Span)
        {
            Log(result.ToString());
        }
        var resultsArray = results.Span.ToArray();

        Assert.True(resultsArray.Length <= 2);
        Assert.Contains(resultsArray, r => r.Member == "element1");
        var found = resultsArray.First(r => r.Member == "element1");

        if (withAttributes)
        {
            Assert.Equal("""{"category":"x"}""", found.AttributesJson);
        }
        else
        {
            Assert.Null(found.AttributesJson);
        }

        Assert.NotEqual(withScores, double.IsNaN(found.Score));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task VectorSetSimilaritySearch_ByMember(bool withScores, bool withAttributes)
    {
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
        var db = conn.GetDatabase();
        var disambiguator = (withScores ? 1 : 0) + (withAttributes ? 2 : 0);
        var key = Me() + disambiguator;

        await db.KeyDeleteAsync(key);

        var vector1 = new float[] { 1.0f, 0.0f, 0.0f };
        var vector2 = new float[] { 0.0f, 1.0f, 0.0f };

        await db.VectorSetAddAsync(key, "element1", vector1.AsMemory(), attributesJson: """{"category":"x"}""");
        await db.VectorSetAddAsync(key, "element2", vector2.AsMemory(), attributesJson: """{"category":"y"}""");

        using var results =
            await db.VectorSetSimilaritySearchByMemberAsync(
                key,
                "element1",
                count: 1,
                withScores: withScores,
                withAttributes: withAttributes);

        Assert.NotNull(results);
        foreach (var result in results.Span)
        {
            Log(result.ToString());
        }
        var resultsArray = results.Span.ToArray();

        Assert.Single(resultsArray);
        Assert.Equal("element1", resultsArray[0].Member);
        if (withAttributes)
        {
            Assert.Equal("""{"category":"x"}""", resultsArray[0].AttributesJson);
        }
        else
        {
            Assert.Null(resultsArray[0].AttributesJson);
        }

        Assert.NotEqual(withScores, double.IsNaN(resultsArray[0].Score));
    }

    [Fact]
    public async Task VectorSetSetAttributesJson()
    {
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key);

        var vector = new float[] { 1.0f, 2.0f, 3.0f };
        await db.VectorSetAddAsync(key, "element1", vector.AsMemory());

        // Set attributes for existing element
        var attributes = """{"category":"updated","priority":"high","timestamp":"2024-01-01"}""";
        var result = await db.VectorSetSetAttributesJsonAsync(key, "element1", attributes);

        Assert.True(result);

        // Verify attributes were set
        var retrievedAttributes = await db.VectorSetGetAttributesJsonAsync(key, "element1");
        Assert.Equal(attributes, retrievedAttributes);

        // Try setting attributes for non-existent element
        var failResult = await db.VectorSetSetAttributesJsonAsync(key, "nonexistent", attributes);
        Assert.False(failResult);
    }

    [Fact]
    public async Task VectorSetGetLinks()
    {
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key);

        // Add some vectors that should be linked
        var vector1 = new float[] { 1.0f, 0.0f, 0.0f };
        var vector2 = new float[] { 0.9f, 0.1f, 0.0f }; // Similar to vector1
        var vector3 = new float[] { 0.0f, 1.0f, 0.0f }; // Different from vector1

        await db.VectorSetAddAsync(key, "element1", vector1.AsMemory());
        await db.VectorSetAddAsync(key, "element2", vector2.AsMemory());
        await db.VectorSetAddAsync(key, "element3", vector3.AsMemory());

        // Get links for element1 (should include similar vectors)
        using var links = await db.VectorSetGetLinksAsync(key, "element1");

        Assert.NotNull(links);
        foreach (var link in links.Span)
        {
            Log(link.ToString());
        }

        var linksArray = links.Span.ToArray();

        // Should contain the other elements (note there can be transient duplicates, so: contains, not exact)
        Assert.Contains("element2", linksArray);
        Assert.Contains("element3", linksArray);
    }

    [Fact]
    public async Task VectorSetGetLinksWithScores()
    {
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key);

        // Add some vectors with known relationships
        var vector1 = new float[] { 1.0f, 0.0f, 0.0f };
        var vector2 = new float[] { 0.9f, 0.1f, 0.0f }; // Similar to vector1
        var vector3 = new float[] { 0.0f, 1.0f, 0.0f }; // Different from vector1

        await db.VectorSetAddAsync(key, "element1", vector1.AsMemory());
        await db.VectorSetAddAsync(key, "element2", vector2.AsMemory());
        await db.VectorSetAddAsync(key, "element3", vector3.AsMemory());

        // Get links with scores for element1
        using var linksWithScores = await db.VectorSetGetLinksWithScoresAsync(key, "element1");
        Assert.NotNull(linksWithScores);
        foreach (var link in linksWithScores.Span)
        {
            Log(link.ToString());
        }

        var linksArray = linksWithScores.Span.ToArray();
        Assert.NotEmpty(linksArray);

        // Verify each link has a valid score
        Assert.All(linksArray, link =>
        {
            Assert.False(link.Member.IsNull);
            Assert.False(double.IsNaN(link.Score));
            Assert.True(link.Score >= 0.0); // Similarity scores should be non-negative
        });

        // Should contain the other elements (note there can be transient duplicates, so: contains, not exact)
        Assert.Contains(linksArray, l => l.Member == "element2");
        Assert.Contains(linksArray, l => l.Member == "element3");

        Assert.True(linksArray.First(l => l.Member == "element2").Score > 0.9); // similar
        Assert.True(linksArray.First(l => l.Member == "element3").Score < 0.8); // less-so
    }
}
