namespace RESPite.Resp.Commands;

/// <summary>
/// Operations relating to lists.
/// </summary>
public static class Lists
{
    /// <summary>
    /// Returns the length of the list stored at key.
    /// </summary>
    public static RespCommand<SimpleString, long> LLEN { get; } = new(default);
}
