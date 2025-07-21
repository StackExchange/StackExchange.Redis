using System;
using System.Net;
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
}
