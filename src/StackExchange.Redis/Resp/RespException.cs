using System;

namespace StackExchange.Redis.Resp;

/// <summary>
/// Represents a RESP error message.
/// </summary>
internal sealed class RespException(string message) : Exception(message)
{
}
