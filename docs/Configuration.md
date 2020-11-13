Configuration
===

Because there are lots of different ways to configure redis, StackExchange.Redis offers a rich configuration model, which is invoked when calling `Connect` (or `ConnectAsync`):

```csharp
var conn = ConnectionMultiplexer.Connect(configuration);
```

The `configuration` here can be either:

- a `ConfigurationOptions` instance
- a `string` representing the configuration

The latter is *basically* a tokenized form of the former.

Basic Configuration Strings
-

The *simplest* configuration example is just the host name:

```csharp
var conn = ConnectionMultiplexer.Connect("localhost");
```

This will connect to a single server on the local machine using the default redis port (6379). Additional options are simply appended (comma-delimited). Ports are represented with a colon (`:`) as is usual. Configuration *options* include an `=` after the name. For example:

```csharp
var conn = ConnectionMultiplexer.Connect("redis0:6380,redis1:6380,allowAdmin=true");
```

If you specify a serviceName in the connection string, it will trigger sentinel mode. This example will connect to a sentinel server on the local machine
using the default sentinel port (26379), discover the current master server for the `mymaster` service and return a managed connection
pointing to that master server that will automatically be updated if the master changes:

```csharp
var conn = ConnectionMultiplexer.Connect("localhost,serviceName=mymaster");
```

An overview of mapping between the `string` and `ConfigurationOptions` representation is shown below, but you can switch between them trivially:

```csharp
ConfigurationOptions options = ConfigurationOptions.Parse(configString);
```

or:

```csharp
string configString = options.ToString();
```

A common usage is to store the *basic* details in a string, and then apply specific details at runtime:

```csharp
string configString = GetRedisConfiguration();
var options = ConfigurationOptions.Parse(configString);
options.ClientName = GetAppName(); // only known at runtime
options.AllowAdmin = true;
conn = ConnectionMultiplexer.Connect(options);
```

Microsoft Azure Redis example with password

```csharp
var conn = ConnectionMultiplexer.Connect("contoso5.redis.cache.windows.net,ssl=true,password=...");
```

Configuration Options
---

The `ConfigurationOptions` object has a wide range of properties, all of which are fully documented in intellisense. Some of the more common options to use include:

| Configuration string   | `ConfigurationOptions` | Default                      | Meaning                                                                                                   |
| ---------------------- | ---------------------- | ---------------------------- | --------------------------------------------------------------------------------------------------------- |
| abortConnect={bool}    | `AbortOnConnectFail`   | `true` (`false` on Azure)    | If true, `Connect` will not create a connection while no servers are available                            |
| allowAdmin={bool}      | `AllowAdmin`           | `false`                      | Enables a range of commands that are considered risky                                                     |
| channelPrefix={string} | `ChannelPrefix`        | `null`                       | Optional channel prefix for all pub/sub operations                                                        |
| checkCertificateRevocation={bool} | `CheckCertificateRevocation`        | `true`                       | A Boolean value that specifies whether the certificate revocation list is checked during authentication.                                                        |
| connectRetry={int}     | `ConnectRetry`         | `3`                          | The number of times to repeat connect attempts during initial `Connect`                                   |
| connectTimeout={int}   | `ConnectTimeout`       | `5000`                       | Timeout (ms) for connect operations                                                                       |
| configChannel={string} | `ConfigurationChannel` | `__Booksleeve_MasterChanged` | Broadcast channel name for communicating configuration changes                                            |
| configCheckSeconds={int} | `ConfigCheckSeconds`  | `60`                         | Time (seconds) to check configuration. This serves as a keep-alive for interactive sockets, if it is supported.     |
| defaultDatabase={int}  | `DefaultDatabase`      | `null`                       | Default database index, from `0` to `databases - 1`                                                       |
| keepAlive={int}        | `KeepAlive`            | `-1`                         | Time (seconds) at which to send a message to help keep sockets alive (60 sec default)                     |
| name={string}          | `ClientName`           | `null`                       | Identification for the connection within redis                                                            |
| password={string}      | `Password`             | `null`                       | Password for the redis server                                                                             |
| user={string}          | `User`                 | `null`                       | User for the redis server (for use with ACLs on redis 6 and above)                                        |
| proxy={proxy type}     | `Proxy`                | `Proxy.None`                 | Type of proxy in use (if any); for example "twemproxy"                                                    |
| resolveDns={bool}      | `ResolveDns`           | `false`                      | Specifies that DNS resolution should be explicit and eager, rather than implicit                          |
| serviceName={string}   | `ServiceName`          | `null`                       | Used for connecting to a sentinel master service                                                          |
| ssl={bool}             | `Ssl`                  | `false`                      | Specifies that SSL encryption should be used                                                              |
| sslHost={string}       | `SslHost`              | `null`                       | Enforces a particular SSL host identity on the server's certificate                                       |
| sslProtocols={enum}    | `SslProtocols`         | `null`                       | Ssl/Tls versions supported when using an encrypted connection.  Use '\|' to provide multiple values.      |
| syncTimeout={int}      | `SyncTimeout`          | `5000`                       | Time (ms) to allow for synchronous operations                                                             |
| asyncTimeout={int}     | `AsyncTimeout`          | `SyncTimeout`               | Time (ms) to allow for asynchronous operations                                                            |
| tiebreaker={string}    | `TieBreaker`           | `__Booksleeve_TieBreak`      | Key to use for selecting a server in an ambiguous master scenario                                         |
| version={string}       | `DefaultVersion`       | (`3.0` in Azure, else `2.0`) | Redis version level (useful when the server does not make this available)                                 |
|                        | `CheckCertificateRevocation` | `true`                 | A Boolean value that specifies whether the certificate revocation list is checked during authentication.  |

Additional code-only options:
- ReconnectRetryPolicy (`IReconnectRetryPolicy`) - Default: `ReconnectRetryPolicy = LinearRetry(ConnectTimeout);`

Tokens in the configuration string are comma-separated; any without an `=` sign are assumed to be redis server endpoints. Endpoints without an explicit port will use 6379 if ssl is not enabled, and 6380 if ssl is enabled.
Tokens starting with `$` are taken to represent command maps, for example: `$config=cfg`.

Obsolete Configuration Options
---
These options are parsed in connection strings for backwards compatibility (meaning they do not error as invalid), but no longer have any effect.

| Configuration string   | `ConfigurationOptions` | Previous Default | Previous Meaning |
| ---------------------- | ---------------------- | ---------------------------- | --------------------------------------------------------------------------------------------------------- |
| responseTimeout={int} | `ResponseTimeout` | `SyncTimeout` | Time (ms) to decide whether the socket is unhealthy |
| writeBuffer={int} | `WriteBuffer` | `4096` | Size of the output buffer |

Automatic and Manual Configuration
---

In many common scenarios, StackExchange.Redis will automatically configure a lot of settings, including the server type and version, connection timeouts, and master/replica relationships. Sometimes, though, the commands for this have been disabled on the redis server. In this case, it is useful to provide more information:

```csharp
ConfigurationOptions config = new ConfigurationOptions
{
    EndPoints =
    {
        { "redis0", 6379 },
        { "redis1", 6380 }
    },
    CommandMap = CommandMap.Create(new HashSet<string>
    { // EXCLUDE a few commands
        "INFO", "CONFIG", "CLUSTER",
        "PING", "ECHO", "CLIENT"
    }, available: false),
    KeepAlive = 180,
    DefaultVersion = new Version(2, 8, 8),
    Password = "changeme"
};
```

Which is equivalent to the command string:

```config
redis0:6379,redis1:6380,keepAlive=180,version=2.8.8,$CLIENT=,$CLUSTER=,$CONFIG=,$ECHO=,$INFO=,$PING=
```
Renaming Commands
---

A slightly unusual feature of redis is that you can disable and/or rename individual commands. As per the previous example, this is done via the `CommandMap`, but instead of passing a `HashSet<string>` to `Create()` (to indicate the available or unavailable commands), you pass a `Dictionary<string,string>`. All commands not mentioned in the dictionary are assumed to be enabled and not renamed. A `null` or blank value records that the command is disabled. For example:

```csharp
var commands = new Dictionary<string,string> {
        { "info", null }, // disabled
        { "select", "use" }, // renamed to SQL equivalent for some reason
};
var options = new ConfigurationOptions {
    // ...
    CommandMap = CommandMap.Create(commands),
    // ...
}
```

The above is equivalent to (in the connection string):

```config
$INFO=,$SELECT=use
```

Twemproxy
---

[Twemproxy](https://github.com/twitter/twemproxy) is a tool that allows multiple redis instances to be used as though it were a single server, with inbuilt sharding and fault tolerance (much like redis cluster, but implemented separately). The feature-set available to Twemproxy is reduced. To avoid having to configure this manually, the `Proxy` option can be used:

```csharp
var options = new ConfigurationOptions
{
    EndPoints = { "my-server" },
    Proxy = Proxy.Twemproxy
};
```

Tiebreakers and Configuration Change Announcements
---

Normally StackExchange.Redis will resolve master/replica nodes automatically. However, if you are not using a management tool such as redis-sentinel or redis cluster, there is a chance that occasionally you will get multiple master nodes (for example, while resetting a node for maintenance it may reappear on the network as a master). To help with this, StackExchange.Redis can use the notion of a *tie-breaker* - which is only used when multiple masters are detected (not including redis cluster, where multiple masters are *expected*). For compatibility with BookSleeve, this defaults to the key named `"__Booksleeve_TieBreak"` (always in database 0). This is used as a crude voting mechanism to help determine the *preferred* master, so that work is routed correctly.

Likewise, when the configuration is changed (especially the master/replica configuration), it will be important for connected instances to make themselves aware of the new situation (via `INFO`, `CONFIG`, etc - where available). StackExchange.Redis does this by automatically subscribing to a pub/sub channel upon which such notifications may be sent. For similar reasons, this defaults to `"__Booksleeve_MasterChanged"`.

Both options can be customized or disabled (set to `""`), via the `.ConfigurationChannel` and `.TieBreaker` configuration properties.

These settings are also used by the `IServer.MakeMaster()` method, which can set the tie-breaker in the database and broadcast the configuration change message. The configuration message can also be used separately to master/replica changes simply to request all nodes to refresh their configurations, via the `ConnectionMultiplexer.PublishReconfigure` method.

ReconnectRetryPolicy
---
StackExchange.Redis automatically tries to reconnect in the background when the connection is lost for any reason. It keeps retrying  until the connection has been restored. It would use ReconnectRetryPolicy to decide how long it should wait between the retries.
ReconnectRetryPolicy can be linear (default), exponential or a custom retry policy.


Examples:
```csharp
config.ReconnectRetryPolicy = new ExponentialRetry(5000); // defaults maxDeltaBackoff to 10000 ms
//retry#    retry to re-connect after time in milliseconds
//1	        a random value between 5000 and 5500	   
//2	        a random value between 5000 and 6050	   
//3	        a random value between 5000 and 6655	   
//4	        a random value between 5000 and 8053
//5	        a random value between 5000 and 10000, since maxDeltaBackoff was 10000 ms
//6	        a random value between 5000 and 10000

config.ReconnectRetryPolicy = new LinearRetry(5000);
//retry#    retry to re-connect after time in milliseconds
//1	        5000
//2	        5000 	   
//3	        5000 	   
//4	        5000
//5	        5000
//6	        5000
```
