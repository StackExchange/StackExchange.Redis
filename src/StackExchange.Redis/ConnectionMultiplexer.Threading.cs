using System;
using System.Threading;
using Pipelines.Sockets.Unofficial;

namespace StackExchange.Redis;

public partial class ConnectionMultiplexer
{
    private static readonly WaitCallback s_CompleteAsWorker = s => ((ICompletable)s!).TryComplete(true);
    internal static void CompleteAsWorker(ICompletable completable)
    {
        if (completable is not null)
        {
            ThreadPool.QueueUserWorkItem(s_CompleteAsWorker, completable);
        }
    }

    internal static bool TryCompleteHandler<T>(EventHandler<T>? handler, object sender, T args, bool isAsync) where T : EventArgs, ICompletable
    {
        if (handler is null) return true;
        if (isAsync)
        {
            if (handler.IsSingle())
            {
                try
                {
                    handler(sender, args);
                }
                catch { }
            }
            else
            {
                foreach (EventHandler<T> sub in handler.AsEnumerable())
                {
                    try
                    {
                        sub(sender, args);
                    }
                    catch { }
                }
            }
            return true;
        }
        else
        {
            return false;
        }
    }
}
