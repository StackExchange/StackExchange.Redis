# resp-benchmark

The `resp-benchmark` tool is a command-line "RESP" benchmark client, comparable to `redis-benchmark`, and
many of the arguments are the same. This is mostly for internal team usage, but is included here for
reference.

Example usage:

``` bash
> dotnet tool install -g RESPite.Benchmark

# basic usage
> resp-benchmark

# 50 clients, pipeline to 100, multiplexed, 1M operations, only test incr, loop 
> resp-benchmark -c 50 -P 100 -n 1000000 +m -t incr -l

```

## Options supported from `redis-benchmark`

Basic options, for parity:

- `-h <hostname>` Server hostname (default 127.0.0.1).
- `-p <port>` Server port (default 6379).
- `-c <clients>` Number of parallel connections (default 50).
- `-n <requests>` Total number of requests (default 100000).
- `-d <size>` Data size of SET/GET value in bytes (default 3).
- `-P <numreq>` Pipeline <numreq> requests. Default 1 (no pipeline).
- `-l` Loop. Run the tests forever.
- `-q` Quiet. Just show query/sec values.
- `-t <tests>` Only run the comma separated list of tests. The test names are the same as the ones produced as output.

## Custom options

Additional options specific to this tool:

- `+m` / `-m`: enable or disable (default) multiplexing: when enabled clients share a connection, otherwise each client has a separate connection.
- `--batch` / `--queue` pipelining should using batching (default) or queueing strategy.
- `--basic` : perform basic typical IO operations rather than synthetic benchmarks.


## Internal options

These exist mostly for Marc's benefit:

- `-w <mode>` Specify the internal write-mode.
- `+x` / `-x`: enable or disable (default) cancellation support (irrelevant until later v3 tranche).

## Local example

To build and run from source, `dotnet run` can be used with everything after `--` being args to the command:

```
dotnet run -p:TargetVer=3 -f net10.0 -c Release -- -q -c 50 -P 100 +m --queue -n 500000 -q -l -t INCR
```