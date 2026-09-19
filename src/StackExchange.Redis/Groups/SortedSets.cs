using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis;

/// <summary>
/// EXPERIMENTAL SPIKE. The sortedsets commands.
/// </summary>
/// <remarks>
/// <para>
/// <b>Empty here, and that is the point.</b> A group is its own files sharing one name:
/// <c>SortedSets.cs</c> holds the group type and the accessor that reaches it, <c>SortedSets.Methods.cs</c>
/// the commands. Declaring the partial here is what makes the file named after the group the one to
/// open first.
/// </para>
/// <para>
/// <b>The accessor cannot live in this class</b> - a member named <c>SortedSets</c> inside a class named
/// <c>SortedSets</c> is <c>CS0542</c> - so it hangs off <see cref="RespDatabaseExtensions"/>, which every
/// group contributes its own accessor to.
/// </para>
/// </remarks>
// RS0026 warns about overloads carrying optional parameters, because adding one later can make an
// existing call ambiguous. That cannot arise here: every member of this class is an extension method on
// one group type, so two members sharing a name are always candidates for the same call - and within the
// group they differ in a parameter that has NO default. Per group, that claim is about a dozen methods
// with one receiver rather than about every command in the library at once.
[SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Extension members on one group type, differing in a non-defaulted parameter; see the comment above")]
public static partial class SortedSets
{
}

/// <summary>
/// EXPERIMENTAL SPIKE. The sorted-set command group: <c>target.SortedSets.AddAsync(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// The largest group in the library, and the one with the most formatting the wire cares about and the
/// caller does not: exclusive bounds carry a <c>(</c>, lexical bounds a <c>[</c>, a descending range
/// swaps its own limits, and <c>ZADD</c> has six independent option tokens. All of that is shared with
/// the <c>MessageWriter</c> path rather than restated - a second copy of a bound convention is a silent
/// off-by-one-bound waiting to happen.
/// </para>
/// <para>
/// <c>ZSCAN</c> stays with the other cursors.
/// </para>
/// </remarks>
public readonly struct RespSortedSets
{
    /// <summary>Group the sorted-set commands of a context.</summary>
    /// <param name="context">The context to send through.</param>
    public RespSortedSets(RespContext context) => Context = context;

    /// <summary>The context these commands are sent through.</summary>
    /// <remarks>
    /// <b>An internal field, not a public property.</b> A group is a context plus a name, and the name is
    /// the whole point: handing the bare <see cref="RespContext"/> back out would undo it, the same way a
    /// public <c>Raw</c> did on the typed contexts. Inside the assembly it stays a plain field, so every
    /// command method reads exactly as it did.
    /// </remarks>
    internal readonly RespContext Context;
}

public static partial class RespDatabaseExtensions
{
    extension(in RespDatabaseContext context)
    {
        /// <summary>The sorted-set commands.</summary>
        public RespSortedSets SortedSets => new(context.Raw);
    }

    extension<TTarget>(TTarget target) where TTarget : IRespKeyspaceTarget
    {
        /// <summary>The sorted-set commands.</summary>
        public RespSortedSets SortedSets => target.Context.SortedSets;
    }
}
