using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class MuxerTest : TestBase
    {
        private readonly WaitAwaitMutex _noTimeoutMux = new WaitAwaitMutex(0), _timeoutMux = new WaitAwaitMutex(1000);

        public MuxerTest(ITestOutputHelper output) : base(output) { }

        [Fact]
        public void CanObtain()
        {
            // can obtain when not contested (outer)
            // can not obtain when contested, even if re-entrant (inner)
            // can obtain after released (loop)

            for (int i = 0; i < 2; i++)
            {
                using (var outer = _noTimeoutMux.TryWait())
                {
                    Assert.True(outer.Success);
                    using (var inner = _noTimeoutMux.TryWait())
                    {
                        Assert.False(inner.Success);
                    }
                }
            }

            for (int i = 0; i < 2; i++)
            {
                using (var outer = _timeoutMux.TryWait())
                {
                    Assert.True(outer.Success);
                }
            }
        }

        [Fact]
        public async Task CanObtainAsync()
        {
            // can obtain when not contested (outer)
            // can not obtain when contested, even if re-entrant (inner)
            // can obtain after released (loop)
            // with no timeout: is always completed
            // with timeout: is completed on the success option

            for (int i = 0; i < 2; i++)
            {
                var awaitable = _noTimeoutMux.TryWaitAsync();
                Assert.True(awaitable.IsCompleted);
                Assert.True(awaitable.CompletedSynchronously);
                using (var outer = await awaitable)
                {
                    Assert.True(outer.Success);

                    awaitable = _noTimeoutMux.TryWaitAsync();
                    Assert.True(awaitable.IsCompleted);
                    Assert.True(awaitable.CompletedSynchronously);
                    using (var inner = await awaitable)
                    {
                        Assert.False(inner.Success);
                    }
                }
            }

            for (int i = 0; i < 2; i++)
            {
                var awaitable = _timeoutMux.TryWaitAsync();
                Assert.True(awaitable.IsCompleted);
                Assert.True(awaitable.CompletedSynchronously);
                using (var outer = await awaitable)
                {
                    Assert.True(outer.Success);
                }
            }
        }

        [Fact]
        public void Timeout()
        {
            object allReady = new object(), allDone = new object();
            const int COMPETITORS = 5;
            int active = 0, complete = 0, success = 0;

            Assert.NotEqual(0, _timeoutMux.TimeoutMilliseconds);
            lock (allDone)
            {
                using (var token = _timeoutMux.TryWait())
                {
                    lock (allReady)
                    {
                        for (int i = 0; i < COMPETITORS; i++)
                        {
                            Task.Run(() =>
                            {
                                lock (allReady)
                                {
                                    if (++active == COMPETITORS) Monitor.PulseAll(allReady);
                                    else Monitor.Wait(allReady);
                                }
                                using (var inner = _timeoutMux.TryWait())
                                {
                                    lock (allDone)
                                    {
                                        if (inner) success++;
                                        if (++complete == COMPETITORS) Monitor.Pulse(allDone);
                                    }
                                    Thread.Sleep(10);
                                }
                            });
                        }
                        Monitor.Wait(allReady);
                    }
                    Thread.Sleep(_timeoutMux.TimeoutMilliseconds * 2);
                }
                Monitor.Wait(allDone);
                Assert.Equal(COMPETITORS, complete);
                Assert.Equal(0, success);
            }
        }


        [Fact]
        public void CompetingCallerAllExecute()
        {
            object allReady = new object(), allDone = new object();
            const int COMPETITORS = 5;
            int active = 0, complete = 0, success = 0;
            lock (allDone)
            {
                using (var token = _timeoutMux.TryWait())
                {
                    lock (allReady)
                    {
                        for (int i = 0; i < COMPETITORS; i++)
                        {
                            Task.Run(() =>
                            {
                                lock (allReady)
                                {
                                    if (++active == COMPETITORS) Monitor.PulseAll(allReady);
                                    else Monitor.Wait(allReady);
                                }
                                using (var inner = _timeoutMux.TryWait())
                                {
                                    lock (allDone)
                                    {
                                        if (inner) success++;
                                        if (++complete == COMPETITORS) Monitor.Pulse(allDone);
                                    }
                                    Thread.Sleep(10);
                                }
                            });
                        }
                        Monitor.Wait(allReady);
                    }
                    Thread.Sleep(100);
                }
                Monitor.Wait(allDone);
                Assert.Equal(COMPETITORS, complete);
                Assert.Equal(COMPETITORS, success);
            }
        }

        [Fact]
        public async Task CompetingCallerAllExecuteAsync()
        {
            object allReady = new object(), allDone = new object();
            const int COMPETITORS = 5;
            int active = 0, success = 0, asyncOps = 0;

            var tasks = new Task[COMPETITORS];
            using (var token = await _timeoutMux.TryWaitAsync())
            {
                lock (allReady)
                {
                    for (int i = 0; i < tasks.Length; i++)
                    {
                        tasks[i] = Task.Run(async () =>
                        {
                            lock (allReady)
                            {
                                if (++active == COMPETITORS) Monitor.PulseAll(allReady);
                                else Monitor.Wait(allReady);
                            }
                            var awaitable = _timeoutMux.TryWaitAsync();
                            using (var inner = await awaitable)
                            {
                                lock (allDone)
                                {
                                    if (inner) success++;
                                    if (!awaitable.CompletedSynchronously) asyncOps++;
                                }
                                await Task.Delay(10);
                            }
                        });
                    }
                    Monitor.Wait(allReady);
                }
                await Task.Delay(100);
            }
            for (int i = 0; i < tasks.Length; i++)
            {   // deliberately not an await - we want a simple timeout here
                Assert.True(tasks[i].Wait(_timeoutMux.TimeoutMilliseconds));
            }

            lock (allDone)
            {
                Assert.Equal(COMPETITORS, success);
                Assert.Equal(COMPETITORS, asyncOps);
            }
        }

        [Fact]
        public async Task TimeoutAsync()
        {
            object allReady = new object(), allDone = new object();
            const int COMPETITORS = 5;
            int active = 0, success = 0, asyncOps = 0;

            var tasks = new Task[COMPETITORS];
            using (var token = await _timeoutMux.TryWaitAsync())
            {
                lock (allReady)
                {
                    for (int i = 0; i < tasks.Length; i++)
                    {
                        tasks[i] = Task.Run(async () =>
                        {
                            lock (allReady)
                            {
                                if (++active == COMPETITORS) Monitor.PulseAll(allReady);
                                else Monitor.Wait(allReady);
                            }
                            var awaitable = _timeoutMux.TryWaitAsync();
                            using (var inner = await awaitable)
                            {
                                lock (allDone)
                                {
                                    Log($"{inner.Success}, {awaitable.CompletedSynchronously}");
                                    if (inner) success++;
                                    if (!awaitable.CompletedSynchronously) asyncOps++;
                                }
                                await Task.Delay(10);
                            }
                        });
                    }
                    Monitor.Wait(allReady);
                }
                await Task.Delay(_timeoutMux.TimeoutMilliseconds * 2);
            }
            for (int i = 0; i < tasks.Length; i++)
            {   // deliberately not an await - we want a simple timeout here
                Assert.True(tasks[i].Wait(_timeoutMux.TimeoutMilliseconds));
            }

            lock (allDone)
            {
                Assert.Equal(0, success);
                Assert.Equal(COMPETITORS, asyncOps);
            }
        }
    }
}
