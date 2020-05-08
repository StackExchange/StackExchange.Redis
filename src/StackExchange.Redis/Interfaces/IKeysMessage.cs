namespace StackExchange.Redis
{
    internal interface IKeysMessage
    {
        RedisKey[] Keys { get; }
    }
}
