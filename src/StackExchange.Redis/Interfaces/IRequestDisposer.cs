using System;

namespace StackExchange.Redis;

/// <summary>
/// Disposing the request data.
/// </summary>
public interface IRequestDisposer
{
    /// <summary>
    /// Disposing the request data.
    /// </summary>
    /// <param name="args">lua script request.</param>
    void Dispose(ReadOnlyMemory<RedisKeyOrValue> args);
}
