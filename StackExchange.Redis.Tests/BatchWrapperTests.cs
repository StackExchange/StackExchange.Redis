using System;
using Moq;
using NUnit.Framework;
using StackExchange.Redis.StackExchange.Redis.KeyspaceIsolation;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public sealed class BatchWrapperTests
    {
        private Mock<IBatch> mock;
        private BatchWrapper wrapper;

        [TestFixtureSetUp]
        public void Initialize()
        {
            mock = new Mock<IBatch>();
            wrapper = new BatchWrapper(mock.Object, "prefix:");
        }

        [Test]
        public void Execute()
        {
            wrapper.Execute();
            mock.Verify(_ => _.Execute(), Times.Once());
        }
    }
}
