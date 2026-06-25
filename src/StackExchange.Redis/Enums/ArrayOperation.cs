using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Describes an array aggregation operation.
/// </summary>
[Experimental(Experiments.Server_8_8, UrlFormat = Experiments.UrlFormat)]
public enum ArrayOperation
{
    /// <summary>
    /// An unknown operation.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Computes the sum of values in the range.
    /// </summary>
    Sum,

    /// <summary>
    /// Finds the minimum value in the range.
    /// </summary>
    Min,

    /// <summary>
    /// Finds the maximum value in the range.
    /// </summary>
    Max,

    /// <summary>
    /// Computes a bitwise AND over values in the range.
    /// </summary>
    And,

    /// <summary>
    /// Computes a bitwise OR over values in the range.
    /// </summary>
    Or,

    /// <summary>
    /// Computes a bitwise XOR over values in the range.
    /// </summary>
    Xor,

    /// <summary>
    /// Counts values in the range that match an operand.
    /// </summary>
    Match,

    /// <summary>
    /// Counts used cells in the range.
    /// </summary>
    Used,
}
