using System.Threading;

namespace StackExchange.Redis
{
    internal class CancellationTokenSourcePool
    {
        private CancellationTokenSource[] store = new CancellationTokenSource[64];
        internal CancellationTokenSource Get()
        {
            CancellationTokenSource found;
            for (int i = 0; i < store.Length; i++)
            {
                if ((found = Interlocked.Exchange(ref store[i], null)) != null)
                {
                    return found;
                }
            }
            return new CancellationTokenSource();
        }

        internal void Return(CancellationTokenSource cts)
        {
            if (cts == null)
                return;
            for (int i = 0; i < store.Length; i++)
            {
                if (Interlocked.CompareExchange(ref store[i], cts, null) == null) return;
            }
        }
    }
}