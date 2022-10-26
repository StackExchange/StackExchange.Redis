using Moq;
using StackExchange.Redis.KeyspaceIsolation;
using System.Text;
using Xunit;

namespace StackExchange.Redis.Tests;

[Collection(nameof(MoqDependentCollection))]
public sealed class KeyPrefixedBatchTests
{
    private readonly Mock<IBatch> mock;
    private readonly KeyPrefixedBatch prefixed;

    public KeyPrefixedBatchTests()
    {
        mock = new Mock<IBatch>();
        prefixed = new KeyPrefixedBatch(mock.Object, Encoding.UTF8.GetBytes("prefix:"));
    }

    [Fact]
    public void Execute()
    {
        prefixed.Execute();
        mock.Verify(_ => _.Execute(), Times.Once());
    }
}
