using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using RESPite;
using RESPite.Messages;
using StackExchange.Redis.Maintenance;

namespace StackExchange.Redis;

internal sealed partial class PhysicalConnection
{
    /// <summary>
    /// Reads a maintenance notification and raises it as an event; observation only.
    /// </summary>
    /// <remarks>
    /// Parsed leniently on purpose. These frames are dispatched here *before* anything reads element 1 as a
    /// channel name, because element 1 is a sequence number - so a malformed one must be swallowed here, and
    /// never fall through to the command matcher, where it would take a reply belonging to something else.
    /// The shapes are (per the contract, all elements after the type being scalars):
    /// <list type="bullet">
    /// <item><c>MOVING seq time endpoint</c></item>
    /// <item><c>MIGRATING seq time shards</c>, <c>FAILING_OVER seq time shards</c></item>
    /// <item><c>MIGRATED seq shards</c>, <c>FAILED_OVER seq shards</c></item>
    /// <item><c>SMIGRATING seq slots</c>, <c>SMIGRATED seq slots</c></item>
    /// </list>
    /// Integers may arrive as <c>:</c> or as a bulk string, trailing elements are explicitly allowed, and the
    /// endpoint may be an explicit null. So the type decides *whether* a time is expected rather than the
    /// content deciding it - a slot list of "123" must not be mistaken for a duration - but a notification
    /// that omits or adds one is still accepted.
    /// </remarks>
    private OutOfBandResult OnMaintenanceNotification(ConnectionMultiplexer muxer, PushKind kind, ref RespReader reader)
    {
        _readStatus = ReadStatus.MaintenanceNotification;

        // the four facts this parse needed `this` for, so that the OTHER core's dispatcher can supply its
        // own and the rules stay in one copy - see ReadMaintenanceNotification
        MaintenanceNotificationReader.ReadMaintenanceNotification(
            muxer,
            BridgeCouldBeNull?.ServerEndPoint,
            BridgeCouldBeNull?.IsConnected == true,
            (VolatileSocket?.RemoteEndPoint as IPEndPoint)?.Address,
            kind,
            ref reader);

        // always: a maintenance notification is not a reply to anything, so the frame is consumed
        // whatever the parse made of it - which is why the shared routine returns nothing to decide
        return OutOfBandResult.Handled;
    }
}
