using System.Diagnostics.CodeAnalysis;

namespace StackExchange.Redis;

/// <summary>
/// The <c>CONFIG</c> commands.
/// </summary>
/// <remarks>
/// <inheritdoc cref="HyperLogLog" path="/remarks/para[1]"/>
/// </remarks>
[SuppressMessage("ApiDesign", "RS0026:Do not add multiple overloads with optional parameters", Justification = "Extension members on one group type, differing in a non-defaulted parameter; see HyperLogLog")]
public static partial class Config
{
}

/// <summary>
/// The configuration group: <c>server.Config.RewriteAsync()</c>.
/// </summary>
/// <remarks>
/// <para>
/// A node's configuration is its own, so like <see cref="RespDiagnostics"/> this hangs off
/// <see cref="RespServerContext"/> and has no database-scoped twin.
/// </para>
/// <para>
/// <b>Named for the command, not for the concept</b>: <c>Configuration</c> is already a namespace in
/// this assembly - it holds <c>Tunnel</c> and the options providers - and a type of the same name would
/// shadow it for every file that opened both.
/// </para>
/// </remarks>
public readonly struct RespConfig
{
    /// <summary>Group the configuration commands of a context.</summary>
    /// <param name="context">The context to send through.</param>
    public RespConfig(RespContext context) => Context = context;

    /// <summary>The context these commands are sent through.</summary>
    /// <remarks><inheritdoc cref="RespHyperLogLog.Context" path="/remarks"/></remarks>
    internal readonly RespContext Context;
}

public static partial class RespServerExtensions
{
    extension(in RespServerContext context)
    {
        /// <summary>The <c>CONFIG</c> commands of this server.</summary>
        public RespConfig Config => new(context.Raw);
    }
}
