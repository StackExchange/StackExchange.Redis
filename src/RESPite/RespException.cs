using System.Diagnostics.CodeAnalysis;
using StackExchange.Redis;

namespace RESPite;

/// <summary>
/// Represents a RESP error message.
/// </summary>
[Experimental(Experiments.Respite, UrlFormat = Experiments.UrlFormat)]
public sealed class RespException(string message) : Exception(message)
{
}
