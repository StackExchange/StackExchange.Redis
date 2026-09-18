using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis;

/// <summary>
/// EXPERIMENTAL SPIKE. The lists commands.
/// </summary>
/// <remarks>
/// <para>
/// <b>Empty here, and that is the point.</b> A group is its own files sharing one name:
/// <c>Lists.cs</c> holds the group type and the accessor that reaches it, <c>Lists.Methods.cs</c>
/// the commands. Declaring the partial here is what makes the file named after the group the one to
/// open first.
/// </para>
/// <para>
/// <b>The accessor cannot live in this class</b> - a member named <c>Lists</c> inside a class named
/// <c>Lists</c> is <c>CS0542</c> - so it hangs off <see cref="RespDatabaseExtensions"/>, which every
/// group contributes its own accessor to.
/// </para>
/// </remarks>
// RS0026 warns about overloads carrying optional parameters, because adding one later can make an
// existing call ambiguous. That cannot arise here: every member of this class is an extension method on
// one group type, so two members sharing a name are always candidates for the same call - and within the
// group they differ in a parameter that has NO default. Per group, that claim is about a dozen methods
// with one receiver rather than about every command in the library at once.
[SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Extension members on one group type, differing in a non-defaulted parameter; see the comment above")]
public static partial class Lists
{
}

/// <summary>
/// EXPERIMENTAL SPIKE. The list-command group: <c>target.Lists.LeftPushAsync(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// The old surface spells these with a <c>List</c> prefix, so within the group it goes:
/// <c>ListLeftPop</c> becomes <c>Lists.LeftPop</c>. The left/right halves stay in the method name
/// rather than becoming a <see cref="ListSide"/> parameter, because that is how the server names the
/// commands and how every caller already thinks about them - <c>LPUSH</c> and <c>RPUSH</c> are two
/// commands, not one with an operand. <c>Move</c> is the exception, and there the side genuinely is an
/// operand.
/// </para>
/// <para>
/// <c>RPOPLPUSH</c> is <b>not</b> here: it is exactly <c>LMOVE src dst RIGHT LEFT</c>, deprecated in
/// its favour since 6.2, and the same kind of spelling relic as <c>GETSET</c>. The adapter keeps the
/// old name working by naming the sides.
/// </para>
/// </remarks>
public readonly struct RespLists
{
    /// <summary>Group the list commands of a context.</summary>
    /// <param name="context">The context to send through.</param>
    public RespLists(in RespContext context) => Context = context;

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
        /// <summary>The list commands.</summary>
        public RespLists Lists => new(context.Raw);
    }

    extension<TTarget>(TTarget target) where TTarget : IRespKeyspaceTarget
    {
        /// <summary>The list commands.</summary>
        public RespLists Lists => target.Context.Lists;
    }
}
