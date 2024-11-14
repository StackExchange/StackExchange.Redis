namespace RESPite.Resp.Commands;

/// <summary>
/// Operations relating to binary string payloads.
/// </summary>
public static class Sets
{
    /// <summary>
    /// Returns the set cardinality (number of elements) of the set stored at key.
    /// </summary>
    public static RespCommand<SimpleString, long> SCARD { get; } = new(default);
}
