using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis;

/// <summary>
/// EXPERIMENTAL SPIKE. The hyperloglog commands.
/// </summary>
/// <remarks>
/// <para>
/// <b>Empty here, and that is the point.</b> A group is its own files sharing one name:
/// <c>HyperLogLog.cs</c> holds the group type and the accessor that reaches it, <c>HyperLogLog.Methods.cs</c>
/// the commands. Declaring the partial here is what makes the file named after the group the one to
/// open first.
/// </para>
/// <para>
/// <b>The accessor cannot live in this class</b> - a member named <c>HyperLogLog</c> inside a class named
/// <c>HyperLogLog</c> is <c>CS0542</c> - so it hangs off <see cref="RespDatabaseExtensions"/>, which every
/// group contributes its own accessor to.
/// </para>
/// </remarks>
// RS0026 warns about overloads carrying optional parameters, because adding one later can make an
// existing call ambiguous. That cannot arise here: every member of this class is an extension method on
// one group type, so two members sharing a name are always candidates for the same call - and within the
// group they differ in a parameter that has NO default. Per group, that claim is about a dozen methods
// with one receiver rather than about every command in the library at once.
[SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Extension members on one group type, differing in a non-defaulted parameter; see the comment above")]
public static partial class HyperLogLog
{
}

/// <summary>
/// EXPERIMENTAL SPIKE. The HyperLogLog group: <c>target.HyperLogLog.AddAsync(...)</c>.
/// </summary>
/// <remarks>
/// Three commands, and the whole group would be unremarkable but for <c>PFCOUNT</c>: it rewrites the
/// cached cardinality into the key, so before 2.8.18 it is a write however much it reads. That is a
/// routing decision rather than a spelling one - the same bytes go out either way - which makes it the
/// first user of <see cref="IRespServerFeatures"/> that changes the flags instead of the command.
/// </remarks>
public readonly struct RespHyperLogLog
{
    private readonly RespContext _context;

    /// <summary>Group the HyperLogLog commands of a context.</summary>
    /// <param name="context">The context to send through.</param>
    public RespHyperLogLog(in RespContext context) => _context = context;

    /// <summary>The underlying context.</summary>
    public RespContext Context => _context;
}

public static partial class RespDatabaseExtensions
{
    extension<TTarget>(TTarget target) where TTarget : IRespKeyspaceTarget
    {
        /// <summary>The HyperLogLog commands.</summary>
        public RespHyperLogLog HyperLogLog => new(target.Context);
    }

    extension(in RespContext context)
    {
        /// <summary>The HyperLogLog commands.</summary>
        public RespHyperLogLog HyperLogLog => new(context);
    }
}
