using System;

namespace RESPite.Redis;

// note that members may also be added as extensions if necessary
public readonly partial struct RedisKeys(in RespContext context)
{
    // ReSharper disable once UnusedMember.Local
    private readonly RespContext _context = context;

    [RespCommand]
    public partial void Del(string key);

    [RespCommand(Formatter = Formatters.KeyStringArray)]
    public partial int Del(ReadOnlyMemory<string> keys);
}
