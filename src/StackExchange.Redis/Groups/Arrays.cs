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
/// EXPERIMENTAL SPIKE. The arrays commands.
/// </summary>
/// <remarks>
/// <para>
/// <b>Empty here, and that is the point.</b> A group is its own files sharing one name:
/// <c>Arrays.cs</c> holds the group type and the accessor that reaches it, <c>Arrays.Methods.cs</c>
/// the commands. Declaring the partial here is what makes the file named after the group the one to
/// open first.
/// </para>
/// <para>
/// <b>The accessor cannot live in this class</b> - a member named <c>Arrays</c> inside a class named
/// <c>Arrays</c> is <c>CS0542</c> - so it hangs off <see cref="RespDatabaseExtensions"/>, which every
/// group contributes its own accessor to.
/// </para>
/// </remarks>
// RS0026 warns about overloads carrying optional parameters, because adding one later can make an
// existing call ambiguous. That cannot arise here: every member of this class is an extension method on
// one group type, so two members sharing a name are always candidates for the same call - and within the
// group they differ in a parameter that has NO default. Per group, that claim is about a dozen methods
// with one receiver rather than about every command in the library at once.
[SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Extension members on one group type, differing in a non-defaulted parameter; see the comment above")]
public static partial class Arrays
{
}

/// <summary>
/// EXPERIMENTAL SPIKE. The array group: <c>target.Arrays.GetAsync(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// The family that needed no shape decision: <see cref="RedisArrayIndex"/> wraps a <c>ulong</c>,
/// <see cref="RedisArrayEntry"/> and <see cref="RedisArrayRange"/> are pairs of those, and
/// <see cref="ArrayInfo"/> is seven of them - nothing here is a composite holding an array, so the
/// whole group follows the rule directly: spans in, leases out.
/// </para>
/// <para>
/// The three element types render <i>themselves</i> (<see cref="IRespArgument"/>), which is what lets a
/// <c>ReadOnlySpan&lt;RedisArrayIndex&gt;</c> or a span of ranges go straight into a hole. No overload
/// per element type on the handler, and no boxing: a constrained call on a struct.
/// </para>
/// <para>
/// <c>ARGREP</c> is absent. <see cref="ArrayGrepRequest"/> is a mutable builder whose predicates render
/// themselves through the <i>old</i> <c>MessageWriter</c>, so moving it is a design decision about that
/// type rather than a transcription of a command; see the queue.
/// </para>
/// </remarks>
public readonly struct RespArrays
{
    private readonly RespContext _context;

    /// <summary>Group the array commands of a context.</summary>
    /// <param name="context">The context to send through.</param>
    public RespArrays(in RespContext context) => _context = context;

    /// <summary>The underlying context.</summary>
    public RespContext Context => _context;
}

public static partial class RespDatabaseExtensions
{
    extension(IRespKeyspaceTarget target)
    {
        /// <summary>The array commands.</summary>
        public RespArrays Arrays => new(target.Context);
    }

    extension(in RespContext context)
    {
        /// <summary>The array commands.</summary>
        public RespArrays Arrays => new(context);
    }
}
