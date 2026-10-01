using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis;

/// <summary>
/// EXPERIMENTAL SPIKE. The strings commands.
/// </summary>
/// <remarks>
/// <para>
/// <b>Empty here, and that is the point.</b> A group is its own files sharing one name:
/// <c>Strings.cs</c> holds the group type and the accessor that reaches it, <c>Strings.Methods.cs</c>
/// the commands. Declaring the partial here is what makes the file named after the group the one to
/// open first.
/// </para>
/// <para>
/// <b>The accessor cannot live in this class</b> - a member named <c>Strings</c> inside a class named
/// <c>Strings</c> is <c>CS0542</c> - so it hangs off <see cref="RespDatabaseExtensions"/>, which every
/// group contributes its own accessor to.
/// </para>
/// </remarks>
// RS0026 warns about overloads carrying optional parameters, because adding one later can make an
// existing call ambiguous. That cannot arise here: every member of this class is an extension method on
// one group type, so two members sharing a name are always candidates for the same call - and within the
// group they differ in a parameter that has NO default. Per group, that claim is about a dozen methods
// with one receiver rather than about every command in the library at once.
[SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Extension members on one group type, differing in a non-defaulted parameter; see the comment above")]
public static partial class Strings
{
}

/// <summary>
/// EXPERIMENTAL SPIKE. The string-command group: <c>target.Strings.SetAsync(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// A plain wrapper over one <see cref="RespContext"/> field - deliberately NOT a reinterpret-cast of a
/// layout-compatible struct. Holding exactly one field of that type makes the layout identical <i>by
/// construction</i>, so the wrapper IS the pun, enforced by the compiler and with no <c>Unsafe</c>. A
/// by-value pun would copy the same bytes anyway; only a <c>ref</c> pun avoids the copy, and that
/// requires a stable address, which drags <c>ref readonly</c> and its lifetime rules into every caller
/// to save a few register moves ahead of a network round trip.
/// </para>
/// <para>
/// Not a <c>ref struct</c>, for the same reason <see cref="RespContext"/> is not: these have to survive
/// an <c>await</c>.
/// </para>
/// </remarks>
public readonly struct RespStrings
{
    /// <summary>Group the string commands of a context.</summary>
    /// <param name="context">The context to send through.</param>
    public RespStrings(RespContext context) => Context = context;

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
        /// <summary>The string commands.</summary>
        public RespStrings Strings => new(context.Raw);
    }

    extension<TTarget>(TTarget target) where TTarget : IRespKeyspaceTarget
    {
        /// <summary>The string commands.</summary>
        public RespStrings Strings => target.Context.Strings;
    }
}
