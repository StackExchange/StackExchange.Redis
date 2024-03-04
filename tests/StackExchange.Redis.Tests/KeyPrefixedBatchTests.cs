using StackExchange.Redis.KeyspaceIsolation;
using System.Text;
using NSubstitute;
using Xunit;

namespace StackExchange.Redis.Tests;

[Collection(nameof(SubstituteDependentCollection))]
public sealed class KeyPrefixedBatchTests
{
    private readonly IBatch mock;
    private readonly KeyPrefixedBatch prefixed;

    public KeyPrefixedBatchTests()
    {
        mock = Substitute.For<IBatch>();
        prefixed = new KeyPrefixedBatch(mock, Encoding.UTF8.GetBytes("prefix:"));
    }

    [Fact]
    public void Execute()
    {
        prefixed.Execute();
        mock.Received(1).Execute();
    }
}
