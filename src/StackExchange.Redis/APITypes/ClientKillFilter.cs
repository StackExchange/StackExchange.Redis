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
    public ClientKillFilter() { }

    /// <summary>
    /// The ID of the client to kill.
    /// </summary>
    public long? Id
    {
        get; private set;
    }

    /// <summary>
    /// The type of client.
    /// </summary>
    public ClientType? ClientType
    {
        get; private set;
    }

    /// <summary>
    /// The authenticated ACL username, 
    /// </summary>
    /// <value></value>
    public string? Username
    {
        get; private set;
    }

    /// <summary>
    /// The endpoint to kill.
    /// </summary>
    public EndPoint? Endpoint
    {
        get; private set;
    }

    /// <summary>
    /// The server endpoint to kill.
    /// </summary>
    public EndPoint? ServerEndpoint
    {
        get; private set;
    }

    /// <summary>
    /// Whether to skip the current connection.
    /// </summary>
    public bool? SkipMe
    {
        get; private set;
    }

    /// <summary>
    /// Age of connection in seconds
    /// </summary>
    /// <value></value>
    public long? MaxAgeInSeconds
    {
        get; private set;
    }

    /// <summary>
    /// Set id filter
    /// </summary>
    /// <param name="id">Id of the client</param>
    /// <returns></returns>
    public ClientKillFilter WithId(long id)
    {
        Id = id; return this;
    }

    /// <summary>
    /// Set client type
    /// </summary>
    /// <param name="clientType">The type of the client</param>
    /// <returns></returns>
    public ClientKillFilter WithClientType(ClientType clientType)
    {
        ClientType = clientType; return this;
    }

    /// <summary>
    /// Set username
    /// </summary>
    /// <param name="username">Authenticated ACL username</param>
    /// <returns></returns>
    public ClientKillFilter WithUsername(string username)
    {
        Username = username; return this;
    }

    /// <summary>
    /// Set endpoint
    /// </summary>
    /// <param name="endpoint">The endpoint to kill.</param>
    /// <returns></returns>
    public ClientKillFilter WithEndpoint(EndPoint endpoint)
    {
        Endpoint = endpoint; return this;
    }

    /// <summary>
    /// Set server endpoint
    /// </summary>
    /// <param name="serverEndpoint">The server endpoint to kill.</param>
    /// <returns></returns>
    public ClientKillFilter WithServerEndpoint(EndPoint serverEndpoint)
    {
        ServerEndpoint = serverEndpoint; return this;
    }

    /// <summary>
    /// Set skipMe
    /// </summary>
    /// <param name="skipMe">Whether to skip the current connection.</param>
    /// <returns></returns>
    public ClientKillFilter WithSkipMe(bool skipMe)
    {
        SkipMe = skipMe; return this;
    }

    /// <summary>
    /// Set MaxAgeInSeconds
    /// </summary>
    /// <param name="maxAgeInSeconds">Age of connection in seconds</param>
    /// <returns></returns>
    public ClientKillFilter WithMaxAgeInSeconds(long maxAgeInSeconds)
    {
        MaxAgeInSeconds = maxAgeInSeconds; return this;
    }
}
