using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using StackExchange.Redis.StackExchange.Redis.KeyspaceIsolation;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public sealed class SubscriberWrapperTests
    {
        private Mock<ISubscriber> mock;
        private SubscriberWrapper wrapper;

        [SetUp]
        public void Initialize()
        {
            mock = new Mock<ISubscriber>();
            wrapper = new SubscriberWrapper(mock.Object, "prefix:");
        }

        [Test]
        public void IdentifyEndpoint()
        {
            wrapper.IdentifyEndpoint("channel", CommandFlags.HighPriority);
            mock.Verify(_ => _.IdentifyEndpoint("prefix:channel", CommandFlags.HighPriority));
        }

        [Test]
        public void IdentifyEndpointAsync()
        {
            wrapper.IdentifyEndpointAsync("channel", CommandFlags.HighPriority);
            mock.Verify(_ => _.IdentifyEndpointAsync("prefix:channel", CommandFlags.HighPriority));
        }

        [Test]
        public void IsConnected()
        {
            wrapper.IsConnected("channel");
            mock.Verify(_ => _.IsConnected("prefix:channel"));
        }

        [Test]
        public void Publish()
        {
            wrapper.Publish("channel", "message", CommandFlags.HighPriority);
            mock.Verify(_ => _.Publish("prefix:channel", "message", CommandFlags.HighPriority));
        }

        [Test]
        public void PublishAsync()
        {
            wrapper.PublishAsync("channel", "message", CommandFlags.HighPriority);
            mock.Verify(_ => _.PublishAsync("prefix:channel", "message", CommandFlags.HighPriority));
        }

        [Test]
        public void Subscribe()
        {
            Action<RedisChannel, RedisValue> handler = (channel, value) => {};
            wrapper.Subscribe("channel", handler, CommandFlags.HighPriority);
            mock.Verify(_ => _.Subscribe("prefix:channel", It.IsNotNull<Action<RedisChannel, RedisValue>>(), CommandFlags.HighPriority));
        }

        [Test]
        public void SubscribeAsync()
        {
            Action<RedisChannel, RedisValue> handler = (channel, value) => { };
            wrapper.SubscribeAsync("channel", handler, CommandFlags.HighPriority).Wait();
            mock.Verify(_ => _.SubscribeAsync("prefix:channel", It.IsNotNull<Action<RedisChannel, RedisValue>>(), CommandFlags.HighPriority));
        }

        [Test]
        public void SubscribedEndpoint()
        {
            wrapper.SubscribedEndpoint("channel");
            mock.Verify(_ => _.SubscribedEndpoint("prefix:channel"));
        }

        [Test]
        public void Unsubscribe()
        {
            Action<RedisChannel, RedisValue> handler = (channel, value) => { };
            wrapper.Unsubscribe("channel", handler, CommandFlags.HighPriority);
            mock.Verify(_ => _.Unsubscribe("prefix:channel", It.IsNotNull<Action<RedisChannel, RedisValue>>(), CommandFlags.HighPriority));
        }

        [Test]
        public void UnsubscribeAsync()
        {
            Action<RedisChannel, RedisValue> handler = (channel, value) => { };
            wrapper.UnsubscribeAsync("channel", handler, CommandFlags.HighPriority).Wait();
            mock.Verify(_ => _.UnsubscribeAsync("prefix:channel", It.IsNotNull<Action<RedisChannel, RedisValue>>(), CommandFlags.HighPriority));
        }

        [Test]
        public void Unsubscribe_null_handler()
        {
            wrapper.Unsubscribe("channel", null, CommandFlags.HighPriority);
            mock.Verify(_ => _.Unsubscribe("prefix:channel", null, CommandFlags.HighPriority));
        }

        [Test]
        public void UnsubscribeAsync_null_handler()
        {
            wrapper.UnsubscribeAsync("channel", null, CommandFlags.HighPriority).Wait();
            mock.Verify(_ => _.UnsubscribeAsync("prefix:channel", null, CommandFlags.HighPriority));
        }

        [Test]
        public void UnsubscribeAll()
        {
            mock.Setup(_ => _.UnsubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>())).Returns(Task.FromResult(true));

            Action<RedisChannel, RedisValue> handler = (channel, value) => { };
            Action<RedisChannel, RedisValue> handler2 = (channel, value) => { };

            wrapper.RegisterInnerSubscription("a", handler);
            wrapper.RegisterInnerSubscription("a", handler2);
            wrapper.RegisterInnerSubscription("b", handler);
            wrapper.RegisterInnerSubscription("c", handler);
            wrapper.RegisterInnerSubscription("d", handler);

            wrapper.UnregisterInnerSubscription("a", handler);
            wrapper.UnregisterInnerSubscription("b", handler);
            wrapper.UnregisterInnerSubscription("c", handler);

            // Subcribed channels: "a", "d"
            Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("a"));
            Assert.IsFalse(wrapper.IsSubscribedToInnerChannel("b"));
            Assert.IsFalse(wrapper.IsSubscribedToInnerChannel("c"));
            Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("d"));

            var handlerForA = wrapper.GetInnerHandlerForChannel("a");
            var handlerForD = wrapper.GetInnerHandlerForChannel("d");

            Assert.IsNotNull(handlerForA);
            Assert.IsNotNull(handlerForD);

            wrapper.UnsubscribeAll(CommandFlags.HighPriority);

            mock.Verify(_ => _.UnsubscribeAsync("a", handlerForA, CommandFlags.HighPriority), Times.Once());
            mock.Verify(_ => _.UnsubscribeAsync("b", It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()), Times.Never());
            mock.Verify(_ => _.UnsubscribeAsync("d", handlerForD, CommandFlags.HighPriority), Times.Once());
            mock.Verify(_ => _.UnsubscribeAsync("c", It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()), Times.Never());
        }

        [Test]
        public void UnsubscribeAllAsync()
        {
            mock.Setup(_ => _.UnsubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>())).Returns(Task.FromResult(true));

            Action<RedisChannel, RedisValue> handler = (channel, value) => { };
            Action<RedisChannel, RedisValue> handler2 = (channel, value) => { };

            wrapper.RegisterInnerSubscription("a", handler);
            wrapper.RegisterInnerSubscription("a", handler2);
            wrapper.RegisterInnerSubscription("b", handler);
            wrapper.RegisterInnerSubscription("c", handler);
            wrapper.RegisterInnerSubscription("d", handler);

            wrapper.UnregisterInnerSubscription("a", handler);
            wrapper.UnregisterInnerSubscription("b", handler);
            wrapper.UnregisterInnerSubscription("c", handler);

            // Subcribed channels: "a", "d"
            Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("a"));
            Assert.IsFalse(wrapper.IsSubscribedToInnerChannel("b"));
            Assert.IsFalse(wrapper.IsSubscribedToInnerChannel("c"));
            Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("d"));

            var handlerForA = wrapper.GetInnerHandlerForChannel("a");
            var handlerForD = wrapper.GetInnerHandlerForChannel("d");

            Assert.IsNotNull(handlerForA);
            Assert.IsNotNull(handlerForD);

            Task task = wrapper.UnsubscribeAllAsync(CommandFlags.HighPriority);
            task.Wait();

            mock.Verify(_ => _.UnsubscribeAsync("a", handlerForA, CommandFlags.HighPriority), Times.Once());
            mock.Verify(_ => _.UnsubscribeAsync("b", It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()), Times.Never());
            mock.Verify(_ => _.UnsubscribeAsync("d", handlerForD, CommandFlags.HighPriority), Times.Once());
            mock.Verify(_ => _.UnsubscribeAsync("c", It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()), Times.Never());
        }

        [Test]
        public void Ping()
        {
            wrapper.Ping(CommandFlags.HighPriority);
            mock.Verify(_ => _.Ping(CommandFlags.HighPriority));
        }

        [Test]
        public void Inner_handler_for_null()
        {
            Assert.IsNull(wrapper.GetInnerHandler(null));
        }

        [Test]
        public void Same_inner_handler_is_returned_when_remembered()
        {
            Action<RedisChannel, RedisValue> outerHandler = (channel, value) => { };
            Action<RedisChannel, RedisValue> innerHandler = wrapper.GetInnerHandler(outerHandler);
            Assert.AreSame(innerHandler, wrapper.GetInnerHandler(outerHandler));
        }

        [Test]
        public void Other_inner_handler_is_returned_when_not_remembered()
        {
            Action<RedisChannel, RedisValue> outerHandler = (channel, value) => { };
            Action<RedisChannel, RedisValue> innerHandler = wrapper.GetInnerHandler(outerHandler, remember: false);
            Assert.AreNotSame(innerHandler, wrapper.GetInnerHandler(outerHandler));
        }

        [Test]
        public void Channel_is_unprefixed()
        {
            HashSet<RedisChannel> actual = new HashSet<RedisChannel>();

            Action<RedisChannel, RedisValue> outerHandler = (channel, value) =>
            {
                actual.Add(channel);
            };

            Action<RedisChannel, RedisValue> innerHandler = wrapper.GetInnerHandler(outerHandler);

            innerHandler("prefix:hello", default(RedisValue));
            innerHandler("other", default(RedisValue)); // should be omitted
            innerHandler("prefix:world", default(RedisValue));

            HashSet<RedisChannel> expected = new HashSet<RedisChannel>() { "hello", "world" };

            CollectionAssert.AreEquivalent(expected, actual);
        }

        [Test]
        public void Subscription_is_remembered_when_inner_subscribe_succeed()
        {
            Action<RedisChannel, RedisValue> handler = (channel, value) => { };
            Assert.IsFalse(wrapper.IsSubscribedToInnerChannel("prefix:channel"));
            wrapper.Subscribe("channel", handler);
            Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("prefix:channel"));
        }

        [Test]
        public void Subscription_is_NOT_remembered_when_inner_subscribe_fail()
        {
            mock.Setup(_ => _.Subscribe(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
                .Throws(new DummyException());

            Action<RedisChannel, RedisValue> handler = (channel, value) => { };
            Assert.IsFalse(wrapper.IsSubscribedToInnerChannel("prefix:channel"));

            try 
            { 
                wrapper.Subscribe("channel", handler);
                Assert.Fail("Should have thrown dummy exception.");
            }
            catch(DummyException) { }

            Assert.IsFalse(wrapper.IsSubscribedToInnerChannel("prefix:channel"));
        }

        [Test]
        public void Subscription_is_purged_when_inner_unsubscribe_succeed()
        {
            Action<RedisChannel, RedisValue> handler = (channel, value) => { };
            wrapper.RegisterInnerSubscription("prefix:channel", wrapper.GetInnerHandler(handler));
            Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("prefix:channel"));

            wrapper.Unsubscribe("channel", handler);
            
            Assert.IsFalse(wrapper.IsSubscribedToInnerChannel("prefix:channel"));
        }

        [Test]
        public void Subscription_is_NOT_purged_when_inner_unsubscribe_fail()
        {
            mock.Setup(_ => _.Unsubscribe(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
                .Throws(new DummyException());

            Action<RedisChannel, RedisValue> handler = (channel, value) => { };
            wrapper.RegisterInnerSubscription("prefix:channel", wrapper.GetInnerHandler(handler));
            Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("prefix:channel"));

            try
            {
                wrapper.Unsubscribe("channel", handler);
                Assert.Fail("Should have thrown dummy exception.");
            }
            catch (DummyException) { }

            Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("prefix:channel"));
        }

        [Test]
        public void Subscription_is_remembered_when_inner_async_subscribe_succeed()
        {
            mock.Setup(_ => _.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
                .Returns(Task.FromResult(true));

            Action<RedisChannel, RedisValue> handler = (channel, value) => { };
            Assert.IsFalse(wrapper.IsSubscribedToInnerChannel("prefix:channel"));

            wrapper.SubscribeAsync("channel", handler).Wait();

            Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("prefix:channel"));
        }

        [Test]
        public void Subscription_is_NOT_remembered_when_inner_async_subscribe_fail()
        {
            mock.Setup(_ => _.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
                .Returns(Task.Factory.StartNew(() => { throw new DummyException(); }));

            Action<RedisChannel, RedisValue> handler = (channel, value) => { };
            Assert.IsFalse(wrapper.IsSubscribedToInnerChannel("prefix:channel"));

            try
            {
                var task = wrapper.SubscribeAsync("channel", handler);
                task.Wait();
                Assert.Fail("Should have thrown dummy exception.");
            }
            catch (AggregateException error) 
            {
                Assert.IsInstanceOf<DummyException>(error.InnerException);
            }

            Assert.IsFalse(wrapper.IsSubscribedToInnerChannel("prefix:channel"));
        }

        [Test]
        public void Subscription_is_purged_when_inner_async_unsubscribe_succeed()
        {
            mock.Setup(_ => _.UnsubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
                .Returns(Task.FromResult(true));

            Action<RedisChannel, RedisValue> handler = (channel, value) => { };
            wrapper.RegisterInnerSubscription("prefix:channel", wrapper.GetInnerHandler(handler));
            Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("prefix:channel"));

            wrapper.UnsubscribeAsync("channel", handler).Wait();

            Assert.IsFalse(wrapper.IsSubscribedToInnerChannel("prefix:channel"));
        }

        [Test]
        public void Subscription_is_NOT_purged_when_inner_async_unsubscribe_fail()
        {
            mock.Setup(_ => _.UnsubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
                .Returns(Task.Factory.StartNew(() => { throw new DummyException(); }));

            Action<RedisChannel, RedisValue> handler = (channel, value) => { };
            wrapper.RegisterInnerSubscription("prefix:channel", wrapper.GetInnerHandler(handler));
            Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("prefix:channel"));

            try
            {
                wrapper.UnsubscribeAsync("channel", handler).Wait();
                Assert.Fail("Should have thrown dummy exception.");
            }
            catch (AggregateException error)
            {
                Assert.IsInstanceOf<DummyException>(error.InnerException);
            }

            Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("prefix:channel"));
        }

        [Test]
        public void Duplicate_handler_registration()
        {
            int invocationsOfA = 0, invocationsOfB = 0;
            Action<RedisChannel, RedisValue> handlerA = (channel, message) => { ++invocationsOfA; };
            Action<RedisChannel, RedisValue> handlerB = (channel, message) => { ++invocationsOfB; };

            wrapper.RegisterInnerSubscription("channel", handlerA);
            wrapper.RegisterInnerSubscription("channel", handlerB);
            wrapper.RegisterInnerSubscription("channel", handlerA);
            wrapper.RegisterInnerSubscription("channel", handlerA);

            Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("channel"));
            wrapper.GetInnerHandlerForChannel("channel")("channel", default(RedisValue));

            Assert.AreEqual(3, invocationsOfA);
            Assert.AreEqual(1, invocationsOfB);

            wrapper.UnregisterInnerSubscription("channel", handlerA);
            wrapper.UnregisterInnerSubscription("channel", handlerB);
            wrapper.UnregisterInnerSubscription("channel", handlerA);

            Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("channel"));
            wrapper.GetInnerHandlerForChannel("channel")("channel", default(RedisValue));

            Assert.AreEqual(4, invocationsOfA);
            Assert.AreEqual(1, invocationsOfB);

            wrapper.UnregisterInnerSubscription("channel", handlerA);

            Assert.IsFalse(wrapper.IsSubscribedToInnerChannel("channel"));
            Assert.IsNull(wrapper.GetInnerHandlerForChannel("channel"));
        }

        [Test]
        public void Concurrent_subscribe_unsubscribe_1()
        {
            using (ManualResetEvent evt = new ManualResetEvent(false))
            {
                mock.Setup(_ => _.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
                    .Returns(() => Task.Factory.StartNew(() => evt.WaitOne()));

                Action<RedisChannel, RedisValue> handlerA = (channel, message) => { };
                Action<RedisChannel, RedisValue> handlerB = (channel, message) => { };

                // Begin subscribing to channel
                var task = wrapper.SubscribeAsync("channel", handlerA);

                // Subscription should not be in effect yet
                Assert.IsFalse(wrapper.IsSubscribedToInnerChannel("prefix:channel"));

                // Concurrently subscribe to channel with another handler
                wrapper.Subscribe("channel", handlerB);

                // Run task in background
                evt.Set();
                task.Wait();
                evt.Reset();

                // Both subscriptions should be in effect
                Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("prefix:channel"));
                Assert.AreNotSame(wrapper.GetInnerHandler(handlerB), wrapper.GetInnerHandlerForChannel("prefix:channel"));

                // Begin unsubscribing both handlers
                task = wrapper.UnsubscribeAsync("channel");

                // Concurrently resubscribe to channel with handler B
                wrapper.Subscribe("channel", handlerB);

                // Run task in background
                evt.Set();
                task.Wait();
                evt.Reset();

                // Subscription should still be active for handler B
                Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("prefix:channel"));
                Assert.AreSame(wrapper.GetInnerHandler(handlerB), wrapper.GetInnerHandlerForChannel("prefix:channel"));

                // Begin subscribing with handler A
                task = wrapper.SubscribeAsync("channel", handlerA);

                // Concurrently unsubscribe both handlers
                wrapper.Unsubscribe("channel");

                // At this intermediate point there should be no subscription
                Assert.IsFalse(wrapper.IsSubscribedToInnerChannel("prefix:channel"));

                // Run task in background
                evt.Set();
                task.Wait();
                evt.Reset();

                // Subscription should be active for handler A
                Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("prefix:channel"));
                Assert.AreSame(wrapper.GetInnerHandler(handlerA), wrapper.GetInnerHandlerForChannel("prefix:channel"));
            }
        }

        [Test]
        public void Concurrent_subscribe_unsubscribe_2()
        {
            using (ManualResetEvent evt = new ManualResetEvent(false))
            {
                mock.Setup(_ => _.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
                    .Returns(() => Task.Factory.StartNew(() => evt.WaitOne()));

                Action<RedisChannel, RedisValue> handlerA = (channel, message) => { };
                Action<RedisChannel, RedisValue> handlerB = (channel, message) => { };

                // Begin subscribing to channel
                var task = wrapper.SubscribeAsync("channel", handlerA);

                // Subscription should not be in effect yet
                Assert.IsFalse(wrapper.IsSubscribedToInnerChannel("prefix:channel"));

                // Concurrently subscribe to channel with another handler
                wrapper.Subscribe("channel", handlerB);

                // Run task in background
                evt.Set();
                task.Wait();
                evt.Reset();

                // Both subscriptions should be in effect
                Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("prefix:channel"));
                Assert.AreNotSame(wrapper.GetInnerHandler(handlerB), wrapper.GetInnerHandlerForChannel("prefix:channel"));

                // Begin unsubscribing both handlers
                task = wrapper.UnsubscribeAllAsync();

                // Concurrently resubscribe to channel with handler B
                wrapper.Subscribe("channel", handlerB);

                // Run task in background
                evt.Set();
                task.Wait();
                evt.Reset();

                // Subscription should still be active for handler B
                Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("prefix:channel"));
                Assert.AreSame(wrapper.GetInnerHandler(handlerB), wrapper.GetInnerHandlerForChannel("prefix:channel"));

                // Begin subscribing with handler A
                task = wrapper.SubscribeAsync("channel", handlerA);

                // Concurrently unsubscribe both handlers
                wrapper.UnsubscribeAll();

                // At this intermediate point there should be no subscription
                Assert.IsFalse(wrapper.IsSubscribedToInnerChannel("prefix:channel"));

                // Run task in background
                evt.Set();
                task.Wait();
                evt.Reset();

                // Subscription should be active for handler A
                Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("prefix:channel"));
                Assert.AreSame(wrapper.GetInnerHandler(handlerA), wrapper.GetInnerHandlerForChannel("prefix:channel"));
            }
        }

        [Test]
        public void Unsubscribe_all_with_sporadic_inner_exceptions()
        {
            Action<RedisChannel, RedisValue> handler = (channel, message) => { };

            mock.Setup(_ => _.UnsubscribeAsync("prefix:a", handler, CommandFlags.None))
                .Returns(() => Task.Factory.StartNew(() => { /*ok*/ }));

            mock.Setup(_ => _.UnsubscribeAsync("prefix:b", handler, CommandFlags.None))
                .Returns(() => Task.Factory.StartNew(() => { throw new DummyException(); }));

            mock.Setup(_ => _.UnsubscribeAsync("prefix:c", handler, CommandFlags.None))
                .Returns(() => Task.Factory.StartNew(() => { /*ok*/ }));

            wrapper.RegisterInnerSubscription("prefix:a", handler);
            wrapper.RegisterInnerSubscription("prefix:b", handler);
            wrapper.RegisterInnerSubscription("prefix:c", handler);

            Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("prefix:a"));
            Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("prefix:b"));
            Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("prefix:c"));

            wrapper.UnsubscribeAll();

            mock.Verify(_ => _.UnsubscribeAsync("prefix:a", handler, CommandFlags.None), Times.Once());
            mock.Verify(_ => _.UnsubscribeAsync("prefix:b", handler, CommandFlags.None), Times.Once());
            mock.Verify(_ => _.UnsubscribeAsync("prefix:c", handler, CommandFlags.None), Times.Once());

            Assert.IsFalse(wrapper.IsSubscribedToInnerChannel("prefix:a"));
            Assert.IsTrue(wrapper.IsSubscribedToInnerChannel("prefix:b"));
            Assert.IsFalse(wrapper.IsSubscribedToInnerChannel("prefix:c"));
        }

        private sealed class DummyException : Exception { }
    }
}
