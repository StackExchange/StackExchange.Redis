using System.Text;
using System.Threading.Tasks;
using NSubstitute;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;

namespace StackExchange.Redis.Tests;

[Collection(nameof(SubstituteDependentCollection))]
public sealed class KeyPrefixedTransactionTests
{
    private readonly ITransaction mock;
    private readonly KeyPrefixedTransaction prefixed;

    public KeyPrefixedTransactionTests()
    {
        mock = Substitute.For<ITransaction>();
        prefixed = new KeyPrefixedTransaction(mock, Encoding.UTF8.GetBytes("prefix:"));
    }

    [Fact]
    public void AddCondition_HashEqual()
    {
        prefixed.AddCondition(Condition.HashEqual("key", "field", "value"));
        mock.Received().AddCondition(Arg.Is<Condition>(value => "prefix:key Hash > field == value" == value.ToString()));
    }

    [Fact]
    public void AddCondition_HashNotEqual()
    {
        prefixed.AddCondition(Condition.HashNotEqual("key", "field", "value"));
        mock.Received().AddCondition(Arg.Is<Condition>(value => "prefix:key Hash > field != value" == value.ToString()));
    }

    [Fact]
    public void AddCondition_HashExists()
    {
        prefixed.AddCondition(Condition.HashExists("key", "field"));
        mock.Received().AddCondition(Arg.Is<Condition>(value => "prefix:key Hash > field exists" == value.ToString()));
    }

    [Fact]
    public void AddCondition_HashNotExists()
    {
        prefixed.AddCondition(Condition.HashNotExists("key", "field"));
        mock.Received().AddCondition(Arg.Is<Condition>(value => "prefix:key Hash > field does not exists" == value.ToString()));
    }

    [Fact]
    public void AddCondition_KeyExists()
    {
        prefixed.AddCondition(Condition.KeyExists("key"));
        mock.Received().AddCondition(Arg.Is<Condition>(value => "prefix:key exists" == value.ToString()));
    }

    [Fact]
    public void AddCondition_KeyNotExists()
    {
        prefixed.AddCondition(Condition.KeyNotExists("key"));
        mock.Received().AddCondition(Arg.Is<Condition>(value => "prefix:key does not exists" == value.ToString()));
    }

    [Fact]
    public void AddCondition_StringEqual()
    {
        prefixed.AddCondition(Condition.StringEqual("key", "value"));
        mock.Received().AddCondition(Arg.Is<Condition>(value => "prefix:key == value" == value.ToString()));
    }

    [Fact]
    public void AddCondition_StringNotEqual()
    {
        prefixed.AddCondition(Condition.StringNotEqual("key", "value"));
        mock.Received().AddCondition(Arg.Is<Condition>(value => "prefix:key != value" == value.ToString()));
    }

    [Fact]
    public void AddCondition_SortedSetEqual()
    {
        prefixed.AddCondition(Condition.SortedSetEqual("key", "member", "score"));
        mock.Received().AddCondition(Arg.Is<Condition>(value => "prefix:key SortedSet > member == score" == value.ToString()));
    }

    [Fact]
    public void AddCondition_SortedSetNotEqual()
    {
        prefixed.AddCondition(Condition.SortedSetNotEqual("key", "member", "score"));
        mock.Received().AddCondition(Arg.Is<Condition>(value => "prefix:key SortedSet > member != score" == value.ToString()));
    }

    [Fact]
    public void AddCondition_SortedSetScoreExists()
    {
        prefixed.AddCondition(Condition.SortedSetScoreExists("key", "score"));
        mock.Received().AddCondition(Arg.Is<Condition>(value => "prefix:key not contains 0 members with score: score" == value.ToString()));
    }

    [Fact]
    public void AddCondition_SortedSetScoreNotExists()
    {
        prefixed.AddCondition(Condition.SortedSetScoreNotExists("key", "score"));
        mock.Received().AddCondition(Arg.Is<Condition>(value => "prefix:key contains 0 members with score: score" == value.ToString()));
    }

    [Fact]
    public void AddCondition_SortedSetScoreCountExists()
    {
        prefixed.AddCondition(Condition.SortedSetScoreExists("key", "score", "count"));
        mock.Received().AddCondition(Arg.Is<Condition>(value => "prefix:key contains count members with score: score" == value.ToString()));
    }

    [Fact]
    public void AddCondition_SortedSetScoreCountNotExists()
    {
        prefixed.AddCondition(Condition.SortedSetScoreNotExists("key", "score", "count"));
        mock.Received().AddCondition(Arg.Is<Condition>(value => "prefix:key not contains count members with score: score" == value.ToString()));
    }

    [Fact]
    public async Task ExecuteAsync()
    {
        await prefixed.ExecuteAsync(CommandFlags.None);
        await mock.Received(1).ExecuteAsync(CommandFlags.None);
    }

    [Fact]
    public void Execute()
    {
        prefixed.Execute(CommandFlags.None);
        mock.Received(1).Execute(CommandFlags.None);
    }
}
