using System;
using System.Net;

namespace StackExchange.Redis;

/// <summary>
/// 
/// </summary>
public class ClientKillFilter
{
    /// <summary>
    /// Filter arguments for ClientKill
    /// </summary>    
    /// <param name="id">The ID of the client to kill.</param>
    /// <param name="clientType">The type of client.</param>
    /// <param name="endpoint">The endpoint to kill.</param>
    /// <param name="skipMe">Whether to skip the current connection.</param>
    /// <param name="maxAgeInSeconds">Max age of connection in seconds, close older ones.</param>
    public ClientKillFilter(long? id, ClientType? clientType, EndPoint? endpoint, bool skipMe, int? maxAgeInSeconds)
    {
        Id = id;
        ClientType = clientType;
        Endpoint = endpoint;
        SkipMe = skipMe;
        MaxAgeInSeconds = maxAgeInSeconds;
    }

    /// <summary>
    /// Filter arguments for ClientKill
    /// </summary>    
    /// <param name="id">The ID of the client to kill.</param>
    /// <param name="clientType">The type of client.</param>
    /// <param name="endpoint">The endpoint to kill.</param>
    /// <param name="maxAgeInSeconds">Max age of connection in seconds, close older ones.</param>
    public ClientKillFilter(long? id, ClientType? clientType, EndPoint? endpoint, int? maxAgeInSeconds)
    {
        Id = id;
        ClientType = clientType;
        Endpoint = endpoint;
        MaxAgeInSeconds = maxAgeInSeconds;
    }

    /// <summary>
    /// Filter arguments for ClientKill
    /// </summary>
    /// <param name="id">The ID of the client to kill.</param>
    /// <param name="clientType">The type of client.</param>
    /// <param name="maxAgeInSeconds">Max age of connection in seconds, close older ones.</param>
    public ClientKillFilter(long? id, ClientType? clientType, int? maxAgeInSeconds) { Id = id; ClientType = clientType; MaxAgeInSeconds = maxAgeInSeconds; }

    /// <summary>
    /// Filter arguments for ClientKill
    /// </summary>
    /// <param name="id">The ID of the client to kill.</param>
    /// <param name="maxAgeInSeconds">Max age of connection in seconds, close older ones.</param>
    public ClientKillFilter(long? id, int? maxAgeInSeconds) { Id = id; MaxAgeInSeconds = maxAgeInSeconds; }

    /// <summary>
    /// Filter arguments for ClientKill
    /// </summary>
    /// <param name="clientType">The type of client.</param>
    /// <param name="maxAgeInSeconds">Max age of connection in seconds, close older ones.</param>
    public ClientKillFilter(ClientType? clientType, int? maxAgeInSeconds) { ClientType = clientType; MaxAgeInSeconds = maxAgeInSeconds; }

    /// <summary>
    /// Filter arguments for ClientKill
    /// </summary>
    /// <param name="endpoint">The endpoint to kill.</param>
    /// <param name="maxAgeInSeconds">Max age of connection in seconds, close older ones.</param>
    public ClientKillFilter(EndPoint? endpoint, int? maxAgeInSeconds) { Endpoint = endpoint; MaxAgeInSeconds = maxAgeInSeconds; }

    /// <summary>
    /// Filter arguments for ClientKill
    /// </summary>
    /// <param name="skipMe">Whether to skip the current connection.</param>
    /// <param name="maxAgeInSeconds">Max age of connection in seconds, close older ones.</param>
    public ClientKillFilter(bool skipMe, int? maxAgeInSeconds) { SkipMe = skipMe; MaxAgeInSeconds = maxAgeInSeconds; }

    /// <summary>
    /// Filter arguments for ClientKill
    /// </summary>
    public ClientKillFilter() { }

    /// <summary>
    /// The ID of the client to kill.
    /// </summary>
    public long? Id { get; set; }

    /// 
    /// <summary>
    /// The type of client.
    /// </summary>
    public ClientType? ClientType { get; set; }

    /// <summary>
    /// The endpoint to kill.
    /// </summary>
    public EndPoint? Endpoint { get; set; }

    /// <summary>
    /// Whether to skip the current connection.
    /// </summary>
    public bool SkipMe { get; set; }

    /// <summary>
    /// Max age of connection in seconds, close older ones.
    /// </summary>
    public long? MaxAgeInSeconds { get; set; }

}

