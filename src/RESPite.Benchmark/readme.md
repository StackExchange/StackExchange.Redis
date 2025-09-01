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