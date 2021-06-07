using System.Text;
using Moq;
using StackExchange.Redis.KeyspaceIsolation;
using Xunit;

namespace StackExchange.Redis.Tests
{
#pragma warning disable RCS1047 // Non-asynchronous method name should not end with 'Async'. 
    [Collection(nameof(MoqDependentCollection))]
    public sealed class TransactionWrapperTests
    {
        private readonly Mock<ITransaction> mock;
        private readonly TransactionWrapper wrapper;

        public TransactionWrapperTests()
        {
            mock = new Mock<ITransaction>();
            wrapper = new TransactionWrapper(mock.Object, Encoding.UTF8.GetBytes("prefix:"));
        }

        [Fact]
        public void AddCondition_HashEqual()
        {
            wrapper.AddCondition(Condition.HashEqual("key", "field", "value"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key Hash > field == value" == value.ToString())));
        }

        [Fact]
        public void AddCondition_HashNotEqual()
        {
            wrapper.AddCondition(Condition.HashNotEqual("key", "field", "value"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key Hash > field != value" == value.ToString())));
        }

        [Fact]
        public void AddCondition_HashExists()
        {
            wrapper.AddCondition(Condition.HashExists("key", "field"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key Hash > field exists" == value.ToString())));
        }

        [Fact]
        public void AddCondition_HashNotExists()
        {
            wrapper.AddCondition(Condition.HashNotExists("key", "field"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key Hash > field does not exists" == value.ToString())));
        }

        [Fact]
        public void AddCondition_KeyExists()
        {
            wrapper.AddCondition(Condition.KeyExists("key"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key exists" == value.ToString())));
        }

        [Fact]
        public void AddCondition_KeyNotExists()
        {
            wrapper.AddCondition(Condition.KeyNotExists("key"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key does not exists" == value.ToString())));
        }

        [Fact]
        public void AddCondition_StringEqual()
        {
            wrapper.AddCondition(Condition.StringEqual("key", "value"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key == value" == value.ToString())));
        }

        [Fact]
        public void AddCondition_StringNotEqual()
        {
            wrapper.AddCondition(Condition.StringNotEqual("key", "value"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key != value" == value.ToString())));
        }

        [Fact]
        public void AddCondition_SortedSetEqual()
        {
            wrapper.AddCondition(Condition.SortedSetEqual("key", "member", "score"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key SortedSet > member == score" == value.ToString())));
        }

        [Fact]
        public void AddCondition_SortedSetNotEqual()
        {
            wrapper.AddCondition(Condition.SortedSetNotEqual("key", "member", "score"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key SortedSet > member != score" == value.ToString())));
        }

        [Fact]
        public void AddCondition_SortedSetScoreExists()
        {
            wrapper.AddCondition(Condition.SortedSetScoreExists("key", "score"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key not contains 0 members with score: score" == value.ToString())));
        }

        [Fact]
        public void AddCondition_SortedSetScoreNotExists()
        {
            wrapper.AddCondition(Condition.SortedSetScoreNotExists("key", "score"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key contains 0 members with score: score" == value.ToString())));
        }

        [Fact]
        public void AddCondition_SortedSetScoreCountExists()
        {
            wrapper.AddCondition(Condition.SortedSetScoreExists("key", "score", "count"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key contains count members with score: score" == value.ToString())));
        }

        [Fact]
        public void AddCondition_SortedSetScoreCountNotExists()
        {
            wrapper.AddCondition(Condition.SortedSetScoreNotExists("key", "score", "count"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key not contains count members with score: score" == value.ToString())));
        }

        [Fact]
        public void ExecuteAsync()
        {
            wrapper.ExecuteAsync(CommandFlags.None);
            mock.Verify(_ => _.ExecuteAsync(CommandFlags.None), Times.Once());
        }

        [Fact]
        public void Execute()
        {
            wrapper.Execute(CommandFlags.None);
            mock.Verify(_ => _.Execute(CommandFlags.None), Times.Once());
        }
    }
#pragma warning restore RCS1047 // Non-asynchronous method name should not end with 'Async'.
}
