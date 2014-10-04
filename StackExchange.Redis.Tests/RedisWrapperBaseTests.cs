using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Net;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using StackExchange.Redis.StackExchange.Redis.KeyspaceIsolation;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public sealed class RedisWrapperBaseTests
    {
        private Mock<IRedisAsync> mock;
        private RedisWrapperBase<IRedisAsync> wrapper;

        [SetUp]
        public void Initialize()
        {
            mock = new Mock<IRedisAsync>();
            wrapper = new RedisWrapperBase<IRedisAsync>(mock.Object, "prefix:");
        }

        [Test]
        public void get_Multiplexer()
        {
            ConnectionMultiplexer expected = ConnectionMultiplexer.CreateMultiplexer("dummy");
            mock.SetupGet(_ => _.Multiplexer).Returns(expected);
            ConnectionMultiplexer actual = wrapper.Multiplexer;
            mock.VerifyGet(_ => _.Multiplexer);
            Assert.AreSame(expected, actual);
        }

        [Test]
        public void PingAsync()
        {
            wrapper.PingAsync(CommandFlags.HighPriority);
            mock.Verify(_ => _.PingAsync(CommandFlags.HighPriority));
        }

        [Test]
        public void TryWait()
        {
            Task task = Task.FromResult<bool>(true);
            wrapper.TryWait(task);
            mock.Verify(_ => _.TryWait(task));
        }

        [Test]
        public void Wait_1()
        {
            Task<bool> task = Task.FromResult<bool>(true);
            wrapper.Wait(task);
            mock.Verify(_ => _.Wait(task));
        }

        [Test]
        public void Wait_2()
        {
            Task task = Task.FromResult<bool>(true);
            wrapper.Wait(task);
            mock.Verify(_ => _.Wait(task));
        }

        [Test]
        public void WaitAll()
        {
            Task[] tasks = new Task[0];
            wrapper.WaitAll(tasks);
            mock.Verify(_ => _.WaitAll(tasks));
        }
    }
}
