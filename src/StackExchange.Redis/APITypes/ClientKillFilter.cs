using System;
using System.Net;
using StackExchange.Redis.Protocol;

namespace StackExchange.Redis;

/// <summary>
/// Filter determining which Redis clients to kill.
/// </summary>
/// <seealso href="https://redis.io/docs/latest/commands/client-kill/"/>
public class ClientKillFilter
{
    /// <summary>
    /// Filter arguments builder for `CLIENT KILL`.
    /// </summary>
    public ClientKillFilter() { }

    /// <summary>
    /// The ID of the client to kill.
    /// </summary>
    public long? Id { get; private set; }

    /// <summary>
    /// The type of client.
    /// </summary>
    public ClientType? ClientType { get; private set; }

    /// <summary>
    /// The authenticated ACL username.
    /// </summary>
    public string? Username { get; private set; }

    /// <summary>
    /// The endpoint to kill.
    /// </summary>
    public EndPoint? Endpoint { get; private set; }

    /// <summary>
    /// The server endpoint to kill.
    /// </summary>
    public EndPoint? ServerEndpoint { get; private set; }

    /// <summary>
    /// Whether to skip the current connection.
    /// </summary>
    public bool? SkipMe { get; private set; }

    /// <summary>
    /// Age of connection in seconds.
    /// </summary>
    public long? MaxAgeInSeconds { get; private set; }

    /// <summary>
    /// Sets client id filter.
    /// </summary>
    /// <param name="id">Id of the client to kill.</param>
    public ClientKillFilter WithId(long? id)
    {
        Id = id;
        return this;
    }

    /// <summary>
    /// Sets client type filter.
    /// </summary>
    /// <param name="clientType">The type of the client.</param>
    public ClientKillFilter WithClientType(ClientType? clientType)
    {
        ClientType = clientType;
        return this;
    }

    /// <summary>
    /// Sets the username filter.
    /// </summary>
    /// <param name="username">Authenticated ACL username.</param>
    public ClientKillFilter WithUsername(string? username)
    {
        Username = username;
        return this;
    }

    /// <summary>
    /// Set the endpoint filter.
    /// </summary>
    /// <param name="endpoint">The endpoint to kill.</param>
    public ClientKillFilter WithEndpoint(EndPoint? endpoint)
    {
        Endpoint = endpoint;
        return this;
    }

    /// <summary>
    /// Set the server endpoint filter.
    /// </summary>
    /// <param name="serverEndpoint">The server endpoint to kill.</param>
    public ClientKillFilter WithServerEndpoint(EndPoint? serverEndpoint)
    {
        ServerEndpoint = serverEndpoint;
        return this;
    }

    /// <summary>
    /// Set the skipMe filter (whether to skip the current connection).
    /// </summary>
    /// <param name="skipMe">Whether to skip the current connection.</param>
    public ClientKillFilter WithSkipMe(bool? skipMe)
    {
        SkipMe = skipMe;
        return this;
    }

    /// <summary>
    /// Set the MaxAgeInSeconds filter.
    /// </summary>
    /// <param name="maxAgeInSeconds">Age of connection in seconds.</param>
    public ClientKillFilter WithMaxAgeInSeconds(long? maxAgeInSeconds)
    {
        MaxAgeInSeconds = maxAgeInSeconds;
        return this;
    }

    /// <summary>
    /// The wire form of the filter: <c>KILL</c> plus one <c>NAME value</c> pair per field that was set,
    /// written straight into the frame as <c>$"{RedisCommand.CLIENT}{operand}"</c>.
    /// </summary>
    /// <param name="filter">The filter to render.</param>
    /// <param name="withReplicaCommands">Whether this server says <c>replica</c> rather than <c>slave</c>.</param>
    /// <remarks>
    /// The one encoding of <c>CLIENT KILL</c>: the loose-argument overload renders through a filter too,
    /// precisely so that a second copy cannot come to disagree with this one about - say - what a replica
    /// is called on this server.
    /// </remarks>
    internal readonly struct Operand(ClientKillFilter filter, bool withReplicaCommands) : IRespArgument
    {
        public void WriteTo(scoped ref RespRequestBuilder handler)
        {
            handler.AppendFormatted(RespLiterals.Kill);
            if (filter.Id is { } id)
            {
                handler.AppendFormatted(RespLiterals.Id);
                handler.AppendFormatted((RedisValue)id);
            }

            if (filter.ClientType is { } clientType)
            {
                handler.AppendFormatted(RespLiterals.Type);
                handler.AppendFormatted(clientType switch
                {
                    Redis.ClientType.Normal => RespLiterals.Normal,
                    Redis.ClientType.Replica => withReplicaCommands ? RespLiterals.Replica : RespLiterals.Slave,
                    Redis.ClientType.PubSub => RespLiterals.PubSub,
                    _ => throw new ArgumentOutOfRangeException(nameof(ClientType)),
                });
            }

            if (filter.Username is { } username)
            {
                handler.AppendFormatted(RespLiterals.Username);
                handler.AppendFormatted(username.AsRedisValue());
            }

            if (filter.Endpoint is { } endpoint)
            {
                handler.AppendFormatted(RespLiterals.Addr);
                handler.AppendFormatted(Format.ToString(endpoint).AsRedisValue());
            }

            if (filter.ServerEndpoint is { } serverEndpoint)
            {
                handler.AppendFormatted(RespLiterals.LAddr);
                handler.AppendFormatted(Format.ToString(serverEndpoint).AsRedisValue());
            }

            if (filter.SkipMe is { } skipMe)
            {
                handler.AppendFormatted(RespLiterals.SkipMe);
                handler.AppendFormatted(skipMe ? RespLiterals.Yes : RespLiterals.No);
            }

            if (filter.MaxAgeInSeconds is { } maxAge)
            {
                handler.AppendFormatted(RespLiterals.MaxAge);
                handler.AppendFormatted((RedisValue)maxAge);
            }
        }
    }
}
