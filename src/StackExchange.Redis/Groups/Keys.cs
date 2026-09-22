using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis;

/// <summary>
/// EXPERIMENTAL SPIKE. The keys commands.
/// </summary>
/// <remarks>
/// <para>
/// <b>Empty here, and that is the point.</b> A group is its own files sharing one name:
/// <c>Keys.cs</c> holds the group type and the accessor that reaches it, <c>Keys.Methods.cs</c>
/// the commands. Declaring the partial here is what makes the file named after the group the one to
/// open first.
/// </para>
/// <para>
/// <b>The accessor cannot live in this class</b> - a member named <c>Keys</c> inside a class named
/// <c>Keys</c> is <c>CS0542</c> - so it hangs off <see cref="RespDatabaseExtensions"/>, which every
/// group contributes its own accessor to.
/// </para>
/// </remarks>
// RS0026 warns about overloads carrying optional parameters, because adding one later can make an
// existing call ambiguous. That cannot arise here: every member of this class is an extension method on
// one group type, so two members sharing a name are always candidates for the same call - and within the
// group they differ in a parameter that has NO default. Per group, that claim is about a dozen methods
// with one receiver rather than about every command in the library at once.
[SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Extension members on one group type, differing in a non-defaulted parameter; see the comment above")]
public static partial class Keys
{
}

/// <summary>
/// EXPERIMENTAL SPIKE. The key-command group: <c>target.Keys.DeleteAsync(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// The old surface spells these with a <c>Key</c> prefix - <c>KeyDelete</c>, <c>KeyExpire</c>,
/// <c>KeyTimeToLive</c> - which was the only way to group them when everything hung off one interface.
/// Here the group is the receiver, so the prefix goes and <c>Keys.Delete</c> says the same thing.
/// </para>
/// <para>
/// Named <c>Keys</c> rather than <c>Keyspace</c>: it matches the other groups, which are all
/// plural-of-the-thing, and <c>Keyspace</c> already means something else in this library - see the
/// <c>KeyspaceIsolation</c> namespace, which is about prefixing rather than about key commands.
/// </para>
/// <para>
/// <c>DBSIZE</c> is <b>not</b> here despite looking like it belongs: it is an <c>IServer</c> command,
/// not a database one, and it lands in that context when it exists. The <c>OBJECT</c> family
/// (<c>ENCODING</c>, <c>REFCOUNT</c>, <c>FREQ</c>, <c>IDLETIME</c>) is deferred for the same reason
/// the scan cursors were: a different command shape, better done together.
/// </para>
/// </remarks>
public readonly struct RespKeys
{
    /// <summary>Group the key commands of a context.</summary>
    /// <param name="context">The context to send through.</param>
    public RespKeys(RespContext context) => Context = context;

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
        /// <summary>The key commands.</summary>
        public RespKeys Keys => new(context.Raw);
    }

    extension<TTarget>(TTarget target) where TTarget : IRespKeyspaceTarget
    {
        /// <summary>The key commands.</summary>
        public RespKeys Keys => target.Context.Keys;
    }
}
