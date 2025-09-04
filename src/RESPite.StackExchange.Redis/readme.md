# RESPite.StackExchange.Redis

This libary is a bridge between StackExchange.Redis and RESPite. It provides the `IConnectionMultiplexer`,
`IDatabase`, `IServer` APIs, but implemented using the `RespConnection` and `RespContext` primitives from
RESPite. This is the intended direction for StackExchange.Redis vFuture.