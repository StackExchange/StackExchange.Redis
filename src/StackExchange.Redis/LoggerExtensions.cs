using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace StackExchange.Redis;

internal static partial class LoggerExtensions
{
    // Helper structs for complex ToString() calls
    internal readonly struct EndPointLogValue(EndPoint? endpoint)
    {
        public override string ToString() => Format.ToString(endpoint);
    }

    internal readonly struct ServerEndPointLogValue(ServerEndPoint server)
    {
        public override string ToString() => Format.ToString(server);
    }

    internal readonly struct ConfigurationOptionsLogValue(ConfigurationOptions options)
    {
        public override string ToString() => options.ToString(includePassword: false);
    }

    // manual extensions
    internal static void LogWithThreadPoolStats(this ILogger? log, string message)
    {
        if (log is null || !log.IsEnabled(LogLevel.Information))
        {
            return;
        }

        _ = PerfCounterHelper.GetThreadPoolStats(out string iocp, out string worker, out string? workItems);

#if NET6_0_OR_GREATER
        // use DISH when possible
        // similar to: var composed = $"{message}, IOCP: {iocp}, WORKER: {worker}, ..."; on net6+
        var dish = new System.Runtime.CompilerServices.DefaultInterpolatedStringHandler(26, 4);
        dish.AppendFormatted(message);
        dish.AppendLiteral(", IOCP: ");
        dish.AppendFormatted(iocp);
        dish.AppendLiteral(", WORKER: ");
        dish.AppendFormatted(worker);
        if (workItems is not null)
        {
            dish.AppendLiteral(", POOL: ");
            dish.AppendFormatted(workItems);
        }
        var composed = dish.ToStringAndClear();
#else
        var sb = new StringBuilder();
        sb.Append(message).Append(", IOCP: ").Append(iocp).Append(", WORKER: ").Append(worker);
        if (workItems is not null)
        {
            sb.Append(", POOL: ").Append(workItems);
        }
        var composed = sb.ToString();
#endif
        log.LogInformationThreadPoolStats(composed);
    }

    // Generated LoggerMessage methods
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Connection failed: {EndPoint} ({ConnectionType}, {FailureType}): {ErrorMessage}")]
    internal static partial void LogErrorConnectionFailed(this ILogger logger, Exception? exception, EndPointLogValue endPoint, ConnectionType connectionType, ConnectionFailureType failureType, string errorMessage);

    [LoggerMessage(
        Level = LogLevel.Error,
        EventId = 1,
        Message = "> {Message}")]
    internal static partial void LogErrorInnerException(this ILogger logger, Exception exception, string message);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 2,
        Message = "Checking {EndPoint} is available...")]
    internal static partial void LogInformationCheckingServerAvailable(this ILogger logger, EndPointLogValue endPoint);

    [LoggerMessage(
        Level = LogLevel.Error,
        EventId = 3,
        Message = "Operation failed on {EndPoint}, aborting: {ErrorMessage}")]
    internal static partial void LogErrorOperationFailedOnServer(this ILogger logger, Exception exception, EndPointLogValue endPoint, string errorMessage);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 4,
        Message = "Attempting to set tie-breaker on {EndPoint}...")]
    internal static partial void LogInformationAttemptingToSetTieBreaker(this ILogger logger, EndPointLogValue endPoint);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 5,
        Message = "Making {EndPoint} a primary...")]
    internal static partial void LogInformationMakingServerPrimary(this ILogger logger, EndPointLogValue endPoint);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 6,
        Message = "Resending tie-breaker to {EndPoint}...")]
    internal static partial void LogInformationResendingTieBreaker(this ILogger logger, EndPointLogValue endPoint);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 7,
        Message = "Broadcasting via {EndPoint}...")]
    internal static partial void LogInformationBroadcastingViaNode(this ILogger logger, EndPointLogValue endPoint);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 8,
        Message = "Replicating to {EndPoint}...")]
    internal static partial void LogInformationReplicatingToNode(this ILogger logger, EndPointLogValue endPoint);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 9,
        Message = "Reconfiguring all endpoints...")]
    internal static partial void LogInformationReconfiguringAllEndpoints(this ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 10,
        Message = "Verifying the configuration was incomplete; please verify")]
    internal static partial void LogInformationVerifyingConfigurationIncomplete(this ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 11,
        Message = "Connecting (async) on {Framework} (StackExchange.Redis: v{Version})")]
    internal static partial void LogInformationConnectingAsync(this ILogger logger, string framework, string version);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 12,
        Message = "Connecting (sync) on {Framework} (StackExchange.Redis: v{Version})")]
    internal static partial void LogInformationConnectingSync(this ILogger logger, string framework, string version);

    [LoggerMessage(
        Level = LogLevel.Error,
        EventId = 13,
        Message = "{ErrorMessage}")]
    internal static partial void LogErrorSyncConnectTimeout(this ILogger logger, Exception exception, string errorMessage);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 14,
        Message = "{Message}")]
    internal static partial void LogInformationAfterConnect(this ILogger logger, string message);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 15,
        Message = "Total connect time: {ElapsedMs:n0} ms")]
    internal static partial void LogInformationTotalConnectTime(this ILogger logger, long elapsedMs);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 16,
        Message = "No tasks to await")]
    internal static partial void LogInformationNoTasksToAwait(this ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 17,
        Message = "All tasks are already complete")]
    internal static partial void LogInformationAllTasksComplete(this ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 18,
        Message = "{Message}",
        SkipEnabledCheck = true)]
    internal static partial void LogInformationThreadPoolStats(this ILogger logger, string message);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 19,
        Message = "Reconfiguration was already in progress due to: {ActiveCause}, attempted to run for: {NewCause}")]
    internal static partial void LogInformationReconfigurationInProgress(this ILogger logger, string? activeCause, string newCause);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 20,
        Message = "{Configuration}")]
    internal static partial void LogInformationConfiguration(this ILogger logger, ConfigurationOptionsLogValue configuration);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 21,
        Message = "{Count} unique nodes specified ({TieBreakerStatus} tiebreaker)")]
    internal static partial void LogInformationUniqueNodesSpecified(this ILogger logger, int count, string tieBreakerStatus);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 22,
        Message = "Allowing {Count} endpoint(s) {TimeSpan} to respond...")]
    internal static partial void LogInformationAllowingEndpointsToRespond(this ILogger logger, int count, TimeSpan timeSpan);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 23,
        Message = "  Server[{Index}] ({Server}) Status: {Status} (inst: {MessagesSinceLastHeartbeat}, qs: {MessagesSentAwaitingResponse}, in: {BytesAvailableOnSocket}, qu: {MessagesSinceLastHeartbeat2}, aw: {IsWriterActive}, in-pipe: {BytesInReadPipe}, out-pipe: {BytesInWritePipe}, bw: {BacklogStatus}, rs: {ReadStatus}. ws: {WriteStatus})")]
    internal static partial void LogInformationServerStatus(this ILogger logger, int index, ServerEndPointLogValue server, TaskStatus status, long messagesSinceLastHeartbeat, long messagesSentAwaitingResponse, long bytesAvailableOnSocket, long messagesSinceLastHeartbeat2, bool isWriterActive, long bytesInReadPipe, long bytesInWritePipe, PhysicalBridge.BacklogStatus backlogStatus, PhysicalConnection.ReadStatus readStatus, PhysicalConnection.WriteStatus writeStatus);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 24,
        Message = "Endpoint summary:")]
    internal static partial void LogInformationEndpointSummary(this ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 25,
        Message = "  {EndPoint}: Endpoint is (Interactive: {InteractiveState}, Subscription: {SubscriptionState})")]
    internal static partial void LogInformationEndpointState(this ILogger logger, EndPointLogValue endPoint, PhysicalBridge.State interactiveState, PhysicalBridge.State subscriptionState);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 26,
        Message = "Task summary:")]
    internal static partial void LogInformationTaskSummary(this ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        EventId = 27,
        Message = "  {Server}: Faulted: {ErrorMessage}")]
    internal static partial void LogErrorServerFaulted(this ILogger logger, Exception exception, ServerEndPointLogValue server, string errorMessage);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 28,
        Message = "  {Server}: Connect task canceled")]
    internal static partial void LogInformationConnectTaskCanceled(this ILogger logger, ServerEndPointLogValue server);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 29,
        Message = "  {Server}: Returned with success as {ServerType} {Role} (Source: {Source})")]
    internal static partial void LogInformationServerReturnedSuccess(this ILogger logger, ServerEndPointLogValue server, ServerType serverType, string role, string source);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 30,
        Message = "  {Server}: Returned, but incorrectly")]
    internal static partial void LogInformationServerReturnedIncorrectly(this ILogger logger, ServerEndPointLogValue server);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 31,
        Message = "  {Server}: Did not respond (Task.Status: {TaskStatus})")]
    internal static partial void LogInformationServerDidNotRespond(this ILogger logger, ServerEndPointLogValue server, TaskStatus taskStatus);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 32,
        Message = "{EndPoint}: Clearing as RedundantPrimary")]
    internal static partial void LogInformationClearingAsRedundantPrimary(this ILogger logger, ServerEndPointLogValue endPoint);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 33,
        Message = "{EndPoint}: Setting as RedundantPrimary")]
    internal static partial void LogInformationSettingAsRedundantPrimary(this ILogger logger, ServerEndPointLogValue endPoint);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 34,
        Message = "Cluster: {CoveredSlots} of {TotalSlots} slots covered")]
    internal static partial void LogInformationClusterSlotsCovered(this ILogger logger, long coveredSlots, int totalSlots);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 35,
        Message = "No subscription changes necessary")]
    internal static partial void LogInformationNoSubscriptionChanges(this ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 36,
        Message = "Subscriptions attempting reconnect: {SubscriptionChanges}")]
    internal static partial void LogInformationSubscriptionsAttemptingReconnect(this ILogger logger, long subscriptionChanges);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 37,
        Message = "{StormLog}")]
    internal static partial void LogInformationStormLog(this ILogger logger, string stormLog);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 38,
        Message = "Resetting failing connections to retry...")]
    internal static partial void LogInformationResettingFailingConnections(this ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 39,
        Message = "  Retrying - attempts left: {AttemptsLeft}...")]
    internal static partial void LogInformationRetryingAttempts(this ILogger logger, int attemptsLeft);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 40,
        Message = "Starting heartbeat...")]
    internal static partial void LogInformationStartingHeartbeat(this ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 41,
        Message = "Broadcasting reconfigure...")]
    internal static partial void LogInformationBroadcastingReconfigure(this ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        EventId = 42,
        Message = "Encountered error while updating cluster config: {ErrorMessage}")]
    internal static partial void LogErrorEncounteredErrorWhileUpdatingClusterConfig(this ILogger logger, Exception exception, string errorMessage);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 43,
        Message = "Election summary:")]
    internal static partial void LogInformationElectionSummary(this ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 44,
        Message = "  Election: {Server} had no tiebreaker set")]
    internal static partial void LogInformationElectionNoTiebreaker(this ILogger logger, ServerEndPointLogValue server);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 45,
        Message = "  Election: {Server} nominates: {ServerResult}")]
    internal static partial void LogInformationElectionNominates(this ILogger logger, ServerEndPointLogValue server, string serverResult);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 46,
        Message = "  Election: No primaries detected")]
    internal static partial void LogInformationElectionNoPrimariesDetected(this ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 47,
        Message = "  Election: Single primary detected: {EndPoint}")]
    internal static partial void LogInformationElectionSinglePrimaryDetected(this ILogger logger, EndPointLogValue endPoint);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 48,
        Message = "  Election: Multiple primaries detected...")]
    internal static partial void LogInformationElectionMultiplePrimariesDetected(this ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 49,
        Message = "  Election: No nominations by tie-breaker")]
    internal static partial void LogInformationElectionNoNominationsByTieBreaker(this ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 50,
        Message = "  Election: Tie-breaker unanimous: {Unanimous}")]
    internal static partial void LogInformationElectionTieBreakerUnanimous(this ILogger logger, string unanimous);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 51,
        Message = "  Election: Elected: {EndPoint}")]
    internal static partial void LogInformationElectionElected(this ILogger logger, EndPointLogValue endPoint);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 52,
        Message = "  Election is contested:")]
    internal static partial void LogInformationElectionContested(this ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 53,
        Message = "    Election: {Key} has {Value} votes")]
    internal static partial void LogInformationElectionVotes(this ILogger logger, string key, int value);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 54,
        Message = "  Election: Choosing primary arbitrarily: {EndPoint}")]
    internal static partial void LogInformationElectionChoosingPrimaryArbitrarily(this ILogger logger, EndPointLogValue endPoint);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 55,
        Message = "...but we couldn't find that")]
    internal static partial void LogInformationCouldNotFindThatEndpoint(this ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 56,
        Message = "...but we did find instead: {DeDottedEndpoint}")]
    internal static partial void LogInformationFoundAlternativeEndpoint(this ILogger logger, string deDottedEndpoint);

    // ServerEndPoint logging methods
    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 57,
        Message = "{Server}: OnConnectedAsync already connected start")]
    internal static partial void LogInformationOnConnectedAsyncAlreadyConnectedStart(this ILogger logger, ServerEndPointLogValue server);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 58,
        Message = "{Server}: OnConnectedAsync already connected end")]
    internal static partial void LogInformationOnConnectedAsyncAlreadyConnectedEnd(this ILogger logger, ServerEndPointLogValue server);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 59,
        Message = "{Server}: OnConnectedAsync init (State={ConnectionState})")]
    internal static partial void LogInformationOnConnectedAsyncInit(this ILogger logger, ServerEndPointLogValue server, PhysicalBridge.State? connectionState);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 60,
        Message = "{Server}: OnConnectedAsync completed ({Result})")]
    internal static partial void LogInformationOnConnectedAsyncCompleted(this ILogger logger, ServerEndPointLogValue server, string result);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 61,
        Message = "{Server}: Auto-configuring...")]
    internal static partial void LogInformationAutoConfiguring(this ILogger logger, ServerEndPointLogValue server);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 62,
        Message = "{EndPoint}: Requesting tie-break (Key=\"{TieBreakerKey}\")...")]
    internal static partial void LogInformationRequestingTieBreak(this ILogger logger, EndPointLogValue endPoint, RedisKey tieBreakerKey);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 63,
        Message = "{Server}: Server handshake")]
    internal static partial void LogInformationServerHandshake(this ILogger logger, ServerEndPointLogValue server);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 64,
        Message = "{Server}: Authenticating via HELLO")]
    internal static partial void LogInformationAuthenticatingViaHello(this ILogger logger, ServerEndPointLogValue server);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 65,
        Message = "{Server}: Authenticating (user/password)")]
    internal static partial void LogInformationAuthenticatingUserPassword(this ILogger logger, ServerEndPointLogValue server);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 66,
        Message = "{Server}: Authenticating (password)")]
    internal static partial void LogInformationAuthenticatingPassword(this ILogger logger, ServerEndPointLogValue server);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 67,
        Message = "{Server}: Setting client name: {ClientName}")]
    internal static partial void LogInformationSettingClientName(this ILogger logger, ServerEndPointLogValue server, string clientName);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 68,
        Message = "{Server}: Setting client lib/ver")]
    internal static partial void LogInformationSettingClientLibVer(this ILogger logger, ServerEndPointLogValue server);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 69,
        Message = "{Server}: Sending critical tracer (handshake): {CommandAndKey}")]
    internal static partial void LogInformationSendingCriticalTracer(this ILogger logger, ServerEndPointLogValue server, string commandAndKey);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 70,
        Message = "{Server}: Flushing outbound buffer")]
    internal static partial void LogInformationFlushingOutboundBuffer(this ILogger logger, ServerEndPointLogValue server);

    // ResultProcessor logging methods
    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 71,
        Message = "Response from {BridgeName} / {CommandAndKey}: {Result}")]
    internal static partial void LogInformationResponse(this ILogger logger, string? bridgeName, string commandAndKey, RawResult result);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 72,
        Message = "{Server}: Auto-configured role: replica")]
    internal static partial void LogInformationAutoConfiguredRoleReplica(this ILogger logger, ServerEndPointLogValue server);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 73,
        Message = "{Server}: Auto-configured (CLIENT) connection-id: {ConnectionId}")]
    internal static partial void LogInformationAutoConfiguredClientConnectionId(this ILogger logger, ServerEndPointLogValue server, long connectionId);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 74,
        Message = "{Server}: Auto-configured (INFO) role: {Role}")]
    internal static partial void LogInformationAutoConfiguredInfoRole(this ILogger logger, ServerEndPointLogValue server, string role);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 75,
        Message = "{Server}: Auto-configured (INFO) version: {Version}")]
    internal static partial void LogInformationAutoConfiguredInfoVersion(this ILogger logger, ServerEndPointLogValue server, Version version);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 76,
        Message = "{Server}: Auto-configured (INFO) server-type: {ServerType}")]
    internal static partial void LogInformationAutoConfiguredInfoServerType(this ILogger logger, ServerEndPointLogValue server, ServerType serverType);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 77,
        Message = "{Server}: Auto-configured (SENTINEL) server-type: sentinel")]
    internal static partial void LogInformationAutoConfiguredSentinelServerType(this ILogger logger, ServerEndPointLogValue server);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 78,
        Message = "{Server}: Auto-configured (CONFIG) timeout: {TimeoutSeconds}s")]
    internal static partial void LogInformationAutoConfiguredConfigTimeout(this ILogger logger, ServerEndPointLogValue server, int timeoutSeconds);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 79,
        Message = "{Server}: Auto-configured (CONFIG) databases: {DatabaseCount}")]
    internal static partial void LogInformationAutoConfiguredConfigDatabases(this ILogger logger, ServerEndPointLogValue server, int databaseCount);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 81,
        Message = "{Server}: Auto-configured (CONFIG) read-only replica: {ReadOnlyReplica}")]
    internal static partial void LogInformationAutoConfiguredConfigReadOnlyReplica(this ILogger logger, ServerEndPointLogValue server, bool readOnlyReplica);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 82,
        Message = "{Server}: Auto-configured (HELLO) server-version: {Version}")]
    internal static partial void LogInformationAutoConfiguredHelloServerVersion(this ILogger logger, ServerEndPointLogValue server, Version version);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 83,
        Message = "{Server}: Auto-configured (HELLO) protocol: {Protocol}")]
    internal static partial void LogInformationAutoConfiguredHelloProtocol(this ILogger logger, ServerEndPointLogValue server, RedisProtocol protocol);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 84,
        Message = "{Server}: Auto-configured (HELLO) connection-id: {ConnectionId}")]
    internal static partial void LogInformationAutoConfiguredHelloConnectionId(this ILogger logger, ServerEndPointLogValue server, long connectionId);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 85,
        Message = "{Server}: Auto-configured (HELLO) server-type: {ServerType}")]
    internal static partial void LogInformationAutoConfiguredHelloServerType(this ILogger logger, ServerEndPointLogValue server, ServerType serverType);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 86,
        Message = "{Server}: Auto-configured (HELLO) role: {Role}")]
    internal static partial void LogInformationAutoConfiguredHelloRole(this ILogger logger, ServerEndPointLogValue server, string role);

    // PhysicalBridge logging methods
    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 87,
        Message = "{EndPoint}: OnEstablishingAsync complete")]
    internal static partial void LogInformationOnEstablishingComplete(this ILogger logger, EndPointLogValue endPoint);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 88,
        Message = "{ErrorMessage}")]
    internal static partial void LogInformationConnectionFailureRequested(this ILogger logger, Exception exception, string errorMessage);

    [LoggerMessage(
        Level = LogLevel.Error,
        EventId = 89,
        Message = "{ErrorMessage}")]
    internal static partial void LogErrorConnectionIssue(this ILogger logger, Exception exception, string errorMessage);

    [LoggerMessage(
        Level = LogLevel.Warning,
        EventId = 90,
        Message = "Dead socket detected, no reads in {LastReadSecondsAgo} seconds with {TimeoutCount} timeouts, issuing disconnect")]
    internal static partial void LogWarningDeadSocketDetected(this ILogger logger, long lastReadSecondsAgo, long timeoutCount);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 91,
        Message = "Resurrecting {Bridge} (retry: {RetryCount})")]
    internal static partial void LogInformationResurrecting(this ILogger logger, PhysicalBridge bridge, long retryCount);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 92,
        Message = "{BridgeName}: Connecting...")]
    internal static partial void LogInformationConnecting(this ILogger logger, string bridgeName);

    [LoggerMessage(
        Level = LogLevel.Error,
        EventId = 93,
        Message = "{BridgeName}: Connect failed: {ErrorMessage}")]
    internal static partial void LogErrorConnectFailed(this ILogger logger, Exception exception, string bridgeName, string errorMessage);

    // PhysicalConnection logging methods
    [LoggerMessage(
        Level = LogLevel.Error,
        EventId = 94,
        Message = "No endpoint")]
    internal static partial void LogErrorNoEndpoint(this ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 95,
        Message = "{EndPoint}: BeginConnectAsync")]
    internal static partial void LogInformationBeginConnectAsync(this ILogger logger, EndPointLogValue endPoint);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 96,
        Message = "{EndPoint}: Starting read")]
    internal static partial void LogInformationStartingRead(this ILogger logger, EndPointLogValue endPoint);

    [LoggerMessage(
        Level = LogLevel.Error,
        EventId = 97,
        Message = "{EndPoint}: (socket shutdown)")]
    internal static partial void LogErrorSocketShutdown(this ILogger logger, Exception exception, EndPointLogValue endPoint);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 98,
        Message = "Configuring TLS")]
    internal static partial void LogInformationConfiguringTLS(this ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 99,
        Message = "TLS connection established successfully using protocol: {SslProtocol}")]
    internal static partial void LogInformationTLSConnectionEstablished(this ILogger logger, System.Security.Authentication.SslProtocols sslProtocol);

    [LoggerMessage(
        Level = LogLevel.Information,
        EventId = 100,
        Message = "{BridgeName}: Connected")]
    internal static partial void LogInformationConnected(this ILogger logger, string bridgeName);
}
