# resp-cli

`resp-cli` is a .NET global tool that provides a basic interactive CLI for talking to `RESP` servers such as [Redis](https://redis.io/), [Garnet](https://microsoft.github.io/garnet/),
[Valkey](https://valkey.io/), or anything else that talks `RESP`.

It is intentionally comparable to the Redis tool `redis-cli`, but is explicitly intended to be server-agnostic,
and deliberately avoids implementation-specific naming. `RESP` is the name of the data protocol used by Redis and
redis-like servers (usually key-value stores).


