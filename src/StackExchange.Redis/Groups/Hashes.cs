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
/// EXPERIMENTAL SPIKE. The hashes commands.
/// </summary>
/// <remarks>
/// <para>
/// <b>Empty here, and that is the point.</b> A group is its own files sharing one name:
/// <c>Hashes.cs</c> holds the group type and the accessor that reaches it, <c>Hashes.Methods.cs</c>
/// the commands. Declaring the partial here is what makes the file named after the group the one to
/// open first.
/// </para>
/// <para>
/// <b>The accessor cannot live in this class</b> - a member named <c>Hashes</c> inside a class named
/// <c>Hashes</c> is <c>CS0542</c> - so it hangs off <see cref="RespDatabaseExtensions"/>, which every
/// group contributes its own accessor to.
/// </para>
/// </remarks>
// RS0026 warns about overloads carrying optional parameters, because adding one later can make an
// existing call ambiguous. That cannot arise here: every member of this class is an extension method on
// one group type, so two members sharing a name are always candidates for the same call - and within the
// group they differ in a parameter that has NO default. Per group, that claim is about a dozen methods
// with one receiver rather than about every command in the library at once.
[SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Extension members on one group type, differing in a non-defaulted parameter; see the comment above")]
public static partial class Hashes
{
}

/// <summary>
/// EXPERIMENTAL SPIKE. The hash-command group: <c>target.Hashes.GetAsync(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// The group where the <see cref="Expiration"/> collapse pays for itself most: the old surface spells
/// the field-lifetime commands as <c>TimeSpan?</c> overloads, <c>DateTime</c> overloads and separate
/// <c>persist</c>/<c>keepTtl</c> booleans, which is four methods where there is one request. One
/// <see cref="Expiration"/> parameter says all of it, and picks between <c>HEXPIRE</c>,
/// <c>HPEXPIRE</c>, <c>HEXPIREAT</c> and <c>HPEXPIREAT</c> on the way past.
/// </para>
/// <para>
/// Two things stay behind. <c>HSCAN</c>/<c>HSCANNOVALUES</c> are deferred-execution cursors, which is
/// not a frame; and <c>HIMPORT</c> needs its <c>PREPARE</c> injected onto the same physical connection,
/// which is a property of the write path rather than of the command.
/// </para>
/// </remarks>
public readonly struct RespHashes
{
    private readonly RespContext _context;

    /// <summary>Group the hash commands of a context.</summary>
    /// <param name="context">The context to send through.</param>
    public RespHashes(in RespContext context) => _context = context;

    /// <summary>The underlying context.</summary>
    public RespContext Context => _context;
}

public static partial class RespDatabaseExtensions
{
    extension(IRespKeyspaceTarget target)
    {
        /// <summary>The hash commands.</summary>
        public RespHashes Hashes => new(target.Context);
    }

    extension(in RespContext context)
    {
        /// <summary>The hash commands.</summary>
        public RespHashes Hashes => new(context);
    }
}
