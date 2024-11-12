# resp-cli

`resp-cli` is a .NET global tool that provides a basic interactive CLI for talking to `RESP` servers such as [Redis](https://redis.io/), [Garnet](https://microsoft.github.io/garnet/),
[Valkey](https://valkey.io/), or anything else that talks `RESP`.

`resp-cli` has two different modes:

- `resp-cli` - a REPL that works similarly to `redis-cli` etc.
- `resp-cli --gui` a terminal desktop application with rich tools for inspecting RESP traffic and managing RESP databases.

You probably want the `--gui` mode! At some future point, this may switch, so that you need to use `resp-cli --repl` to
use the more basic mode.

It is intentionally comparable to the Redis tool `redis-cli`, but is explicitly intended to be server-agnostic,
and deliberately avoids implementation-specific naming. `RESP` is the name of the data protocol used by Redis and
redis-like servers (usually key-value stores).


