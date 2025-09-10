using System;
using System.Text;
using NSubstitute;
using Xunit;

namespace StackExchange.Redis.Tests
{
    [Collection(nameof(SubstituteDependentCollection))]
    public sealed class KeyPrefixedVectorSetTests
    {
        private readonly IDatabase mock;
        private readonly IDatabase prefixed;

        public KeyPrefixedVectorSetTests()
        {
            mock = Substitute.For<IDatabase>();
            prefixed = new KeyspaceIsolation.KeyPrefixedDatabase(mock, Encoding.UTF8.GetBytes("prefix:"));
        }

        [Fact]
        public void VectorSetAdd_Fp32()
        {
            if (BitConverter.IsLittleEndian)
            {
                Assert.True(VectorSetAddMessage.UseFp32);
#if DEBUG // can be suppressed
                VectorSetAddMessage.SuppressFp32();
                Assert.False(VectorSetAddMessage.UseFp32);
                VectorSetAddMessage.RestoreFp32();
                Assert.True(VectorSetAddMessage.UseFp32);
#endif
            }
            else
            {
                Assert.False(VectorSetAddMessage.UseFp32);
            }
        }

        [Fact]
        public void VectorSetAdd_BasicCall()
        {
            var vector = new[] { 1.0f, 2.0f, 3.0f }.AsMemory();

            var request = VectorSetAddRequest.Member("element1", vector);
            prefixed.VectorSetAdd("vectorset", request);

            mock.Received().VectorSetAdd(
                "prefix:vectorset",
                request);
        }

        [Fact]
        public void VectorSetAdd_WithAllParameters()
        {
            var vector = new[] { 1.0f, 2.0f, 3.0f }.AsMemory();
            var attributes = """{"category":"test"}""";

            var request = VectorSetAddRequest.Member(
                "element1",
                vector,
                attributes);
            request.ReducedDimensions = 64;
            request.Quantization = VectorSetQuantization.Binary;
            request.BuildExplorationFactor = 300;
            request.MaxConnections = 32;
            request.UseCheckAndSet = true;
            prefixed.VectorSetAdd(
                "vectorset",
                request,
                flags: CommandFlags.FireAndForget);

            mock.Received().VectorSetAdd(
                "prefix:vectorset",
                request,
                CommandFlags.FireAndForget);
        }

        [Fact]
        public void VectorSetLength()
        {
            prefixed.VectorSetLength("vectorset");
            mock.Received().VectorSetLength("prefix:vectorset");
        }

        [Fact]
        public void VectorSetDimension()
        {
            prefixed.VectorSetDimension("vectorset");
            mock.Received().VectorSetDimension("prefix:vectorset");
        }

        [Fact]
        public void VectorSetGetApproximateVector()
        {
            prefixed.VectorSetGetApproximateVector("vectorset", "member1");
            mock.Received().VectorSetGetApproximateVector("prefix:vectorset", "member1");
        }

        [Fact]
        public void VectorSetGetAttributesJson()
        {
            prefixed.VectorSetGetAttributesJson("vectorset", "member1");
            mock.Received().VectorSetGetAttributesJson("prefix:vectorset", "member1");
        }

        [Fact]
        public void VectorSetInfo()
        {
            prefixed.VectorSetInfo("vectorset");
            mock.Received().VectorSetInfo("prefix:vectorset");
        }

        [Fact]
        public void VectorSetContains()
        {
            prefixed.VectorSetContains("vectorset", "member1");
            mock.Received().VectorSetContains("prefix:vectorset", "member1");
        }

        [Fact]
        public void VectorSetGetLinks()
        {
            prefixed.VectorSetGetLinks("vectorset", "member1");
            mock.Received().VectorSetGetLinks("prefix:vectorset", "member1");
        }

        [Fact]
        public void VectorSetGetLinksWithScores()
        {
            prefixed.VectorSetGetLinksWithScores("vectorset", "member1");
            mock.Received().VectorSetGetLinksWithScores("prefix:vectorset", "member1");
        }

        [Fact]
        public void VectorSetRandomMember()
        {
            prefixed.VectorSetRandomMember("vectorset");
            mock.Received().VectorSetRandomMember("prefix:vectorset");
        }

        [Fact]
        public void VectorSetRandomMembers()
        {
            prefixed.VectorSetRandomMembers("vectorset", 5);
            mock.Received().VectorSetRandomMembers("prefix:vectorset", 5);
        }

        [Fact]
        public void VectorSetRemove()
        {
            prefixed.VectorSetRemove("vectorset", "member1");
            mock.Received().VectorSetRemove("prefix:vectorset", "member1");
        }

        [Fact]
        public void VectorSetSetAttributesJson()
        {
            var attributes = """{"category":"test"}""";

            prefixed.VectorSetSetAttributesJson("vectorset", "member1", attributes);
            mock.Received().VectorSetSetAttributesJson("prefix:vectorset", "member1", attributes);
        }

        [Fact]
        public void VectorSetSimilaritySearchByVector()
        {
            var vector = new[] { 1.0f, 2.0f, 3.0f }.AsMemory();

            var query = VectorSetSimilaritySearchRequest.ByVector(vector);
            prefixed.VectorSetSimilaritySearch(
                "vectorset",
                query);
            mock.Received().VectorSetSimilaritySearch(
                "prefix:vectorset",
                query);
        }

        [Fact]
        public void VectorSetSimilaritySearchByMember()
        {
            var query = VectorSetSimilaritySearchRequest.ByMember("member1");
            query.Count = 5;
            query.WithScores = true;
            query.WithAttributes = true;
            query.Epsilon = 0.1;
            query.SearchExplorationFactor = 400;
            query.FilterExpression = "category='test'";
            query.MaxFilteringEffort = 1000;
            query.UseExactSearch = true;
            query.DisableThreading = true;
            prefixed.VectorSetSimilaritySearch(
                "vectorset",
                query,
                CommandFlags.FireAndForget);
            mock.Received().VectorSetSimilaritySearch(
                "prefix:vectorset",
                query,
                CommandFlags.FireAndForget);
        }

        [Fact]
        public void VectorSetSimilaritySearchByVector_DefaultParameters()
        {
            var vector = new[] { 1.0f, 2.0f }.AsMemory();

            // Test that default parameters work correctly
            var query = VectorSetSimilaritySearchRequest.ByVector(vector);
            prefixed.VectorSetSimilaritySearch("vectorset", query);
            mock.Received().VectorSetSimilaritySearch(
                "prefix:vectorset",
                query);
        }
    }
}
