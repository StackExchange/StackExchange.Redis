using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f, 4.0f };

        if (suppressFp32) VectorSetAddMessage.SuppressFp32();
        try
        {
            var request = VectorSetAddRequest.Member("element1", vector.AsMemory(), null);
            var result = await db.VectorSetAddAsync(key, request);

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

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f, 4.0f };
        var attributes = """{"category":"test","id":123}""";

        var request = VectorSetAddRequest.Member("element1", vector.AsMemory(), attributes);
        var result = await db.VectorSetAddAsync(key, request);

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
        var result = await db.VectorSetAddAsync(
            key,
            request);

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

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var length = await db.VectorSetLengthAsync(key);
        Assert.Equal(0, length);
    }

    [Fact]
    public async Task VectorSetLength_WithElements()
    {
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector1 = new[] { 1.0f, 2.0f, 3.0f };
        var vector2 = new[] { 4.0f, 5.0f, 6.0f };

        var request = VectorSetAddRequest.Member("element1", vector1.AsMemory());
        await db.VectorSetAddAsync(key, request);
        request = VectorSetAddRequest.Member("element2", vector2.AsMemory());
        await db.VectorSetAddAsync(key, request);

        var length = await db.VectorSetLengthAsync(key);
        Assert.Equal(2, length);
    }

    [Fact]
    public async Task VectorSetDimension()
    {
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f };
        var request = VectorSetAddRequest.Member("element1", vector.AsMemory());
        await db.VectorSetAddAsync(key, request);

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

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f };
        if (suppressFp32) VectorSetAddMessage.SuppressFp32();
        try
        {
            var request = VectorSetAddRequest.Member("element1", vector.AsMemory());
            await db.VectorSetAddAsync(key, request);

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

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var originalVector = new[] { 1.0f, 2.0f, 3.0f, 4.0f };
        if (suppressFp32) VectorSetAddMessage.SuppressFp32();
        try
        {
            var request = VectorSetAddRequest.Member("element1", originalVector.AsMemory());
            await db.VectorSetAddAsync(key, request);

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

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f };
        var request = VectorSetAddRequest.Member("element1", vector.AsMemory());
        await db.VectorSetAddAsync(key, request);

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

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f };
        var request = VectorSetAddRequest.Member("element1", vector.AsMemory());
        request.Quantization = quantization;
        await db.VectorSetAddAsync(key, request);

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

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector1 = new[] { 1.0f, 2.0f, 3.0f };
        var vector2 = new[] { 4.0f, 5.0f, 6.0f };

        var request = VectorSetAddRequest.Member("element1", vector1.AsMemory());
        await db.VectorSetAddAsync(key, request);
        request = VectorSetAddRequest.Member("element2", vector2.AsMemory());
        await db.VectorSetAddAsync(key, request);

        var randomMember = await db.VectorSetRandomMemberAsync(key);
        Assert.True(randomMember == "element1" || randomMember == "element2");
    }

    [Fact]
    public async Task VectorSetRandomMembers()
    {
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector1 = new[] { 1.0f, 2.0f, 3.0f };
        var vector2 = new[] { 4.0f, 5.0f, 6.0f };
        var vector3 = new[] { 7.0f, 8.0f, 9.0f };

        var request = VectorSetAddRequest.Member("element1", vector1.AsMemory());
        await db.VectorSetAddAsync(key, request);
        request = VectorSetAddRequest.Member("element2", vector2.AsMemory());
        await db.VectorSetAddAsync(key, request);
        request = VectorSetAddRequest.Member("element3", vector3.AsMemory());
        await db.VectorSetAddAsync(key, request);

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

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        // Add some test vectors
        var vector1 = new[] { 1.0f, 0.0f, 0.0f };
        var vector2 = new[] { 0.0f, 1.0f, 0.0f };
        var vector3 = new[] { 0.9f, 0.1f, 0.0f }; // Similar to vector1

        var request =
            VectorSetAddRequest.Member("element1", vector1.AsMemory(), attributesJson: """{"category":"x"}""");
        await db.VectorSetAddAsync(key, request);
        request = VectorSetAddRequest.Member("element2", vector2.AsMemory(), attributesJson: """{"category":"y"}""");
        await db.VectorSetAddAsync(key, request);
        request = VectorSetAddRequest.Member("element3", vector3.AsMemory(), attributesJson: """{"category":"z"}""");
        await db.VectorSetAddAsync(key, request);

        // Search for vectors similar to vector1
        var query = VectorSetSimilaritySearchRequest.ByVector(vector1.AsMemory());
        query.Count = 2;
        query.WithScores = withScores;
        query.WithAttributes = withAttributes;
        using var results = await db.VectorSetSimilaritySearchAsync(key, query);

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

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector1 = new[] { 1.0f, 0.0f, 0.0f };
        var vector2 = new[] { 0.0f, 1.0f, 0.0f };

        var request =
            VectorSetAddRequest.Member("element1", vector1.AsMemory(), attributesJson: """{"category":"x"}""");
        await db.VectorSetAddAsync(key, request);
        request = VectorSetAddRequest.Member("element2", vector2.AsMemory(), attributesJson: """{"category":"y"}""");
        await db.VectorSetAddAsync(key, request);

        var query = VectorSetSimilaritySearchRequest.ByMember("element1");
        query.Count = 1;
        query.WithScores = withScores;
        query.WithAttributes = withAttributes;
        using var results = await db.VectorSetSimilaritySearchAsync(key, query);

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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task VectorSetSimilaritySearch_WithFilter(bool corruptPrefix, bool corruptSuffix)
    {
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        Random rand = new Random();

        float[] vector = new float[50];

        void ScrambleVector()
        {
            var arr = vector;
            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = (float)rand.NextDouble();
            }
        }

        string[] regions = new[] { "us-west", "us-east", "eu-west", "eu-east", "ap-south", "ap-north" };
        for (int i = 0; i < 100; i++)
        {
            var region = regions[rand.Next(regions.Length)];
            var json = (corruptPrefix ? "oops" : "")
                       + JsonConvert.SerializeObject(new { id = i, region })
                       + (corruptSuffix ? "oops" : "");
            ScrambleVector();
            var request = VectorSetAddRequest.Member($"element{i}", vector.AsMemory(), json);
            await db.VectorSetAddAsync(key, request);
        }

        ScrambleVector();
        var query = VectorSetSimilaritySearchRequest.ByVector(vector);
        query.Count = 100;
        query.WithScores = true;
        query.WithAttributes = true;
        query.FilterExpression = ".id >= 30";
        using var results = await db.VectorSetSimilaritySearchAsync(key, query);

        Assert.NotNull(results);
        foreach (var result in results.Span)
        {
            Log(result.ToString());
        }

        Log($"Total matches: {results.Span.Length}");

        var resultsArray = results.Span.ToArray();
        if (corruptPrefix)
        {
            // server short-circuits failure to be no match; we just want to assert
            // what the observed behavior *is*
            Assert.Empty(resultsArray);
        }
        else
        {
            Assert.Equal(70, resultsArray.Length);
            Assert.All(resultsArray, r => Assert.True(
                r.Score is > 0.0 and < 1.0 && GetId(r.Member!) >= 30));
        }

        static int GetId(string member)
        {
            if (member.StartsWith("element"))
            {
                return int.Parse(member.Substring(7), NumberStyles.Integer, CultureInfo.InvariantCulture);
            }

            return -1;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData(".id >= 30")]
    public async Task VectorSetSimilaritySearch_TestFilterValues(string? filterExpression)
    {
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        Random rand = new Random();

        float[] vector = new float[50];

        void ScrambleVector()
        {
            var arr = vector;
            for (int i = 0; i < arr.Length; i++)
            {
                arr[i] = (float)rand.NextDouble();
            }
        }

        string[] regions = new[] { "us-west", "us-east", "eu-west", "eu-east", "ap-south", "ap-north" };
        for (int i = 0; i < 100; i++)
        {
            var region = regions[rand.Next(regions.Length)];
            var json = JsonConvert.SerializeObject(new { id = i, region });
            ScrambleVector();
            var request = VectorSetAddRequest.Member($"element{i}", vector.AsMemory(), json);
            await db.VectorSetAddAsync(key, request);
        }

        ScrambleVector();
        var query = VectorSetSimilaritySearchRequest.ByVector(vector);
        query.Count = 100;
        query.WithScores = true;
        query.WithAttributes = true;
        query.FilterExpression = filterExpression;

        using var results = await db.VectorSetSimilaritySearchAsync(key, query);

        Assert.NotNull(results);
        foreach (var result in results.Span)
        {
            Log(result.ToString());
        }

        Log($"Total matches: {results.Span.Length}");
        // we're not interested in the specific results; we're just checking that the
        // filter expression was added and parsed without exploding about arg mismatch
    }

    [Fact]
    public async Task VectorSetSetAttributesJson()
    {
        await using var conn = Create(require: RedisFeatures.v8_0_0_M04);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f };
        var request = VectorSetAddRequest.Member("element1", vector.AsMemory());
        await db.VectorSetAddAsync(key, request);

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

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        // Add some vectors that should be linked
        var vector1 = new[] { 1.0f, 0.0f, 0.0f };
        var vector2 = new[] { 0.9f, 0.1f, 0.0f }; // Similar to vector1
        var vector3 = new[] { 0.0f, 1.0f, 0.0f }; // Different from vector1

        var request = VectorSetAddRequest.Member("element1", vector1.AsMemory());
        await db.VectorSetAddAsync(key, request);
        request = VectorSetAddRequest.Member("element2", vector2.AsMemory());
        await db.VectorSetAddAsync(key, request);
        request = VectorSetAddRequest.Member("element3", vector3.AsMemory());
        await db.VectorSetAddAsync(key, request);

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

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        // Add some vectors with known relationships
        var vector1 = new[] { 1.0f, 0.0f, 0.0f };
        var vector2 = new[] { 0.9f, 0.1f, 0.0f }; // Similar to vector1
        var vector3 = new[] { 0.0f, 1.0f, 0.0f }; // Different from vector1

        var request = VectorSetAddRequest.Member("element1", vector1.AsMemory());
        await db.VectorSetAddAsync(key, request);
        request = VectorSetAddRequest.Member("element2", vector2.AsMemory());
        await db.VectorSetAddAsync(key, request);
        request = VectorSetAddRequest.Member("element3", vector3.AsMemory());
        await db.VectorSetAddAsync(key, request);

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
        // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
        Assert.All(linksArray, static link =>
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

    [Fact]
    public async Task VectorSetRange_BasicOperation()
    {
        await using var conn = Create(require: RedisFeatures.v8_4_0_rc1);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        // Add members with lexicographically ordered names
        var vector = new[] { 1.0f, 2.0f, 3.0f };
        var members = new[] { "alpha", "beta", "delta", "gamma" }; // note: delta before gamma because lexicographical

        foreach (var member in members)
        {
            var request = VectorSetAddRequest.Member(member, vector.AsMemory());
            await db.VectorSetAddAsync(key, request);
        }

        // Get all members - should be in lexicographical order
        using var result = await db.VectorSetRangeAsync(key);

        Assert.NotNull(result);
        Assert.Equal(4, result.Length);
        // Lexicographical order: alpha, beta, delta, gamma
        Assert.Equal(new[] { "alpha", "beta", "delta", "gamma" }, result.Span.ToArray().Select(r => (string?)r).ToArray());
    }

    [Fact]
    public async Task VectorSetRange_WithStartAndEnd()
    {
        await using var conn = Create(require: RedisFeatures.v8_4_0_rc1);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f };
        var members = new[] { "apple", "banana", "cherry", "date", "elderberry" };

        foreach (var member in members)
        {
            var request = VectorSetAddRequest.Member(member, vector.AsMemory());
            await db.VectorSetAddAsync(key, request);
        }

        // Get range from "banana" to "date" (inclusive)
        using var result = await db.VectorSetRangeAsync(key, start: "banana", end: "date");

        Assert.NotNull(result);
        Assert.Equal(3, result.Length);
        Assert.Equal(new[] { "banana", "cherry", "date" }, result.Span.ToArray().Select(r => (string?)r).ToArray());
    }

    [Fact]
    public async Task VectorSetRange_WithCount()
    {
        await using var conn = Create(require: RedisFeatures.v8_4_0_rc1);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f };

        // Add 10 members
        for (int i = 0; i < 10; i++)
        {
            var request = VectorSetAddRequest.Member($"member{i}", vector.AsMemory());
            await db.VectorSetAddAsync(key, request);
        }

        // Get only 5 members
        using var result = await db.VectorSetRangeAsync(key, count: 5);

        Assert.NotNull(result);
        Assert.Equal(5, result.Length);
    }

    [Fact]
    public async Task VectorSetRange_WithExcludeStart()
    {
        await using var conn = Create(require: RedisFeatures.v8_4_0_rc1);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f };
        var members = new[] { "a", "b", "c", "d" };

        foreach (var member in members)
        {
            var request = VectorSetAddRequest.Member(member, vector.AsMemory());
            await db.VectorSetAddAsync(key, request);
        }

        // Get range excluding start
        using var result = await db.VectorSetRangeAsync(key, start: "a", end: "d", exclude: Exclude.Start);

        Assert.NotNull(result);
        Assert.Equal(3, result.Length);
        Assert.Equal(new[] { "b", "c", "d" }, result.Span.ToArray().Select(r => (string?)r).ToArray());
    }

    [Fact]
    public async Task VectorSetRange_WithExcludeEnd()
    {
        await using var conn = Create(require: RedisFeatures.v8_4_0_rc1);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f };
        var members = new[] { "a", "b", "c", "d" };

        foreach (var member in members)
        {
            var request = VectorSetAddRequest.Member(member, vector.AsMemory());
            await db.VectorSetAddAsync(key, request);
        }

        // Get range excluding end
        using var result = await db.VectorSetRangeAsync(key, start: "a", end: "d", exclude: Exclude.Stop);

        Assert.NotNull(result);
        Assert.Equal(3, result.Length);
        Assert.Equal(new[] { "a", "b", "c" }, result.Span.ToArray().Select(r => (string?)r).ToArray());
    }

    [Fact]
    public async Task VectorSetRange_WithExcludeBoth()
    {
        await using var conn = Create(require: RedisFeatures.v8_4_0_rc1);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f };
        var members = new[] { "a", "b", "c", "d", "e" };

        foreach (var member in members)
        {
            var request = VectorSetAddRequest.Member(member, vector.AsMemory());
            await db.VectorSetAddAsync(key, request);
        }

        // Get range excluding both boundaries
        using var result = await db.VectorSetRangeAsync(key, start: "a", end: "e", exclude: Exclude.Both);

        Assert.NotNull(result);
        Assert.Equal(3, result.Length);
        Assert.Equal(new[] { "b", "c", "d" }, result.Span.ToArray().Select(r => (string?)r).ToArray());
    }

    [Fact]
    public async Task VectorSetRange_EmptySet()
    {
        await using var conn = Create(require: RedisFeatures.v8_4_0_rc1);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        // Don't add any members
        using var result = await db.VectorSetRangeAsync(key);

        Assert.NotNull(result);
        Assert.Empty(result.Span.ToArray());
    }

    [Fact]
    public async Task VectorSetRange_NoMatches()
    {
        await using var conn = Create(require: RedisFeatures.v8_4_0_rc1);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f };
        var members = new[] { "a", "b", "c" };

        foreach (var member in members)
        {
            var request = VectorSetAddRequest.Member(member, vector.AsMemory());
            await db.VectorSetAddAsync(key, request);
        }

        // Query range with no matching members
        using var result = await db.VectorSetRangeAsync(key, start: "x", end: "z");

        Assert.NotNull(result);
        Assert.Empty(result.Span.ToArray());
    }

    [Fact]
    public async Task VectorSetRange_OpenStart()
    {
        await using var conn = Create(require: RedisFeatures.v8_4_0_rc1);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f };
        var members = new[] { "alpha", "beta", "gamma" };

        foreach (var member in members)
        {
            var request = VectorSetAddRequest.Member(member, vector.AsMemory());
            await db.VectorSetAddAsync(key, request);
        }

        // Get from beginning to "beta"
        using var result = await db.VectorSetRangeAsync(key, end: "beta");

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Equal(new[] { "alpha", "beta" }, result.Span.ToArray().Select(r => (string?)r).ToArray());
    }

    [Fact]
    public async Task VectorSetRange_OpenEnd()
    {
        await using var conn = Create(require: RedisFeatures.v8_4_0_rc1);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f };
        var members = new[] { "alpha", "beta", "gamma" };

        foreach (var member in members)
        {
            var request = VectorSetAddRequest.Member(member, vector.AsMemory());
            await db.VectorSetAddAsync(key, request);
        }

        // Get from "beta" to end
        using var result = await db.VectorSetRangeAsync(key, start: "beta");

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Equal(new[] { "beta", "gamma" }, result.Span.ToArray().Select(r => (string?)r).ToArray());
    }

    [Fact]
    public async Task VectorSetRange_SyncVsAsync()
    {
        await using var conn = Create(require: RedisFeatures.v8_4_0_rc1);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f };

        // Add 20 members
        for (int i = 0; i < 20; i++)
        {
            var request = VectorSetAddRequest.Member($"m{i:D2}", vector.AsMemory());
            await db.VectorSetAddAsync(key, request);
        }

        // Call both sync and async
        using var syncResult = db.VectorSetRange(key, start: "m05", end: "m15");
        using var asyncResult = await db.VectorSetRangeAsync(key, start: "m05", end: "m15");

        Assert.NotNull(syncResult);
        Assert.NotNull(asyncResult);
        Assert.Equal(syncResult.Length, asyncResult.Length);
        Assert.Equal(syncResult.Span.ToArray().Select(r => (string?)r), asyncResult.Span.ToArray().Select(r => (string?)r));
    }

    [Fact]
    public async Task VectorSetRange_WithNumericLexOrder()
    {
        await using var conn = Create(require: RedisFeatures.v8_4_0_rc1);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f };
        var members = new[] { "1", "10", "2", "20", "3" };

        foreach (var member in members)
        {
            var request = VectorSetAddRequest.Member(member, vector.AsMemory());
            await db.VectorSetAddAsync(key, request);
        }

        // Get all - should be in lexicographical order, not numeric
        using var result = await db.VectorSetRangeAsync(key);

        Assert.NotNull(result);
        Assert.Equal(5, result.Length);
        // Lexicographical order: "1", "10", "2", "20", "3"
        Assert.Equal(new[] { "1", "10", "2", "20", "3" }, result.Span.ToArray().Select(r => (string?)r).ToArray());
    }

    [Fact]
    public async Task VectorSetRangeEnumerate_BasicIteration()
    {
        await using var conn = Create(require: RedisFeatures.v8_4_0_rc1);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f };

        // Add 50 members
        for (int i = 0; i < 50; i++)
        {
            var request = VectorSetAddRequest.Member($"member{i:D3}", vector.AsMemory());
            await db.VectorSetAddAsync(key, request);
        }

        // Enumerate with batch size of 10
        var allMembers = new System.Collections.Generic.List<RedisValue>();
        foreach (var member in db.VectorSetRangeEnumerate(key, count: 10))
        {
            allMembers.Add(member);
        }

        Assert.Equal(50, allMembers.Count);

        // Verify lexicographical order
        var sorted = allMembers.OrderBy(m => (string?)m, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, allMembers);
    }

    [Fact]
    public async Task VectorSetRangeEnumerate_WithRange()
    {
        await using var conn = Create(require: RedisFeatures.v8_4_0_rc1);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f };

        // Add members "a" through "z"
        for (char c = 'a'; c <= 'z'; c++)
        {
            var request = VectorSetAddRequest.Member(c.ToString(), vector.AsMemory());
            await db.VectorSetAddAsync(key, request);
        }

        // Enumerate from "f" to "p" with batch size 5
        var allMembers = new System.Collections.Generic.List<RedisValue>();
        foreach (var member in db.VectorSetRangeEnumerate(key, start: "f", end: "p", count: 5))
        {
            allMembers.Add(member);
        }

        // Should get "f" through "p" inclusive (11 members)
        Assert.Equal(11, allMembers.Count);
        Assert.Equal("f", (string?)allMembers.First());
        Assert.Equal("p", (string?)allMembers.Last());
    }

    [Fact]
    public async Task VectorSetRangeEnumerate_EarlyBreak()
    {
        await using var conn = Create(require: RedisFeatures.v8_4_0_rc1);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f };

        // Add 100 members
        for (int i = 0; i < 100; i++)
        {
            var request = VectorSetAddRequest.Member($"member{i:D3}", vector.AsMemory());
            await db.VectorSetAddAsync(key, request);
        }

        // Take only first 25 members
        var limitedMembers = db.VectorSetRangeEnumerate(key, count: 10).Take(25).ToList();

        Assert.Equal(25, limitedMembers.Count);
    }

    [Fact]
    public async Task VectorSetRangeEnumerate_EmptyBatches()
    {
        await using var conn = Create(require: RedisFeatures.v8_4_0_rc1);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        // Don't add any members
        var allMembers = new System.Collections.Generic.List<RedisValue>();
        foreach (var member in db.VectorSetRangeEnumerate(key))
        {
            allMembers.Add(member);
        }

        Assert.Empty(allMembers);
    }

    [Fact]
    public async Task VectorSetRangeEnumerateAsync_BasicIteration()
    {
        await using var conn = Create(require: RedisFeatures.v8_4_0_rc1);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f };

        // Add 50 members
        for (int i = 0; i < 50; i++)
        {
            var request = VectorSetAddRequest.Member($"member{i:D3}", vector.AsMemory());
            await db.VectorSetAddAsync(key, request);
        }

        // Enumerate with batch size of 10
        var allMembers = new System.Collections.Generic.List<RedisValue>();
        await foreach (var member in db.VectorSetRangeEnumerateAsync(key, count: 10))
        {
            allMembers.Add(member);
        }

        Assert.Equal(50, allMembers.Count);

        // Verify lexicographical order
        var sorted = allMembers.OrderBy(m => (string?)m, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, allMembers);
    }

    [Fact]
    public async Task VectorSetRangeEnumerateAsync_WithCancellation()
    {
        await using var conn = Create(require: RedisFeatures.v8_4_0_rc1);
        var db = conn.GetDatabase();
        var key = Me();

        await db.KeyDeleteAsync(key, CommandFlags.FireAndForget);

        var vector = new[] { 1.0f, 2.0f, 3.0f };

        // Add 100 members
        for (int i = 0; i < 100; i++)
        {
            var request = VectorSetAddRequest.Member($"member{i:D3}", vector.AsMemory());
            await db.VectorSetAddAsync(key, request);
        }

        using var cts = new CancellationTokenSource();
        var allMembers = new System.Collections.Generic.List<RedisValue>();

        // Start enumeration and cancel after collecting some members
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var member in db.VectorSetRangeEnumerateAsync(key, count: 10).WithCancellation(cts.Token))
            {
                allMembers.Add(member);

                // Cancel after we've collected 25 members
                if (allMembers.Count == 25)
                {
                    cts.Cancel();
                }
            }
        });

        // Should have stopped at or shortly after 25 members
        Log($"Expected ~25 members, got {allMembers.Count}");
        Assert.True(allMembers.Count >= 25 && allMembers.Count <= 35, $"Expected ~25 members, got {allMembers.Count}");
    }
}
