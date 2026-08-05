using Microsoft.CodeAnalysis;

namespace StackExchange.Redis.Build;

/// <summary>
/// Values that exist in newer Roslyn than we compile against.
/// </summary>
/// <remarks>
/// This assembly ships as an analyzer inside the StackExchange.Redis package, so it is deliberately built
/// against an old Roslyn to stay loadable in older hosts (see <c>Directory.Packages.props</c>). That is a
/// *compile-time* floor only: at run-time we are hosted by the consumer's compiler, which may be far newer
/// and can therefore hand us values that did not exist when we were built. Matching on the numeric value
/// keeps us correct in both directions, so prefer a shim here over raising the floor.
/// </remarks>
internal static class RefKinds
{
    /// <summary><c>ref readonly</c> parameters (C# 12); <see cref="RefKind"/> gained this in Roslyn 4.8.</summary>
    public const RefKind RefReadOnlyParameter = (RefKind)4;
}
