using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis;

/// <summary>
/// EXPERIMENTAL SPIKE. The scripts commands.
/// </summary>
/// <remarks>
/// <para>
/// <b>Empty here, and that is the point.</b> A group is its own files sharing one name:
/// <c>Scripts.cs</c> holds the group type and the accessor that reaches it, <c>Scripts.Methods.cs</c>
/// the commands. Declaring the partial here is what makes the file named after the group the one to
/// open first.
/// </para>
/// <para>
/// <b>The accessor cannot live in this class</b> - a member named <c>Scripts</c> inside a class named
/// <c>Scripts</c> is <c>CS0542</c> - so it hangs off <see cref="RespDatabaseExtensions"/>, which every
/// group contributes its own accessor to.
/// </para>
/// </remarks>
// RS0026 warns about overloads carrying optional parameters, because adding one later can make an
// existing call ambiguous. That cannot arise here: every member of this class is an extension method on
// one group type, so two members sharing a name are always candidates for the same call - and within the
// group they differ in a parameter that has NO default. Per group, that claim is about a dozen methods
// with one receiver rather than about every command in the library at once.
[SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Extension members on one group type, differing in a non-defaulted parameter; see the comment above")]
public static partial class Scripts
{
}

/// <summary>
/// EXPERIMENTAL SPIKE. The scripting command group: <c>target.Scripts.EvaluateAsync(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Always <c>EVALSHA</c>, with <c>SCRIPT LOAD</c> in front of it when needed.</b> The alternative
/// considered was a frame that could re-spell itself as <c>EVAL &lt;body&gt;</c> after a
/// <c>NOSCRIPT</c>; this is better, because it keeps a frame a pure function of its arguments - the
/// property that lets frames be cached, routed and replayed - and moves the cleverness into
/// composition, where transactions and <c>HIMPORT</c> already live.
/// </para>
/// <para>
/// <b>The preamble is decided at write time, not here.</b> The pair is always built, and an
/// <see cref="IRespPreambleGate"/> decides when the connection is finally known whether the
/// <c>SCRIPT LOAD</c> half expands at all - because the endpoint whose script cache is in question is
/// not chosen until the write, and a resend after <c>NOSCRIPT</c>, a reconnect or a <c>MOVED</c> must
/// re-decide. That is also what makes the retry terminate.
/// </para>
/// <para>
/// The belief lives on the endpoint, shared with the classic path rather than duplicated: one table,
/// one <c>SCRIPT FLUSH</c>/restart invalidation, no second thing to keep honest. It is soft in both
/// directions - a wrong "loaded" costs a <c>NOSCRIPT</c> and a retry, a wrong "not loaded" costs an
/// idempotent <c>SCRIPT LOAD</c> - which is what lets this be an optimisation over something already
/// correct rather than a thing the correctness rests on.
/// </para>
/// </remarks>
public readonly struct RespScripts
{
    private readonly RespContext _context;

    /// <summary>Group the scripting commands of a context.</summary>
    /// <param name="context">The context to send through.</param>
    public RespScripts(in RespContext context) => _context = context;

    /// <summary>The underlying context.</summary>
    public RespContext Context => _context;
}

public static partial class RespDatabaseExtensions
{
    extension<TTarget>(TTarget target) where TTarget : IRespKeyspaceTarget
    {
        /// <summary>The scripting commands.</summary>
        public RespScripts Scripts => new(target.Context);
    }

    extension(in RespContext context)
    {
        /// <summary>The scripting commands.</summary>
        public RespScripts Scripts => new(context);
    }
}
