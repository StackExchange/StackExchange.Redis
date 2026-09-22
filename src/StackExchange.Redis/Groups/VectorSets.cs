using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis;

/// <summary>
/// EXPERIMENTAL SPIKE. The vectorsets commands.
/// </summary>
/// <remarks>
/// <para>
/// <b>Empty here, and that is the point.</b> A group is its own files sharing one name:
/// <c>VectorSets.cs</c> holds the group type and the accessor that reaches it, <c>VectorSets.Methods.cs</c>
/// the commands. Declaring the partial here is what makes the file named after the group the one to
/// open first.
/// </para>
/// <para>
/// <b>The accessor cannot live in this class</b> - a member named <c>VectorSets</c> inside a class named
/// <c>VectorSets</c> is <c>CS0542</c> - so it hangs off <see cref="RespDatabaseExtensions"/>, which every
/// group contributes its own accessor to.
/// </para>
/// </remarks>
// RS0026 warns about overloads carrying optional parameters, because adding one later can make an
// existing call ambiguous. That cannot arise here: every member of this class is an extension method on
// one group type, so two members sharing a name are always candidates for the same call - and within the
// group they differ in a parameter that has NO default. Per group, that claim is about a dozen methods
// with one receiver rather than about every command in the library at once.
[SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Extension members on one group type, differing in a non-defaulted parameter; see the comment above")]
public static partial class VectorSets
{
}

/// <summary>
/// EXPERIMENTAL SPIKE. The vector-set group: <c>target.VectorSets.AddAsync(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// The one group that was already the right shape: the old surface hands back <c>Lease&lt;float&gt;</c>
/// and <c>Lease&lt;VectorSetLink&gt;</c> rather than arrays, because vector sets arrived after that
/// argument was settled. So this is the rare move with no shape to decide - only
/// <see cref="ReadOnlyLease{T}"/> in place of <see cref="Lease{T}"/> on the public side, with the
/// writable siblings kept internal for <see cref="IDatabase"/>.
/// </para>
/// <para>
/// <c>VRANGE</c>'s enumerating form stays with the scans: it issues a command per batch, and deferred
/// execution is not a frame.
/// </para>
/// </remarks>
public readonly struct RespVectorSets
{
    /// <summary>Group the vector-set commands of a context.</summary>
    /// <param name="context">The context to send through.</param>
    public RespVectorSets(RespContext context) => Context = context;

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
        /// <summary>The vector-set commands.</summary>
        public RespVectorSets VectorSets => new(context.Raw);
    }

    extension<TTarget>(TTarget target) where TTarget : IRespKeyspaceTarget
    {
        /// <summary>The vector-set commands.</summary>
        public RespVectorSets VectorSets => target.Context.VectorSets;
    }
}
