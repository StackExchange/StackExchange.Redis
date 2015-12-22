Configuration
===

Because there are lots of different ways to configure redis, StackExchange.Redis offers a rich configuration model, which is invoked when calling `Connect` (or `ConnectAsync`):

```C#
var conn = ConnectionMultiplexer.Connect(configuration);
```

The `configuration` here can be either:

- a `ConfigurationOptions` instance
- a `string` representing the configuration

The latter is *basically* a tokenized form of the former.

Basic Configuration Strings
-

The *simplest* configuration example is just the host name:

```C#
var conn = ConnectionMultiplexer.Connect("localhost");
```

This will connect to a single server on the local machine using the default redis port (6379). Additional options are simply appended (comma-delimited). Ports are represented with a colon (`:`) as is usual. Configuration *options* include an `=` after the name. For example:

```C#
var conn = ConnectionMultiplexer.Connect("redis0:6380,redis1:6380,allowAdmin=true");
```

An overview of mapping between the `string` and `ConfigurationOptions` representation is shown below, but you can switch between them trivially:

```C#
ConfigurationOptions options = ConfigurationOptions.Parse(configString);
```

or:

```C#
string configString = options.ToString();
```

A common usage is to store the *basic* details in a string, and then apply specific details at runtime:

```C#
string configString = GetRedisConfiguration();
var options = ConfigurationOptions.Parse(configString);
options.ClientName = GetAppName(); // only known at runtime
options.AllowAdmin = true;
conn = ConnectionMultiplexer.Connect(options);
```

Microsoft Azure Redis example with password

```C#
var conn = ConnectionMultiplexer.Connect("contoso5.redis.cache.windows.net,ssl=true,password=...");
```

Configuration Options
---

The `ConfigurationOptions` object has a wide range of properties, all of which are fully documented in intellisense. Some of the more common options to use include:

| Configuration string   | `ConfigurationOptions` | Meaning                                                                         |
| ---------------------- | ---------------------- | ------------------------------------------------------------------------------- |
| abortConnect={bool}    | `AbortOnConnectFail`   | If true, `Connect` will not create a connection while no servers are available  |
| allowAdmin={bool}     | `AllowAdmin`           | Enables a range of commands that are considered risky                           |
| channelPrefix={string} | `ChannelPrefix`        | Optional channel prefix for all pub/sub operations                              |
| connectRetry={int}     | `ConnectRetry`         | The number of times to repeat connect attempts during initial `Connect`         |
| connectTimeout={int}   | `ConnectTimeout`       | Timeout (ms) for connect operations                                             |
| configChannel={string} | `ConfigurationChannel` | Broadcast channel name for communicating configuration changes                  |
| defaultDatabase={int}  | `DefaultDatabase`      | Default database index, from `0` to `databases - 1`
| keepAlive={int}        | `KeepAlive`            | Time (seconds) at which to send a message to help keep sockets alive            |
| name={string}          | `ClientName`           | Identification for the connection within redis                                  |
| password={string}      | `Password`             | Password for the redis server                                                   |
| proxy={proxy type}     | `Proxy`                | Type of proxy in use (if any); for example "twemproxy"                          |
| resolveDns={bool}      | `ResolveDns`           | Specifies that DNS resolution should be explicit and eager, rather than implicit |
| serviceName={string}   | `ServiceName`          | Not currently implemented (intended for use with sentinel)                      |
| ssl={bool}             | `Ssl`                  | Specifies that SSL encryption should be used                                    |
| sslHost={string}       | `SslHost`              | Enforces a particular SSL host identity on the server's certificate             |
| syncTimeout={int}      | `SyncTimeout`          | Time (ms) to allow for synchronous operations                                   |
| tiebreaker={string}    | `TieBreaker`           | Key to use for selecting a server in an ambiguous master scenario               |
| version={string}       | `DefaultVersion`       | Redis version level (useful when the server does not make this available)       |
| writeBuffer={int}      | `WriteBuffer`          | Size of the output buffer                                                       |

Tokens in the configuration string are comma-separated; any without an `=` sign are assumed to be redis server endpoints. Endpoints without an explicit port will use 6379 if ssl is not enabled, and 6380 if ssl is enabled.
Tokens starting with `$` are taken to represent command maps, for example: `$config=cfg`.

Automatic and Manual Configuration
---

In many common scenarios, StackExchange.Redis will automatically configure a lot of settings, including the server type and version, connection timeouts, and master/slave relationships. Sometimes, though, the commands for this have been disabled on the redis server. In this case, it is useful to provide more information:

```C#
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

```C#
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

```C#
var options = new ConfigurationOptions
{
    EndPoints = { "my-server" },
    Proxy = Proxy.Twemproxy
};
```

Tiebreakers and Configuration Change Announcements
---

Normally StackExchange.Redis will resolve master/slave nodes automatically. However, if you are not using a management tool such as redis-sentinel or redis cluster, there is a chance that occasionally you will get multiple master nodes (for example, while resetting a node for maintenance it may reappear on the network as a master). To help with this, StackExchange.Redis can use the notion of a *tie-breaker* - which is only used when multiple masters are detected (not including redis cluster, where multiple masters are *expected*). For compatibility with BookSleeve, this defaults to the key named `"__Booksleeve_TieBreak"` (always in database 0). This is used as a crude voting mechanism to help determine the *preferred* master, so that work is routed correctly.

Likewise, when the configuration is changed (especially the master/slave configuration), it will be important for connected instances to make themselves aware of the new situation (via `INFO`, `CONFIG`, etc - where available). StackExchange.Redis does this by automatically subscribing to a pub/sub channel upon which such notifications may be sent. For similar reasons, this defaults to `"__Booksleeve_MasterChanged"`.

Both options can be customized or disabled (set to `""`), via the `.ConfigurationChannel` and `.TieBreaker` configuration properties.

These settings are also used by the `IServer.MakeMaster()` method, which can set the tie-breaker in the database and broadcast the configuration change message. The configuration message can also be used separately to master/slave changes simply to request all nodes to refresh their configurations, via the `ConnectionMultiplexer.PublishReconfigure` method.
