using System.Net;

namespace RESPite.StackExchange.Redis;

internal static class Utils
{
    internal static void LogLocked(this TextWriter? writer, string message)
    {
        if (writer is null) return;
        lock (writer)
        {
            writer.WriteLine(message);
        }
    }

#if NET10_0_OR_GREATER
    internal static void LogLocked(
        this TextWriter? writer,
        ref System.Runtime.CompilerServices.DefaultInterpolatedStringHandler message)
    {
        if (writer is null)
        {
            message.Clear();
        }
        else
        {
            lock (writer)
            {
                writer.WriteLine(message.ToStringAndClear());
            }
        }
    }
#endif
}
