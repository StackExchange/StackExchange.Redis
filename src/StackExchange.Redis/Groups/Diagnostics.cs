using System.Diagnostics.CodeAnalysis;

namespace StackExchange.Redis;

/// <summary>
/// The commands that ask a server about itself.
/// </summary>
/// <remarks>
/// <inheritdoc cref="HyperLogLog" path="/remarks/para[1]"/>
/// </remarks>
[SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Extension members on one group type, differing in a non-defaulted parameter; see HyperLogLog")]
public static partial class Diagnostics
{
}

/// <summary>
/// The diagnostic group: <c>server.Diagnostics.LatencyDoctorAsync()</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Server-scoped, and only server-scoped.</b> Every command here answers about the node that was
/// asked - its latency history, its allocator, its slow log - so there is no database-scoped twin, and
/// the group hangs off <see cref="RespServerContext"/> alone.
/// </para>
/// <para>
/// <b>The grouping is by subject, not by command name</b>: <c>LATENCY</c>, <c>MEMORY</c> and
/// <c>SLOWLOG</c> are three commands asking the same kind of question, and a caller looking for "what is
/// this server doing" should not have to know which of the three it lives under.
/// </para>
/// </remarks>
public readonly struct RespDiagnostics
{
    /// <summary>Group the diagnostic commands of a context.</summary>
    /// <param name="context">The context to send through.</param>
    public RespDiagnostics(RespContext context) => Context = context;

    /// <summary>The context these commands are sent through.</summary>
    /// <remarks><inheritdoc cref="RespHyperLogLog.Context" path="/remarks"/></remarks>
    internal readonly RespContext Context;
}

public static partial class RespServerExtensions
{
    extension(in RespServerContext context)
    {
        /// <summary>The commands that ask this server about itself.</summary>
        public RespDiagnostics Diagnostics => new(context.Raw);
    }
}
