using Moq;
using StackExchange.Redis.KeyspaceIsolation;
using System.Text;

namespace StackExchange.Redis.Tests
{
    public sealed class BatchWrapperTests
    {
        private readonly Mock<IBatch> mock;
        private readonly BatchWrapper wrapper;

        public BatchWrapperTests()
        {
            mock = new Mock<IBatch>();
            wrapper = new BatchWrapper(mock.Object, Encoding.UTF8.GetBytes("prefix:"));
        }

        [Fact]
        public void Execute()
        {
            wrapper.Execute();
            mock.Verify(_ => _.Execute(), Times.Once());
        }
    }
}
