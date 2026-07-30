using System;
using System.Threading;

namespace StackExchange.Redis.Interfaces;

[Flags]
internal enum DatabaseFeatureFlags
{
    None = 0,
    Cluster = 1 << 0,
    ConnectionGroup = 1 << 1,
    Batch = 1 << 2,
    Transaction = 1 << 3,
    KeyPrefix = 1 << 4,
    Retry = 1 << 5,
    Unknown = 1 << 6,
    Failover = 1 << 7,
}

internal interface IInternalDatabaseAsync : IDatabaseAsync
{
    DatabaseFeatureFlags GetFeatures(out string name);
    CancellationToken GetNextFailover();

    /// <summary>
    /// The async-state that this database stamps onto the tasks it hands out, if any; wrappers that
    /// cannot preserve it (see <c>RetryDatabase</c>) need to know when one is present rather than
    /// dropping it silently.
    /// </summary>
    object? AsyncState { get; }
}

/// <summary>
/// Exposes transaction-level detail needed by the retry machinery. Implemented by the concrete
/// transaction types so a <c>RetryTransaction</c> can inspect the transaction it is replaying against.
/// </summary>
internal interface IInternalTransaction
{
    /// <summary>
    /// The most side-effecting retry category across all queued operations (excluding <c>WATCH</c>
    /// constraints); this describes what replaying the whole transaction would do.
    /// </summary>
    CommandFlags GetAggregateRetryCategory();
}

internal static class InternalDatabaseExtension
{
    internal static DatabaseFeatureFlags GetFeatures(this IDatabaseAsync database, out string name)
    {
        if (database is IInternalDatabaseAsync idb)
        {
            return idb.GetFeatures(out name);
        }

        name = "";
        return DatabaseFeatureFlags.Unknown;
    }

    internal static string BuildString(this IDatabaseAsync database)
    {
        var features = database.GetFeatures(out string name);
        return string.IsNullOrEmpty(name) ? features.ToString() : $"{name}: {features}";
    }

    internal static DatabaseFeatureFlags RejectFlags(this IDatabaseAsync database, DatabaseFeatureFlags incompatible)
    {
        // note: returns *all* the features of the database provided
        var features = database.GetFeatures(out _);
        var overlap = features & incompatible;
        if (overlap is not 0) Throw(overlap);
        return features;

        static void Throw(DatabaseFeatureFlags overlap) => throw new InvalidOperationException(
            $"This operation is not compatible with feature(s): {overlap}");
    }

    internal static object? GetAsyncState(this IDatabaseAsync database)
        => database is IInternalDatabaseAsync ida ? ida.AsyncState : null;

    internal static CancellationToken GetNextFailover(this IDatabaseAsync database)
    {
        // get a CT that represents the next failover; you might be asking "shouldn't that be a Task getter?" - no,
        // because Task *does* have ContinueWith, but it doesn't have any mechanism to *undo* that; conversely,
        // CancellationToken is expressly designed with that intent, with Register(..) being scoped.
        return database is IInternalDatabaseAsync ida ? ida.GetNextFailover() : CancellationToken.None;
    }
}
