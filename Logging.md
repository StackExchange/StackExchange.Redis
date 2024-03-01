Logging and validating the underlying RESP stream
===

Sometimes (rarely) there is a question over the validity of the RESP stream from a server (especially when using proxies
or a "redis-like-but-not-actually-redis" server), and it is hard to know whether the *data sent* was bad, vs
the client library tripped over the data.

To help with this, an experimental API exists to help log and validate RESP streams. This API is not intended
for routine use (and may change at any time), but can be useful for diagnosing problems.

For example, consider we have the following load test which (on some setup) causes a failure with some
degree of reliability (even if you need to run it 6 times to see a failure):

``` c#
// connect
Console.WriteLine("Connecting...");
var options = ConfigurationOptions.Parse(ConnectionString);
await using var muxer = await ConnectionMultiplexer.ConnectAsync(options);
var db = muxer.GetDatabase();

// load
RedisKey testKey = "marc_abc";
await db.KeyDeleteAsync(testKey);
Console.WriteLine("Writing...");
for (int i = 0; i < 100; i++)
{
    // sync every 50 iterations (pipeline the rest)
    var flags = (i % 50) == 0 ? CommandFlags.None : CommandFlags.FireAndForget;
    await db.SetAddAsync(testKey, Guid.NewGuid().ToString(), flags);
}

// fetch
Console.WriteLine("Reading...");
int count = 0;
for (int i = 0; i < 10; i++)
{
    // this is deliberately not using SCARD
    // (to put load on the inbound)
    count += (await db.SetMembersAsync(testKey)).Length;
}
Console.WriteLine("all done");
```

## Logging RESP streams

When this fails, it will not be obvious exactly who is to blame. However, we can ask for the data streams
to be logged to the local file-system.

**Obviously, this may leave data on disk, so this may present security concerns if used with production data; use
this feature sparingly, and clean up after yourself!**

``` c#
// connect
Console.WriteLine("Connecting...");
var options = ConfigurationOptions.Parse(ConnectionString);
LoggingTunnel.LogToDirectory(options, @"C:\Code\RedisLog"); // <=== added!
await using var muxer = await ConnectionMultiplexer.ConnectAsync(options);
...
```

This API is marked `[Obsolete]` simply to discourage usage, but you can ignore this warning once you
understand what it is saying (using `#pragma warning disable CS0618` if necessary).

This will update the `ConfigurationOptions` with a custom `Tunnel` that performs file-based mirroring
of the RESP streams. If `Ssl` is enabled on the `ConfigurationOptions`, the `Tunnel` will *take over that responsibility*
(so that the unencrypted data can be logged), and will *disable* `Ssl` on the `ConfigurationOptions` - but TLS
will still be used correctly.

If we run our code, we will see that 2 files are written per connection ("in" and "out"); if you are using RESP2 (the default),
then 2 connections are usually established (one for regular "interactive" commands, and one for pub/sub messages), so this will
typically create 4 files.

## Validating RESP streams

RESP is *mostly* text, so a quick eyeball can be achieved using any text tool; an "out" file will typically start:

``` txt
$6
CLIENT
$7
SETNAME
...
```

and an "in" file will typically start:

``` txt
+OK
+OK
+OK
...
```

This is the start of the handshakes for identifying the client to the redis server, and the server acknowledging this (if
you have authentication enabled, there will be a `AUTH` command first, or `HELLO` on RESP3).

If there is a failure, you obviously don't want to manually check these files. Instead, an API exists to validate RESP streams:

``` c#
var messages = await LoggingTunnel.ValidateAsync(@"C:\Code\RedisLog");
Console.WriteLine($"{messages} RESP fragments validated");
```

If the RESP streams are *not* valid, an exception will provide further details.

**An exception here is strong evidence that there is a fault either in the redis server, or an intermediate proxy**.

Conversely, if the library reported a protocol failure but the validation step here *does not* report an error, then
that is strong evidence of a library error; [**please report this**](https://github.com/StackExchange/StackExchange.Redis/issues/new) (with details).

You can also *replay* the conversation locally, seeing the individual requests and responses:

``` c#
var messages = await LoggingTunnel.ReplayAsync(@"C:\Code\RedisLog", (cmd, resp) =>
{
    if (cmd.IsNull)
    {
        // out-of-band/"push" response
        Console.WriteLine("<< " + LoggingTunnel.DefaultFormatResponse(resp));
    }
    else
    {
        Console.WriteLine(" > " + LoggingTunnel.DefaultFormatCommand(cmd));
        Console.WriteLine(" < " + LoggingTunnel.DefaultFormatResponse(resp));
    }
});
Console.WriteLine($"{messages} RESP commands validated");
```

The `DefaultFormatCommand` and `DefaultFormatResponse` methods are provided for convenience, but you
can perform your own formatting logic if required. If a RESP erorr is encountered in the response to
a particular message, the callback will still be invoked to indicate that error. For example, after deliberately
introducing an error into the captured file, we might see:

``` txt
 > CLUSTER NODES
 < -ERR This instance has cluster support disabled
 > GET __Booksleeve_TieBreak
 < (null)
 > ECHO ...
 < -Invalid bulk string terminator
Unhandled exception. StackExchange.Redis.RedisConnectionException: Invalid bulk string terminator
```

The `-ERR` message is not a problem - that's normal and simply indicates that this is not a redis cluster; however, the
final pair is an `ECHO` request, for which the corresponding response was invalid. This information is useful for finding
out what happened.

Emphasis: this API is not intended for common/frequent usage; it is intended only to assist validating the underlying
RESP stream.