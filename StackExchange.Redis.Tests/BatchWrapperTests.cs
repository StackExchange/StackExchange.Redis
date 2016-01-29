#if FEATURE_MOQ
using Moq;
using NUnit.Framework;
using StackExchange.Redis.KeyspaceIsolation;
using System.Text;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public sealed class BatchWrapperTests
    {
        private Mock<IBatch> mock;
        private BatchWrapper wrapper;

        //[TestFixtureSetUp]
        [OneTimeSetUpAttribute]
        public void Initialize()
        {
            mock = new Mock<IBatch>();
            wrapper = new BatchWrapper(mock.Object, Encoding.UTF8.GetBytes("prefix:"));
        }

        [Test]
        public void Execute()
        {
            wrapper.Execute();
            mock.Verify(_ => _.Execute(), Times.Once());
        }
    }
}
#endif