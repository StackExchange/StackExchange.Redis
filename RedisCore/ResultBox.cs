using System;
using System.Threading;

namespace RedisCore
{
    internal sealed class ResultBox<T>
    {
        public object SyncLock => this;

        private T result;
        private Exception error;
        public void SetResult(Exception error, T result)
        {
            lock (SyncLock)
            {
                this.error = error;
                this.result = result;
                Monitor.Pulse(SyncLock);
            }
        }
        internal T WaitLocked()
        {
            Monitor.Wait(SyncLock);
            var error = this.error;
            if (error != null) throw error;
            return result;
        }

        [ThreadStatic]
        static ResultBox<T> instance;
        internal static ResultBox<T> Get()
        {
            var tmp = instance ?? new ResultBox<T>();
            instance = null;
            return tmp;
        }

        internal static void Put(ResultBox<T> box)
        {
            if (box != null)
            {
                box.Reset();
                instance = box;
            }
        }

        private void Reset()
        {
            error = null;
            result = default(T);
        }
    }
}
