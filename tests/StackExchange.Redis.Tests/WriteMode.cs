namespace StackExchange.Redis.Tests;

public enum WriteMode
{
    Default = (int)BufferedStreamWriter.WriteMode.Default,
    Sync = (int)BufferedStreamWriter.WriteMode.Sync,
    Async = (int)BufferedStreamWriter.WriteMode.Async,
    Pipe = (int)BufferedStreamWriter.WriteMode.Pipe,
}
