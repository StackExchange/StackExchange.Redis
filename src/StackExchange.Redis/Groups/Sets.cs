using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;
using StackExchange.Redis.Interpolated;

namespace StackExchange.Redis;

/// <summary>
/// EXPERIMENTAL SPIKE. The sets commands.
/// </summary>
/// <remarks>
/// <para>
/// <b>Empty here, and that is the point.</b> A group is its own files sharing one name:
/// <c>Sets.cs</c> holds the group type and the accessor that reaches it, <c>Sets.Methods.cs</c>
/// the commands. Declaring the partial here is what makes the file named after the group the one to
/// open first.
/// </para>
/// <para>
/// <b>The accessor cannot live in this class</b> - a member named <c>Sets</c> inside a class named
/// <c>Sets</c> is <c>CS0542</c> - so it hangs off <see cref="RespDatabaseExtensions"/>, which every
/// group contributes its own accessor to.
/// </para>
/// </remarks>
// RS0026 warns about overloads carrying optional parameters, because adding one later can make an
// existing call ambiguous. That cannot arise here: every member of this class is an extension method on
// one group type, so two members sharing a name are always candidates for the same call - and within the
// group they differ in a parameter that has NO default. Per group, that claim is about a dozen methods
// with one receiver rather than about every command in the library at once.
[SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Extension members on one group type, differing in a non-defaulted parameter; see the comment above")]
public static partial class Sets
{
}

/// <summary>
/// EXPERIMENTAL SPIKE. The set-command group: <c>target.Sets.AddAsync(...)</c>.
/// </summary>
/// <remarks>
/// The smallest group so far, and the one where the variadic hole does most of the work: nearly every
/// command here takes either a run of members or a run of keys, and the old surface spells each of
/// those as a pair of overloads - one fixed-arity, one array. <c>SSCAN</c> stays with the other
/// cursors.
/// </remarks>
public readonly struct RespSets
{
    private readonly RespContext _context;

    /// <summary>Group the set commands of a context.</summary>
    /// <param name="context">The context to send through.</param>
    public RespSets(in RespContext context) => _context = context;

    /// <summary>The underlying context.</summary>
    public RespContext Context => _context;
}

public static partial class RespDatabaseExtensions
{
    extension(IRespKeyspaceTarget target)
    {
        /// <summary>The set commands.</summary>
        public RespSets Sets => new(target.Context);
    }

    extension(in RespContext context)
    {
        /// <summary>The set commands.</summary>
        public RespSets Sets => new(context);
    }
}
