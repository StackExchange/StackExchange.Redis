﻿using System;
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
        [InlineData(SourceOrign.NewTCS, false)]
        [InlineData(SourceOrign.Create, false)]
        [InlineData(SourceOrign.CreateDenyExec, true)]
        public void VerifyIsSyncSafe(SourceOrign origin, bool expected)
        {
            var source = Create<int>(origin);
            Assert.Equal(expected, TaskSource.IsSyncSafe(source.Task));
        }
#endif
        private static TaskCompletionSource<T> Create<T>(SourceOrign origin)
        {
            switch (origin)
            {
                case SourceOrign.NewTCS: return new TaskCompletionSource<T>();
                case SourceOrign.Create: return TaskSource.Create<T>(null);
                case SourceOrign.CreateDenyExec: return TaskSource.CreateDenyExecSync<T>(null);
                default: throw new ArgumentOutOfRangeException(nameof(origin));
            }
        }

        [Theory]
        // regular framework behaviour: 2 out of 3 cause hijack
        [InlineData(SourceOrign.NewTCS, AttachMode.ContinueWith, false)]
        [InlineData(SourceOrign.NewTCS, AttachMode.ContinueWithExecSync, true)]
        [InlineData(SourceOrign.NewTCS, AttachMode.Await, true)]
        // Create is just a wrapper of ^^^; expect the same
        [InlineData(SourceOrign.Create, AttachMode.ContinueWith, false)]
        [InlineData(SourceOrign.Create, AttachMode.ContinueWithExecSync, true)]
        [InlineData(SourceOrign.Create, AttachMode.Await, true)]
        // deny exec-sync: none should cause hijack
        [InlineData(SourceOrign.CreateDenyExec, AttachMode.ContinueWith, false)]
        [InlineData(SourceOrign.CreateDenyExec, AttachMode.ContinueWithExecSync, false)]
        [InlineData(SourceOrign.CreateDenyExec, AttachMode.Await, false)]
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
                Assert.True(settingThread == from, "expected hijack; didn't happen");
            }
            else
            {
                Assert.False(settingThread == from, "setter was hijacked");
            }
        }

        public enum SourceOrign
        {
            NewTCS,
            Create,
            CreateDenyExec
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