# Ambient Cancellation Support

StackExchange.Redis supports cancellation / timeout of operations. Because this feature impacts all operations, rather than add new parameters
to every method, it uses a declarative scope - an "ambient" context. This uses the `AsyncLocal<T>` feature, allowing for meaning:

- unrelated code-paths (threads or async-contexts) can have different values without conflicting with each-other
- the value is correctly propagated between `async`/`await` code

## Usage

### Timeout

Timeouts are probably the most common cancellation scenario, so is exposed directly:

```csharp
using (database.Multiplexer.WithTimeout(TimeSpan.FromSeconds(5)))
// using (database.Multiplexer.WithTimeout(5_000)) // identical
{
    await database.StringSetAsync("key", "value");
    var value = await database.StringGetAsync("key");
    // operations will be cancelled when the *combined* time (i.e. from the `WithTimeout` call) exceeds 5 seconds
}
```

### Cancellation

You can also use `CancellationToken` to drive cancellation:

```csharp
CancellationToken token = ...; // for example, from HttpContext.RequestAborted
using (database.Multiplexer.WithCancellation(token))
{
    await database.StringSetAsync("key", "value");
    var value = await database.StringGetAsync("key");
    // both operations use the cancellation token
}
```
### Combined Cancellation and Timeout

These two concepts can be combined:

```csharp
CancellationToken token = ...; // for example, from HttpContext.RequestAborted
using (database.Multiplexer.WithCancellationAndTimeout(token, TimeSpan.FromSeconds(10)))
// using (database.Multiplexer.WithCancellationAndTimeout(token, 10_000)) // identical
{
    await database.StringSetAsync("key", "value");
    var value = await database.StringGetAsync("key");
    // operations use the cancellation token *and* observe the specified timeout
}
```

### Nested Scopes

Timeout/cancellation scopes can be nested, with the inner scope *replacing* the outer scope for that database:

```csharp
using (database.Multiplexer.WithCancellation(yourToken))
{
    await database.StringSetAsync("key1", "value1"); // Uses yourToken
    
    using (database.Multiplexer.WithTimeout(5000))
    {
        await database.StringSetAsync("key2", "value2"); // Uses 5s timeout, but does *not* observe yourToken
    }
    
    await database.StringSetAsync("key3", "value3"); // Uses yourToken
}
```

Consequently, timeout/cancellation can be suppressed by using `.WithCancellation(CancellationToken.None)`.

## Multiplexer scope

The scope of a `WithTimeout` (etc) call is tied to the *multiplexer*, hence the typical usage of `database.Multiplexer.WithTimeout(...)`.
Usually, there is only a single multiplexer in use, but this choice ensures that there are no surprises by library code outside of
your control / knowledge being impacted by your local cancellation / timeout choices.