using NUnit.Framework;
using System.Linq;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public sealed class RedisCommandHandlerTest : TestBase
    {
        public class TestCommandHandler : IRedisCommandHandler
        {
            public delegate void OnExecutingHandler(RedisCommand command, RedisKey[] involvedKeys = null, RedisValue[] involvedValues = null);
            public delegate void OnExecutedHandler(RedisCommand command, ref object result, RedisKey[] involvedKeys = null);

            public OnExecutingHandler onExecuting;
            public OnExecutedHandler onExecuted;

            public void OnExecuting(RedisCommand command, RedisKey[] involvedKeys = null, RedisValue[] involvedValues = null)
            {
                onExecuting(command, involvedKeys, involvedValues);
            }

            public void OnExecuted(RedisCommand command, ref object result, RedisKey[] involvedKeys = null)
            {
                onExecuted(command, ref result, involvedKeys);
            }
        }

        [Test]
        public void CanHandleCommands()
        {
            RedisServiceFactory.Register<IRedisCommandHandler, TestCommandHandler>();
            TestCommandHandler cmdHandler = (TestCommandHandler)RedisServiceFactory.CommandHandlers.First();

            bool onExecutingDone = false;
            bool onExecutedDone = false;
            RedisKey[] testKeys = new RedisKey[] { "test" };
            RedisValue[] testValues = new RedisValue[] { "test value" };
            object testResult = (RedisValue)"hello world";

            cmdHandler.onExecuting = (command, involvedKeys, involvedValues) =>
            {
                Assert.AreEqual(RedisCommand.SET, command);
                Assert.AreEqual(1, testKeys.Intersect(involvedKeys).Count());
                Assert.AreEqual(1, testValues.Intersect(involvedValues).Count());
                onExecutingDone = true;
            };
            cmdHandler.onExecuted = (RedisCommand command, ref object result, RedisKey[] involvedKeys) =>
            {
                Assert.AreEqual(RedisCommand.HMSET, command);
                Assert.AreEqual(1, testKeys.Intersect(involvedKeys).Count());
                Assert.AreEqual(testResult, result);
                onExecutedDone = true;
            };

            RedisServiceFactory.CommandHandlers.ExecuteBeforeHandlers(RedisCommand.SET, new RedisKey[] { "test" }, new RedisValue[] { "test value" });
            RedisServiceFactory.CommandHandlers.ExecuteAfterHandlers(RedisCommand.HMSET, new RedisKey[] { "test" }, ref testResult);

            Assert.IsTrue(onExecutingDone);
            Assert.IsTrue(onExecutedDone);
        }

        [Test]
        public void NoErrorWhenNoHandlerIsConfigured()
        {
            RedisKey[] testKeys = new RedisKey[] { "test" };
            RedisValue[] testValues = new RedisValue[] { "test value" };
            object testResult = (RedisValue)"hello world";

            Assert.DoesNotThrow
            (
                () =>
                {
                    Assert.IsFalse(RedisServiceFactory.CommandHandlers.ExecuteBeforeHandlers(RedisCommand.SET, new RedisKey[] { "test" }, new RedisValue[] { "test value" }));
                    Assert.IsFalse(RedisServiceFactory.CommandHandlers.ExecuteAfterHandlers(RedisCommand.HMSET, new RedisKey[] { "test" }, ref testResult));
                }
            );
        }

        [Test]
        public void CanActivateCommandHandlerToSpecificCommand()
        {
            RedisCommandHandlerConfiguration config = new RedisCommandHandlerConfiguration();
            config.ActivateForCommands(RedisCommand.HSET, RedisCommand.HDEL, RedisCommand.ZRANGE);

            RedisServiceFactory.Register<IRedisCommandHandler, TestCommandHandler>();
            TestCommandHandler cmdHandler = (TestCommandHandler)RedisServiceFactory.CommandHandlers.First();

            bool onExecutingDone = false;
            bool onExecutedDone = false;
            RedisKey[] testKeys = new RedisKey[] { "test" };
            RedisValue[] testValues = new RedisValue[] { "test value" };
            object testResult = (RedisValue)"hello world";

            cmdHandler.onExecuting = (command, involvedKeys, involvedValues) =>
            {
                onExecutingDone = true;
            };
            cmdHandler.onExecuted = (RedisCommand command, ref object result, RedisKey[] involvedKeys) =>
            {
                onExecutedDone = true;
            };

            Assert.IsFalse(RedisServiceFactory.CommandHandlers.ExecuteBeforeHandlers(RedisCommand.SET, new RedisKey[] { "test" }, new RedisValue[] { "test value" }));
            Assert.IsFalse(RedisServiceFactory.CommandHandlers.ExecuteAfterHandlers(RedisCommand.GET, new RedisKey[] { "test" }, ref testResult));
            Assert.IsFalse(onExecutingDone);
            Assert.IsFalse(onExecutedDone);

            onExecutingDone = false;
            onExecutedDone = false;

            cmdHandler.onExecuting = (command, involvedKeys, involvedValues) =>
            {
                onExecutingDone = true;
                Assert.AreEqual(RedisCommand.SET, command);
            };
            cmdHandler.onExecuted = (RedisCommand command, ref object result, RedisKey[] involvedKeys) =>
            {
                onExecutedDone = true;
                Assert.AreEqual(RedisCommand.SET, command);
            };

            config.ActivateForCommands(RedisCommand.SET);

            Assert.IsTrue(RedisServiceFactory.CommandHandlers.ExecuteBeforeHandlers(RedisCommand.SET, new RedisKey[] { "test" }, new RedisValue[] { "test value" }));
            Assert.IsTrue(RedisServiceFactory.CommandHandlers.ExecuteAfterHandlers(RedisCommand.SET, new RedisKey[] { "test" }, ref testResult));
            Assert.IsTrue(onExecutingDone);
            Assert.IsTrue(onExecutedDone);
        }
    }
}
