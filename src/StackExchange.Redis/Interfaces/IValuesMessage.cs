namespace StackExchange.Redis
{
    internal interface IValuesMessage
    {
        RedisValue[] Values { get; }
    }
}
