namespace Resp.RedisCommands;

public readonly partial struct RedisDatabase
{
    private readonly RespContext _context;

    public RedisDatabase(in RespContext context)
        => _context = context;

    [RespCommand]
    public partial void FlushDb();
}

public readonly partial struct RedisStrings
{
    private readonly RespContext _context;

    public RedisStrings(in RespContext context)
        => _context = context;

    [RespCommand]
    public partial string? Get(string key);

    [RespCommand]
    public partial void Set(string key, string value);

    [RespCommand]
    public partial void Set(string key, int value);

    [RespCommand]
    public partial int Incr(string key);

    [RespCommand]
    public partial int IncrBy(string key, int value);

    [RespCommand]
    public partial int Decr(string key);

    [RespCommand]
    public partial int DecrBy(string key, int value);
}
