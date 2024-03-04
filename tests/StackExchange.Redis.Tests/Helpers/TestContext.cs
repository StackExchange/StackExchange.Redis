namespace StackExchange.Redis.Tests;

public class TestContext
{
    public IRedisTest Test { get; set; }

    public bool IsResp2 => Test.Protocol == RedisProtocol.Resp2;
    public bool IsResp3 => Test.Protocol == RedisProtocol.Resp3;

    public string KeySuffix => Test.Protocol switch
    {
        RedisProtocol.Resp2 => "R2",
        RedisProtocol.Resp3 => "R3",
        _ => "",
    };

    public TestContext(IRedisTest test) => Test = test;

    public override string ToString() => $"Protocol: {Test.Protocol}";
}
