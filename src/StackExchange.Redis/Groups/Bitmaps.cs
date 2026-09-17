using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis;

/// <summary>
/// EXPERIMENTAL SPIKE. The bitmaps commands.
/// </summary>
/// <remarks>
/// <para>
/// <b>Empty here, and that is the point.</b> A group is its own files sharing one name:
/// <c>Bitmaps.cs</c> holds the group type and the accessor that reaches it, <c>Bitmaps.Methods.cs</c>
/// the commands. Declaring the partial here is what makes the file named after the group the one to
/// open first.
/// </para>
/// <para>
/// <b>The accessor cannot live in this class</b> - a member named <c>Bitmaps</c> inside a class named
/// <c>Bitmaps</c> is <c>CS0542</c> - so it hangs off <see cref="RespDatabaseExtensions"/>, which every
/// group contributes its own accessor to.
/// </para>
/// </remarks>
// RS0026 warns about overloads carrying optional parameters, because adding one later can make an
// existing call ambiguous. That cannot arise here: every member of this class is an extension method on
// one group type, so two members sharing a name are always candidates for the same call - and within the
// group they differ in a parameter that has NO default. Per group, that claim is about a dozen methods
// with one receiver rather than about every command in the library at once.
[SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Extension members on one group type, differing in a non-defaulted parameter; see the comment above")]
public static partial class Bitmaps
{
}

/// <summary>
/// EXPERIMENTAL SPIKE. The bitmap-command group: <c>target.Bitmaps.CountAsync(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Its own group, not a corner of <see cref="RespStrings"/>.</b> A bitmap is physically a string on
/// the server, and the old surface says so by name - <c>StringBitCount</c>, <c>StringSetBit</c>. But
/// the whole argument for grouping (design notes 9.4, reason 1) is that the shape of the API should
/// teach the API, and Redis documents bitmaps as a type of their own with its own page. A caller
/// reaching for <c>BITPOS</c> is thinking about bitmaps, not about the fact that the bytes live in a
/// string key; <c>ctx.Bitmaps.</c> is the list they wanted, and <c>ctx.Strings.</c> stays the list
/// someone storing a value wanted.
/// </para>
/// <para>
/// The adapters in <c>TransitionalDatabase.Bitmaps.cs</c> keep the old <c>StringBitXxx</c> names
/// working, so the regrouping costs existing callers nothing.
/// </para>
/// </remarks>
public readonly struct RespBitmaps
{
    private readonly RespContext _context;

    /// <summary>Group the bitmap commands of a context.</summary>
    /// <param name="context">The context to send through.</param>
    public RespBitmaps(in RespContext context) => _context = context;

    /// <summary>The underlying context.</summary>
    public RespContext Context => _context;
}

public static partial class RespDatabaseExtensions
{
    extension(in RespDatabaseContext context)
    {
        /// <summary>The bitmap commands.</summary>
        public RespBitmaps Bitmaps => new(context.Raw);
    }

    extension<TTarget>(TTarget target) where TTarget : IRespKeyspaceTarget
    {
        /// <summary>The bitmap commands.</summary>
        public RespBitmaps Bitmaps => target.Context.Bitmaps;
    }
}
