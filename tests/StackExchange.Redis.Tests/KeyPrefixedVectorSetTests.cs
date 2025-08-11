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

            prefixed.VectorSetAdd("vectorset", "element1", vector);

            mock.Received().VectorSetAdd(
                "prefix:vectorset",
                "element1",
                vector);
        }

        [Fact]
        public void VectorSetAdd_WithAllParameters()
        {
            var vector = new[] { 1.0f, 2.0f, 3.0f }.AsMemory();
            var attributes = """{"category":"test"}""";

            prefixed.VectorSetAdd(
                "vectorset",
                "element1",
                vector,
                reducedDimensions: 64,
                quantizationType: VectorQuantizationType.Binary,
                buildExplorationFactor: 300,
                maxConnections: 32,
                useCheckAndSet: true,
                attributesJson: attributes,
                flags: CommandFlags.FireAndForget);

            mock.Received().VectorSetAdd(
                "prefix:vectorset",
                "element1",
                vector,
                64,
                VectorQuantizationType.Binary,
                300,
                32,
                true,
                attributes,
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

            prefixed.VectorSetSimilaritySearchByVector(
                "vectorset",
                vector);
            mock.Received().VectorSetSimilaritySearchByVector(
                "prefix:vectorset",
                vector);
        }

        [Fact]
        public void VectorSetSimilaritySearchByMember()
        {
            prefixed.VectorSetSimilaritySearchByMember(
                "vectorset",
                "member1",
                5,
                true,
                true,
                0.1,
                400,
                "category='test'",
                1000,
                true,
                true,
                CommandFlags.FireAndForget);
            mock.Received().VectorSetSimilaritySearchByMember(
                "prefix:vectorset",
                "member1",
                5,
                true,
                true,
                0.1,
                400,
                "category='test'",
                1000,
                true,
                true,
                CommandFlags.FireAndForget);
        }

        [Fact]
        public void VectorSetSimilaritySearchByVector_DefaultParameters()
        {
            var vector = new[] { 1.0f, 2.0f }.AsMemory();

            // Test that default parameters work correctly
            prefixed.VectorSetSimilaritySearchByVector("vectorset", vector);
            mock.Received().VectorSetSimilaritySearchByVector(
                "prefix:vectorset",
                vector);
        }
    }
}
