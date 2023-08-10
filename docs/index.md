StackExchange.Redis
===================

- [Release Notes](ReleaseNotes)
- [API Browser (via fuget.org)](https://www.fuget.org/packages/StackExchange.Redis/)

## Overview

StackExchange.Redis is a high performance general purpose redis client for .NET languages (C#, etc.). It is the logical successor to [BookSleeve](https://code.google.com/archive/p/booksleeve/),
and is the client developed-by (and used-by) [Stack Exchange](https://stackexchange.com/) for busy sites like [Stack Overflow](https://stackoverflow.com/). For the full reasons
why this library was created (i.e. "What about BookSleeve?") [please see here](https://marcgravell.blogspot.com/2014/03/so-i-went-and-wrote-another-redis-client.html).

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

- [Server](Server) - running a redis server
- [Basic Usage](Basics) - getting started and basic usage
- [Configuration](Configuration) - options available when connecting to redis
- [Pipelines and Multiplexers](PipelinesMultiplexers) - what is a multiplexer?
- [Keys, Values and Channels](KeysValues) - discusses the data-types used on the API
- [Transactions](Transactions) - how atomic transactions work in redis
- [Events](Events) - the events available for logging / information purposes
- [Pub/Sub Message Order](PubSubOrder) - advice on sequential and concurrent processing
- [Using RESP3](Resp3) - information on using RESP3
- [ServerMaintenanceEvent](ServerMaintenanceEvent) - how to listen and prepare for hosted server maintenance (e.g. Azure Cache for Redis)
- [Streams](Streams) - how to use the Stream data type
- [Where are `KEYS` / `SCAN` / `FLUSH*`?](KeysScan) - how to use server-based commands
- [Profiling](Profiling) - profiling interfaces, as well as how to profile in an `async` world
- [Scripting](Scripting) - running Lua scripts with convenient named parameter replacement
- [Testing](Testing) - running the `StackExchange.Redis.Tests` suite to validate changes
- [Timeouts](Timeouts) - guidance on dealing with timeout problems
- [Thread Theft](ThreadTheft) - guidance on avoiding TPL threading problems

Questions and Contributions
---

If you think you have found a bug or have a feature request, please [report an issue][2], or if appropriate: submit a pull request. If you have a question, feel free to [contact me](https://github.com/mgravell).

  [1]: https://docs.microsoft.com/en-us/dotnet/standard/parallel-programming/task-parallel-library-tpl
  [2]: https://github.com/StackExchange/StackExchange.Redis/issues?state=open
