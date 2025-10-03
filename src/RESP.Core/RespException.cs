using System;

namespace Resp;

/// <summary>
/// Represents a RESP error message.
/// </summary>
public sealed class RespException(string message) : Exception(message)
{
}
