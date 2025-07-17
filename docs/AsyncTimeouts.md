# Async timeouts and cancellation

StackExchange.Redis directly supports timeout of *synchronous* operations, but for *asynchronous* operations, it is recommended
to use the inbuilt framework support for cancellation and timeouts, i.e. the [WaitAsync](https://learn.microsoft.com/dotnet/api/system.threading.tasks.task.waitasync)
family of methods. This allows the caller to control timeout (via `TimeSpan`), cancellation (via `CancellationToken`), or both.

Note that it is possible that operations will still be buffered and may still be issued to the server *after* timeout/cancellation means
that the caller isn't observing the result.

## Usage

### Timeout

Timeouts are probably the most common cancellation scenario:

```csharp
var timeout = TimeSpan.FromSeconds(5);
await database.StringSetAsync("key", "value").WaitAsync(timeout);
var value = await database.StringGetAsync("key").WaitAsync(timeout);
```

### Cancellation

You can also use `CancellationToken` to drive cancellation, identically:

```csharp
CancellationToken token = ...; // for example, from HttpContext.RequestAborted
await database.StringSetAsync("key", "value").WaitAsync(token);
var value = await database.StringGetAsync("key").WaitAsync(token);
```
### Combined Cancellation and Timeout

These two concepts can be combined so that if either cancellation or timeout occur, the caller's
operation  is cancelled:

```csharp
var timeout = TimeSpan.FromSeconds(5);
CancellationToken token = ...; // for example, from HttpContext.RequestAborted
await database.StringSetAsync("key", "value").WaitAsync(timeout, token);
var value = await database.StringGetAsync("key").WaitAsync(timeout, token);
```

### Creating a timeout for multiple operations

If you want a timeout to apply to a *group* of operations rather than individually, then you
can using `CancellationTokenSource` to create a `CancellationToken` that is cancelled after a
specified timeout. For example:

```csharp
var timeout = TimeSpan.FromSeconds(5);
using var cts = new CancellationTokenSource(timeout);
await database.StringSetAsync("key", "value").WaitAsync(cts.Token);
var value = await database.StringGetAsync("key").WaitAsync(cts.Token);
```

This can additionally be combined with one-or-more cancellation tokens:

```csharp
var timeout = TimeSpan.FromSeconds(5);
CancellationToken token = ...; // for example, from HttpContext.RequestAborted
using var cts = CancellationTokenSource.CreateLinkedTokenSource(token); // or multiple tokens
cts.CancelAfter(timeout);
await database.StringSetAsync("key", "value").WaitAsync(cts.Token);
var value = await database.StringGetAsync("key").WaitAsync(cts.Token);
``````