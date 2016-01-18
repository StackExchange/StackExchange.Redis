StackExchange.Redis
===================

StackExchange.Redis is a high performance general purpose redis client for .NET languages (C# etc). It is the logical successor to [BookSleeve](https://code.google.com/p/booksleeve/),
and is the client developed-by (and used-by) [Stack Exchange](http://stackexchange.com/) for busy sites like [Stack Overflow](http://stackoverflow.com/). For the full reasons
why this library was created (i.e. "What about BookSleeve?") [please see here](http://marcgravell.blogspot.com/2014/03/so-i-went-and-wrote-another-redis-client.html).

Features
--

- High performance multiplexed design, allowing for efficient use of shared connections from multiple calling threads
- Abstraction over redis node configuration: the client can silently negotiate multiple redis servers for robustness and availability
- Convenient access to the full redis feature-set
- Full dual programming model both synchronous and asynchronous usage, without requiring "sync over async" usage of the [TPL][1]
- Support for redis "cluster"

Installation
---

StackExchange.Redis can be installed via the nuget UI (as [StackExchange.Redis](https://www.nuget.org/packages/StackExchange.Redis/)), or via the nuget package manager console:

```PowerShell
PM> Install-Package StackExchange.Redis
```

If you require a strong-named package (because your project is strong-named), then you may wish to use instead:

```PowerShell
PM> Install-Package StackExchange.Redis.StrongName
```

([for further reading, see here](http://blog.marcgravell.com/2014/06/snk-we-need-to-talk.html))

Documentation
---

- [Basic Usage](https://github.com/StackExchange/StackExchange.Redis/blob/master/Docs/Basics.md) - getting started and basic usage
- [Configuration](https://github.com/StackExchange/StackExchange.Redis/blob/master/Docs/Configuration.md) - options available when connecting to redis
- [Pipelines and Multiplexers](https://github.com/StackExchange/StackExchange.Redis/blob/master/Docs/PipelinesMultiplexers.md) - what is a multiplexer?
- [Keys, Values and Channels](https://github.com/StackExchange/StackExchange.Redis/blob/master/Docs/KeysValues.md) - discusses the data-types used on the API
- [Transactions](https://github.com/StackExchange/StackExchange.Redis/blob/master/Docs/Transactions.md) - how atomic transactions work in redis
- [Events](https://github.com/StackExchange/StackExchange.Redis/blob/master/Docs/Events.md) - the events available for logging / information purposes
- [Pub/Sub Message Order](https://github.com/StackExchange/StackExchange.Redis/blob/master/Docs/PubSubOrder.md) - advice on sequential and concurrent processing
- [Where are `KEYS` / `SCAN` / `FLUSH*`?](https://github.com/StackExchange/StackExchange.Redis/blob/master/Docs/KeysScan.md) - how to use server-based commands
- [Profiling](https://github.com/StackExchange/StackExchange.Redis/blob/master/Docs/Profiling.md) - profiling interfaces, as well as how to profile in an `async` world
- [Scripting](https://github.com/StackExchange/StackExchange.Redis/blob/master/Docs/Scripting.md) - running Lua scripts with convenient named parameter replacement 

Questions and Contributions
---

If you think you have found a bug or have a feature request, please [report an issue][2], or if appropriate: submit a pull request. If you have a question, feel free to [contact me](https://github.com/mgravell).

  [1]: http://msdn.microsoft.com/en-us/library/dd460717%28v=vs.110%29.aspx
  [2]: https://github.com/StackExchange/StackExchange.Redis/issues?state=open
