namespace RESPite.Shared;

internal static class TaskExtensions
{
    internal static void FireAndForget(this Task task) => task?.ContinueWith(
        static t => GC.KeepAlive(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
}
