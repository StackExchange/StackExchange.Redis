using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class TaskTests
    {
#if DEBUG

#if !PLAT_SAFE_CONTINUATIONS // IsSyncSafe doesn't exist if PLAT_SAFE_CONTINUATIONS is defined
        [Test]
        [TestCase(SourceOrign.NewTCS, false)]
        [TestCase(SourceOrign.Create, false)]
        [TestCase(SourceOrign.CreateDenyExec, true)]
        public void VerifyIsSyncSafe(SourceOrign origin, bool expected)
        {
            var source = Create<int>(origin);
            Assert.AreEqual(expected, TaskSource.IsSyncSafe(source.Task));
        }
#endif
        static TaskCompletionSource<T> Create<T>(SourceOrign origin)
        {
            switch (origin)
            {
                case SourceOrign.NewTCS: return new TaskCompletionSource<T>();
                case SourceOrign.Create: return TaskSource.Create<T>(null);
                case SourceOrign.CreateDenyExec: return TaskSource.CreateDenyExecSync<T>(null);
                default: throw new ArgumentOutOfRangeException(nameof(origin));
            }
        }
        [Test]
        // regular framework behaviour: 2 out of 3 cause hijack
        [TestCase(SourceOrign.NewTCS, AttachMode.ContinueWith, false)]
        [TestCase(SourceOrign.NewTCS, AttachMode.ContinueWithExecSync, true)]
        [TestCase(SourceOrign.NewTCS, AttachMode.Await, true)]
        // Create is just a wrapper of ^^^; expect the same
        [TestCase(SourceOrign.Create, AttachMode.ContinueWith, false)]
        [TestCase(SourceOrign.Create, AttachMode.ContinueWithExecSync, true)]
        [TestCase(SourceOrign.Create, AttachMode.Await, true)]
        // deny exec-sync: none should cause hijack
        [TestCase(SourceOrign.CreateDenyExec, AttachMode.ContinueWith, false)]
        [TestCase(SourceOrign.CreateDenyExec, AttachMode.ContinueWithExecSync, false)]
        [TestCase(SourceOrign.CreateDenyExec, AttachMode.Await, false)]
        public void TestContinuationHijacking(SourceOrign origin, AttachMode attachMode, bool expectHijack)
        {
            TaskCompletionSource<int> source = Create<int>(origin);           

            int settingThread = Environment.CurrentManagedThreadId;
            var state = new AwaitState();
            state.Attach(source.Task, attachMode);
            source.TrySetResult(123);
            state.Wait(); // waits for the continuation to run
            int from = state.Thread;
            Assert.AreNotEqual(-1, from, "not set");
            if (expectHijack)
            {
                Assert.AreEqual(settingThread, from, "expected hijack; didn't happen");
            }
            else
            {
                Assert.AreNotEqual(settingThread, from, "setter was hijacked");
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
        class AwaitState
        {
            public int Thread => continuationThread;
            volatile int continuationThread = -1;
            private ManualResetEventSlim evt = new ManualResetEventSlim();
            public void Wait()
            {
                if (!evt.Wait(5000)) throw new TimeoutException();
            }
            public void Attach(Task task, AttachMode attachMode)
            {
                switch(attachMode)
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

