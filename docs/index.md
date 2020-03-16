StackExchange.Redis
===================

[Release Notes](ReleaseNotes)

## Overview

StackExchange.Redis is a high performance general purpose redis client for .NET languages (C#, etc.). It is the logical successor to [BookSleeve](https://code.google.com/archive/p/booksleeve/),
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

Documentation
---

- [Basic Usage](Basics.md) - getting started and basic usage
- [Configuration](Configuration.md) - options available when connecting to redis
- [Pipelines and Multiplexers](PipelinesMultiplexers.md) - what is a multiplexer?
- [Keys, Values and Channels](KeysValues.md) - discusses the data-types used on the API
- [Transactions](Transactions.md) - how atomic transactions work in redis
- [Events](Events.md) - the events available for logging / information purposes
- [Pub/Sub Message Order](PubSubOrder.md) - advice on sequential and concurrent processing
- [Streams](Streams.md) - how to use the Stream data type
- [Where are `KEYS` / `SCAN` / `FLUSH*`?](KeysScan.md) - how to use server-based commands
- [Profiling](Profiling.md) - profiling interfaces, as well as how to profile in an `async` world
- [Scripting](Scripting.md) - running Lua scripts with convenient named parameter replacement
- [Testing](Testing.md) - running the `StackExchange.Redis.Tests` suite to validate changes
- [Thread Theft](ThreadTheft.md) - guidance on avoiding TPL threading problems

Questions and Contributions
---

If you think you have found a bug or have a feature request, please [report an issue][2], or if appropriate: submit a pull request. If you have a question, feel free to [contact me](https://github.com/mgravell).

  [1]: http://msdn.microsoft.com/en-us/library/dd460717%28v=vs.110%29.aspx
  [2]: https://github.com/StackExchange/StackExchange.Redis/issues?state=open
