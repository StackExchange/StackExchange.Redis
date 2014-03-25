Configuration
===

Because there are lots of different ways to configure redis, StackExchange.Redis offers a rich configuration model, which is invoked when calling `Connect` (or `ConnectAsync`):

    var conn = ConnectionMultiplexer.Connect(configuration);

The `configuration` here can be either:

- a `ConfigurationOptions` instance
- a `string` representing the configuration

The latter is *basically* a tokenized form of the former. 

Basic Configuration Strings
-

The *simplest* configuration example is just the host name:

    var conn = ConnectionMultiplexer.Connect("localhost");

This will connect to a single server on the local machine using the default redis port (6379). Additional options are simply appended (comma-delimited). Ports are represented with a colon (`:`) as is usual. Configuration *options* include an `=` after the name. For example:

    var conn = ConnectionMultiplexer.Connect("redis0:6380,redis1:6380,allowAdmin=true");

An extensive mapping between the `string` and `ConfigurationOptions` representation is not documented here, but you can switch between them trivially:

    ConfigurationOptions options = ConfigurationOptions.Parse(configString);

or:

    string configString = options.ToString();

Configuration Options
---

The `ConfigurationOptions` object has a wide range of properties, all of which are fully documented in intellisense. Some of the more common options to use include:

- `AllowAdmin` - enables potentially harmful system commands that are not needed by data-oriented clients
- `ClientName` - sets a name against the connections to identify them (visible to redis maintenance tools)
- `CommandMap` - renames or disables individual redis commands
- `DefaultVersion` - the redis version to assume if it cannot auto-configure
- `EndPoints` - the set of redis nodes to connect to
- `Password` - the password to authenticate (`AUTH`) against the redis server
- `SyncTimeout` - the timeout to apply when performing synchronous operations

Automatic and Manual Configuration
---

In many common scenarios, StackExchange.Redis will automatically configure a lot of settings, including the server type and version, connection timeouts, and master/slave relationships. Sometimes, though, the commands for this have been disabled.