using RESPite.Resp.Commands;
using static RESPite.Resp.Client.CommandFactory;

namespace RESPite.Resp.KeyValueStore;

/// <summary>
/// Operations relating to lists.
/// </summary>
public static class Lists
{
    /// <summary>
    /// Returns the length of the list stored at key.
    /// </summary>
    public static readonly RespCommand<SimpleString, long> LLEN = new(Default);

    /// <summary>
    /// Returns the element at index index in the list stored at key. The index is zero-based, so 0 means the first element, 1 the second element and so on. Negative indices can be used to designate elements starting at the tail of the list. Here, -1 means the last element, -2 means the penultimate and so forth.
    /// </summary>
    public static readonly RespCommand<(SimpleString Key, int Index), LeasedString> LINDEX = new(Default);
}
