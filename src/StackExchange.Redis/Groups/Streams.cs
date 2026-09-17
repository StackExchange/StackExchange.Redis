using System;
using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// EXPERIMENTAL SPIKE. The stream commands and the shapes they answer with.
/// </summary>
/// <remarks>
/// <para>
/// <b>Empty here, and that is the point.</b> The stream surface is three files sharing this name:
/// <c>Streams.cs</c> holds the group type and the accessor that reaches it, <c>Streams.Methods.cs</c> the
/// commands, <c>Streams.Types.cs</c> the reply shapes. Declaring the partial here is what makes the file
/// named after the group the one to open first, and what the other two hang off.
/// </para>
/// <para>
/// <b>The accessor cannot live in this class</b>, which is a compiler fact and not a preference: a member
/// named <c>Streams</c> inside a class named <c>Streams</c> is <c>CS0542</c>. So it hangs off
/// <see cref="RespDatabaseExtensions"/> instead, which every group contributes its own accessor to.
/// </para>
/// </remarks>
// RS0026 warns about overloads that carry optional parameters, because adding one later can make an
// existing call ambiguous. That hazard cannot arise here, and saying so once beats a pragma per command:
// every member of this class is an extension method on RespStreams, so two members sharing a name are
// always candidates for the same call - and within the group they differ in a parameter that has NO
// default (a span versus a single id, a trim mode that changes what the reply is). Splitting the surface
// per group is what makes that checkable: the claim is now about a dozen methods with one receiver,
// rather than about every command in the library at once.
[SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Extension members on one group type, differing in a non-defaulted parameter; see the comment above")]
public static partial class Streams
{
}

/// <summary>
/// EXPERIMENTAL SPIKE. The stream command group: <c>target.Streams.LengthAsync(...)</c>.
/// </summary>
/// <remarks>
/// A group is a context plus a name, and nothing else - it exists so the commands have something to hang
/// off that says which family they belong to. It is a <c>readonly struct</c> so that reaching for a group
/// costs nothing beyond copying the context.
/// </remarks>
public readonly struct RespStreams
{
    private readonly RespContext _context;

    /// <summary>Group the stream commands of a context.</summary>
    /// <param name="context">The context to send through.</param>
    public RespStreams(in RespContext context) => _context = context;

    /// <summary>The underlying context.</summary>
    public RespContext Context => _context;
}

/// <summary>
/// EXPERIMENTAL SPIKE. Reaches the command groups from a database or a context.
/// </summary>
/// <remarks>
/// <b>One class, assembled from the group files.</b> Each group declares its own accessor beside its own
/// type rather than this being a separate list to maintain - so adding a group is one file, and this
/// cannot fall out of step with what exists. It has to be a class of its own rather than living on each
/// group, because of <c>CS0542</c>; see <see cref="Streams"/>.
/// </remarks>
public static partial class RespDatabaseExtensions
{
    extension(in RespDatabaseContext context)
    {
        /// <summary>The stream commands.</summary>
        public RespStreams Streams => new(context.Raw);
    }

    extension<TTarget>(TTarget target) where TTarget : IRespKeyspaceTarget
    {
        /// <summary>The stream commands.</summary>
        public RespStreams Streams => target.Context.Streams;
    }
}
