#nullable enable
namespace StackExchange.Redis
{
    internal interface IWriteState
    {
        CommandMap CommandMap { get; }
        byte[]? ChannelPrefix { get; }
    }
}
