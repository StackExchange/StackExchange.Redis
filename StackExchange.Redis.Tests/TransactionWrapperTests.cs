#if FEATURE_MOQ
using System.Text;
using Moq;
using NUnit.Framework;
using StackExchange.Redis.KeyspaceIsolation;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public sealed class TransactionWrapperTests
    {
        private Mock<ITransaction> mock;
        private TransactionWrapper wrapper;

        [OneTimeSetUp]
        public void Initialize()
        {
            mock = new Mock<ITransaction>();
            wrapper = new TransactionWrapper(mock.Object, Encoding.UTF8.GetBytes("prefix:"));
        }

        [Test]
        public void AddCondition_HashEqual()
        {
            wrapper.AddCondition(Condition.HashEqual("key", "field", "value"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key > field == value" == value.ToString())));
        }

        [Test]
        public void AddCondition_HashNotEqual()
        {
            wrapper.AddCondition(Condition.HashNotEqual("key", "field", "value"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key > field != value" == value.ToString())));
        }

        [Test]
        public void AddCondition_HashExists()
        {
            wrapper.AddCondition(Condition.HashExists("key", "field"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key > field exists" == value.ToString())));
        }

        [Test]
        public void AddCondition_HashNotExists()
        {
            wrapper.AddCondition(Condition.HashNotExists("key", "field"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key > field does not exists" == value.ToString())));
        }

        [Test]
        public void AddCondition_KeyExists()
        {
            wrapper.AddCondition(Condition.KeyExists("key"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key exists" == value.ToString())));
        }

        [Test]
        public void AddCondition_KeyNotExists()
        {
            wrapper.AddCondition(Condition.KeyNotExists("key"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key does not exists" == value.ToString())));
        }

        [Test]
        public void AddCondition_StringEqual()
        {
            wrapper.AddCondition(Condition.StringEqual("key", "value"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key == value" == value.ToString())));
        }

        [Test]
        public void AddCondition_StringNotEqual()
        {
            wrapper.AddCondition(Condition.StringNotEqual("key", "value"));
            mock.Verify(_ => _.AddCondition(It.Is<Condition>(value => "prefix:key != value" == value.ToString())));
        }

        [Test]
        public void ExecuteAsync()
        {
            wrapper.ExecuteAsync(CommandFlags.HighPriority);
            mock.Verify(_ => _.ExecuteAsync(CommandFlags.HighPriority), Times.Once());
        }

        [Test]
        public void Execute()
        {
            wrapper.Execute(CommandFlags.HighPriority);
            mock.Verify(_ => _.Execute(CommandFlags.HighPriority), Times.Once());
        }
    }
}
#endif