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
    /// <summary>Group the HyperLogLog commands of a context.</summary>
    /// <param name="context">The context to send through.</param>
    public RespHyperLogLog(in RespContext context) => Context = context;

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
        /// <summary>The HyperLogLog commands.</summary>
        public RespHyperLogLog HyperLogLog => new(context.Raw);
    }

    extension<TTarget>(TTarget target) where TTarget : IRespKeyspaceTarget
    {
        /// <summary>The HyperLogLog commands.</summary>
        public RespHyperLogLog HyperLogLog => target.Context.HyperLogLog;
    }
}
