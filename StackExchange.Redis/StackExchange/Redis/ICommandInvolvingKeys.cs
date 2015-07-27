namespace StackExchange.Redis
{
    internal interface ICommandInvolvingKeys
    {
        RedisKey[] InvolvedKeys { get; }
    }
}