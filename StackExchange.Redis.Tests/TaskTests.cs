using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StackExchange.Redis.Tests
{
    public class TaskTests
    {
#if DEBUG

#if !PLAT_SAFE_CONTINUATIONS // IsSyncSafe doesn't exist if PLAT_SAFE_CONTINUATIONS is defined
        [Theory]
        [InlineData(SourceOrign.NewTCS)]
        [InlineData(SourceOrign.Create)]
        public void VerifyIsSyncSafe(SourceOrign origin)
        {
            var source = Create<int>(origin);
            // Yes this looks stupid, but it's the proper pattern for how we statically init now
            // ...and if we're dropping NET45 support, we can just nuke it all.
#if NET462
            Assert.True(TaskSource.IsSyncSafe(source.Task));
#elif NETCOREAPP1_0
            Assert.True(TaskSource.IsSyncSafe(source.Task));
#elif NETCOREAPP2_0
            Assert.True(TaskSource.IsSyncSafe(source.Task));
#endif
        }
#endif
        private static TaskCompletionSource<T> Create<T>(SourceOrign origin)
        {
            switch (origin)
            {
                case SourceOrign.NewTCS: return new TaskCompletionSource<T>();
                case SourceOrign.Create: return TaskSource.Create<T>(null);
                default: throw new ArgumentOutOfRangeException(nameof(origin));
            }
        }

        [Theory]
        // regular framework behaviour: 2 out of 3 cause hijack
        [InlineData(SourceOrign.NewTCS, AttachMode.ContinueWith, true)]
        [InlineData(SourceOrign.NewTCS, AttachMode.ContinueWithExecSync, false)]
        [InlineData(SourceOrign.NewTCS, AttachMode.Await, true)]
        // Create is just a wrapper of ^^^; expect the same
        [InlineData(SourceOrign.Create, AttachMode.ContinueWith, true)]
        [InlineData(SourceOrign.Create, AttachMode.ContinueWithExecSync, false)]
        [InlineData(SourceOrign.Create, AttachMode.Await, true)]
        public void TestContinuationHijacking(SourceOrign origin, AttachMode attachMode, bool expectHijack)
        {
            TaskCompletionSource<int> source = Create<int>(origin);

            int settingThread = Environment.CurrentManagedThreadId;
            var state = new AwaitState();
            state.Attach(source.Task, attachMode);
            source.TrySetResult(123);
            state.Wait(); // waits for the continuation to run
            int from = state.Thread;
            Assert.NotEqual(-1, from); // not set
            if (expectHijack)
            {
                Assert.True(settingThread != from, $"expected hijack; didn't happen, Origin={settingThread}, Final={from}");
            }
            else
            {
                Assert.True(settingThread == from, $"setter was hijacked, Origin={settingThread}, Final={from}");
            }
        }

        public enum SourceOrign
        {
            NewTCS,
            Create
        }

        public enum AttachMode
        {
            ContinueWith,
            ContinueWithExecSync,
            Await
        }

        private class AwaitState
        {
            public int Thread => continuationThread;
            private volatile int continuationThread = -1;
            private readonly ManualResetEventSlim evt = new ManualResetEventSlim();
            public void Wait()
            {
                if (!evt.Wait(5000)) throw new TimeoutException();
            }

            public void Attach(Task task, AttachMode attachMode)
            {
                switch (attachMode)
                {
                    case AttachMode.ContinueWith:
                        task.ContinueWith(Continue);
                        break;
                    case AttachMode.ContinueWithExecSync:
                        task.ContinueWith(Continue, TaskContinuationOptions.ExecuteSynchronously);
                        break;
                    case AttachMode.Await:
                        DoAwait(task);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(attachMode));
                }
            }

            private void Continue(Task task)
            {
                continuationThread = Environment.CurrentManagedThreadId;
                evt.Set();
            }

            private async void DoAwait(Task task)
            {
                await task.ConfigureAwait(false);
                continuationThread = Environment.CurrentManagedThreadId;
                evt.Set();
            }
        }
#endif
    }
}