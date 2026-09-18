using System.Diagnostics.CodeAnalysis;
using RESPite;

namespace StackExchange.Redis;

/// <summary>
/// Whether to ask a server to send maintenance notifications - advance warning of migrations, failovers and
/// endpoint moves.
/// </summary>
/// <remarks>
/// The three modes, and their names, are prescribed cross-client so that configuration and support tickets
/// line up between clients. Only Redis Enterprise and Redis Cloud emit these notifications; OSS Redis, Valkey
/// and Garnet do not recognize the opt-in at all, which is why <see cref="Auto"/> exists and why the default
/// is <see cref="Disabled"/> rather than asking every server in existence.
/// <para>
/// <b>Note that <see cref="Enabled"/> means "required", not "on": it rejects any connection that cannot
/// deliver notifications.</b> <see cref="Auto"/> is the mode that turns the feature on where available and
/// stays out of the way otherwise, and is what most callers want. The names are the cross-client ones, and
/// this is the one place they are easy to misread.
/// </para>
/// </remarks>
[Experimental(Experiments.MaintenanceNotifications, UrlFormat = Experiments.UrlFormat)]
public enum MaintenanceNotificationMode
{
    /// <summary>
    /// Never ask; no notification is requested and none will arrive.
    /// </summary>
    Disabled = 0,

    /// <summary>
    /// <b>Requires them, and REJECTS THE CONNECTION if they are unavailable</b> - including against any
    /// server that does not support them, and on any RESP2 connection. Only for a deployment known to
    /// support them; use <see cref="Auto"/> to ask without that risk.
    /// </summary>
    /// <remarks>
    /// A server that refuses fails the connection, and so does a server that answers <c>HELLO 3</c> as RESP2,
    /// since nothing can be delivered on a RESP2 connection. So does a configuration that could never ask in
    /// the first place - <c>Protocol = Resp2</c>, or <c>HELLO</c> unavailable - because requiring a
    /// RESP3-only feature over RESP2 is a contradiction, and honouring half of it silently is the outcome
    /// this mode exists to prevent.
    /// </remarks>
    Enabled,

    /// <summary>
    /// Ask, and carry on if the server refuses or the connection ends up RESP2 - the feature is then simply
    /// off for that server, and the connection is never rejected over it. Safe against a mixture of servers,
    /// or one whose support you don't know.
    /// </summary>
    Auto,
}
