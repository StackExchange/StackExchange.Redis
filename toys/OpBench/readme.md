# OpBench

This is a basic client to show achieved throughput. It is designed to be modifiable, so that a consumer can substitute
a command that is more representative of their workload. It is not designed to be a fully featured benchmarking tool.

The work to perform is specified via `Work`; optionally, `Init` can be supplied as a once-only setup step.

Variables:

- `Connections`: the number of actual Redis connections to use. Defaults to 1. Most SE.Redis scenarios do not benefit from multiple connections, due to the internal multiplexing.
- `Clients`: the number of effective parallel clients to simulate. Defaults to 1 per connection.
  - each client represents a concurrent call path in your application code; high concurrency is very normal in .NET applications (especially server scenarios)
- `PipelineDepth`: the number of commands to pipeline (batch) per client. Defaults to 1.
  - Pipelining is a very effective way of improving throughput, but it is not always applicable; in particular, it is not applicable if you need the result
    of one command before issuing the next. Pipelining is especially suitable for write-heavy workloads.
  - Note that SE.Redis internally pipelines commands from parallel callers (clients) on the same connection.
- `IterationsPerClient`: the number of operations to perform per client. Defaults to 1000.
- `RepeatCount`: the number of times to repeat the test


