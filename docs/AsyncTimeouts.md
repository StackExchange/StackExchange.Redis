# Async timeouts and cancellation

StackExchange.Redis directly supports timeout of *synchronous* operations (see [Timeouts](Timeouts)). For *asynchronous*
operations, every method on the grouped API (`db.Strings`, `db.Hashes`, etc; see [The original `IDatabase` API](LegacyApi))
takes a `CancellationToken` as its final parameter, which lets the caller control timeout (via a
`CancellationTokenSource` with a delay), cancellation, or both.

## What cancellation does (and does not) do

Cancellation races the reply: whichever happens first decides the outcome. If the token is cancelled first, the
awaited call throws `OperationCanceledException` and any reply that arrives later is discarded.

- If the command has **not yet been written** to the socket (for example, it is still queued behind other work), it is
  dropped and never sent.
- If the command **has already been written**, it cannot be withdrawn: the server will still execute it, and any effect
  it has (a `SET`, an `INCR`, etc) still happens. The caller simply stops waiting for the result.

So cancellation is a statement about the *caller*, not a guarantee that the command did not run. Treat a cancelled
write the same way you would treat a timeout: its outcome is unknown.

A token that is already cancelled when the method is called fails immediately, without sending anything. Where
cancellation cannot be honoured at all (for example, inside a batch or transaction, whose commands are sent as a unit),
passing a cancellable token throws rather than pretending; pass `default` there.

The token is in addition to the library's own timeouts, not a replacement for them.

## Usage

### Timeout

Timeouts are probably the most common cancellation scenario:

```csharp
var timeout = TimeSpan.FromSeconds(5);
using var cts = new CancellationTokenSource(timeout);
await db.Strings.SetAsync("key", "value", cancellationToken: cts.Token);
var value = await db.Strings.GetAsync("key", cancellationToken: cts.Token);
```

Note that a `CancellationTokenSource` created this way covers *every* operation that uses its token, so the timeout
above applies to the two operations together, not to each one individually. For a per-operation timeout, create a
source per operation.

### Cancellation

You can also use an existing `CancellationToken` to drive cancellation, identically:

```csharp
CancellationToken token = ...; // for example, from HttpContext.RequestAborted
await db.Strings.SetAsync("key", "value", cancellationToken: token);
var value = await db.Strings.GetAsync("key", cancellationToken: token);
```

### Combined Cancellation and Timeout

These two concepts can be combined so that if either cancellation or timeout occur, the caller's
operation is cancelled:

```csharp
var timeout = TimeSpan.FromSeconds(5);
CancellationToken token = ...; // for example, from HttpContext.RequestAborted
using var cts = CancellationTokenSource.CreateLinkedTokenSource(token); // or multiple tokens
cts.CancelAfter(timeout);
await db.Strings.SetAsync("key", "value", cancellationToken: cts.Token);
var value = await db.Strings.GetAsync("key", cancellationToken: cts.Token);
```

### Cancelling keys enumeration

Keys being enumerated (via `SCAN`) can *also* be cancelled, using the inbuilt `.WithCancellation(...)` method:

```csharp
CancellationToken token = ...; // for example, from HttpContext.RequestAborted
await foreach (var key in server.KeysAsync(pattern: "*foo*").WithCancellation(token))
{
    ...
}
```

To use a timeout instead, you can use the `CancellationTokenSource` approach shown above.

## The original `IDatabase` API

The methods on `IDatabase` / `IDatabaseAsync` (`StringGetAsync` etc) do not take a `CancellationToken`. For those, use
the inbuilt framework support for cancellation and timeouts, i.e. the
[WaitAsync](https://learn.microsoft.com/dotnet/api/system.threading.tasks.task.waitasync) family of methods, which accept
a `TimeSpan`, a `CancellationToken`, or both:

```csharp
var timeout = TimeSpan.FromSeconds(5);
CancellationToken token = ...; // for example, from HttpContext.RequestAborted
await database.StringSetAsync("key", "value").WaitAsync(timeout);
var value = await database.StringGetAsync("key").WaitAsync(timeout, token);
```

`WaitAsync` only abandons the *wait*: the operation itself is unaffected, so it will still be buffered and may still be
issued to the server *after* the timeout/cancellation means that the caller isn't observing the result.
