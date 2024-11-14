using RESPite.Resp.Commands;
using static RESPite.Resp.Client.CommandFactory;

namespace RESPite.Resp.KeyValueStore;

/// <summary>
/// Operations relating to binary string payloads.
/// </summary>
public static class Sets
{
    /// <summary>
    /// Returns the set cardinality (number of elements) of the set stored at key.
    /// </summary>
    public static readonly RespCommand<SimpleString, long> SCARD = new(Default);
}
