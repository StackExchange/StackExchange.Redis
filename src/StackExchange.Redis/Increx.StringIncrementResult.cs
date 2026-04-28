using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Represents the result of an increment operation including the resulting value and the increment actually applied.
/// </summary>
/// <typeparam name="T">The numeric type represented by the result.</typeparam>
[Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
public readonly struct StringIncrementResult<T>(T value, T appliedIncrement)
{
    /// <summary>
    /// The resulting value after the increment operation.
    /// </summary>
    public T Value { get; } = value;

    /// <summary>
    /// The increment that was actually applied.
    /// </summary>
    public T AppliedIncrement { get; } = appliedIncrement;
}
