# Ambient Cancellation Support

StackExchange.Redis now supports ambient cancellation using `AsyncLocal<T>` to provide cancellation tokens to Redis operations without expanding the API surface.

## Overview

The ambient cancellation feature allows you to set a cancellation context that applies to all Redis operations within an async scope. This provides a clean way to handle timeouts and cancellation without adding `CancellationToken` parameters to every method.

## Key Features

- **Zero API Surface Impact**: No new parameters added to existing methods
- **Scoped Cancellation**: Uses `using` statements for proper scope management
- **Timeout Support**: Can specify timeouts that are converted to cancellation tokens
- **Composable**: Can combine cancellation tokens with timeouts
- **Nested Scopes**: Inner scopes override outer scopes
- **Backward Compatible**: All existing code continues to work unchanged

## Usage

### Basic Cancellation

```csharp
using var cts = new CancellationTokenSource();

using (database.WithCancellation(cts.Token))
{
    await database.StringSetAsync("key", "value");
    var value = await database.StringGetAsync("key");
    // Both operations use the cancellation token
}
```

### Timeout Support

```csharp
using (database.WithTimeout(TimeSpan.FromSeconds(5)))
{
    await database.StringSetAsync("key", "value");
    // Operation will be cancelled if it takes longer than 5 seconds
}
```

### Combined Cancellation and Timeout

```csharp
using var cts = new CancellationTokenSource();

using (database.WithCancellationAndTimeout(cts.Token, TimeSpan.FromSeconds(10)))
{
    await database.StringSetAsync("key", "value");
    // Operation will be cancelled if either the token is cancelled OR 10 seconds elapse
}
```

### Nested Scopes

```csharp
using var outerToken = new CancellationTokenSource();
using var innerToken = new CancellationTokenSource();

using (database.WithCancellation(outerToken.Token))
{
    await database.StringSetAsync("key1", "value1"); // Uses outerToken
    
    using (database.WithCancellation(innerToken.Token))
    {
        await database.StringSetAsync("key2", "value2"); // Uses innerToken
    }
    
    await database.StringSetAsync("key3", "value3"); // Uses outerToken again
}
```

### Pub/Sub Operations

```csharp
using var cts = new CancellationTokenSource();

using (subscriber.WithCancellation(cts.Token))
{
    await subscriber.SubscribeAsync("channel", handler);
    await subscriber.PublishAsync("channel", "message");
    // Both operations use the cancellation token
}
```

## Extension Methods

The functionality is provided through extension methods on `IRedisAsync`:

- `WithCancellation(CancellationToken)` - Sets ambient cancellation token
- `WithTimeout(TimeSpan)` - Sets ambient timeout (converted to cancellation token)
- `WithCancellationAndTimeout(CancellationToken, TimeSpan)` - Sets both cancellation and timeout

## Implementation Details

### AsyncLocal Context

The implementation uses `AsyncLocal<T>` to flow the cancellation context through async operations:

```csharp
private static readonly AsyncLocal<CancellationContext?> _context = new();
```

### Scope Management

Each `WithCancellation` call returns an `IDisposable` that manages the scope:

```csharp
public static IDisposable WithCancellation(this IRedisAsync redis, CancellationToken cancellationToken)
{
    return new CancellationScope(cancellationToken, null);
}
```

### Integration Points

The cancellation token is applied at the core execution level:

1. `RedisBase.ExecuteAsync` retrieves the ambient cancellation token
2. `ConnectionMultiplexer.ExecuteAsyncImpl` accepts the cancellation token
3. `TaskResultBox<T>` registers for cancellation and properly handles cancellation

### Timeout Handling

Timeouts are converted to cancellation tokens using `CancellationTokenSource`:

```csharp
public CancellationToken GetEffectiveToken()
{
    if (!Timeout.HasValue) return Token;
    
    var timeoutSource = new CancellationTokenSource(Timeout.Value);
    return Token.CanBeCanceled
        ? CancellationTokenSource.CreateLinkedTokenSource(Token, timeoutSource.Token).Token
        : timeoutSource.Token;
}
```

## Error Handling

When an operation is cancelled, it throws an `OperationCanceledException`:

```csharp
try
{
    using (database.WithCancellation(cancelledToken))
    {
        await database.StringSetAsync("key", "value");
    }
}
catch (OperationCanceledException)
{
    // Handle cancellation
}
```

## Performance Considerations

- **Minimal Overhead**: When no ambient cancellation is set, there's virtually no performance impact
- **Efficient Scoping**: Uses struct-based scoping to minimize allocations
- **Proper Cleanup**: Cancellation registrations are properly disposed when operations complete

## Limitations

- **Client-Side Only**: Redis doesn't support server-side cancellation, so cancellation only prevents the client from waiting for a response
- **In-Flight Operations**: Operations that have already been sent to the server will continue executing on the server even if cancelled on the client
- **Connection Health**: Cancelled operations don't affect connection health or availability

## Migration

Existing code requires no changes. The ambient cancellation is purely additive:

```csharp
// This continues to work exactly as before
await database.StringSetAsync("key", "value");

// This adds cancellation support
using (database.WithCancellation(cancellationToken))
{
    await database.StringSetAsync("key", "value");
}
```

## Best Practices

1. **Use `using` statements** to ensure proper scope cleanup
2. **Prefer cancellation tokens over timeouts** when possible for better control
3. **Handle `OperationCanceledException`** appropriately in your application
4. **Don't rely on cancellation for server-side operation termination**
5. **Test cancellation scenarios** to ensure your application handles them gracefully

## Examples

See `examples/CancellationExample.cs` for comprehensive usage examples and `tests/StackExchange.Redis.Tests/CancellationTests.cs` for test cases demonstrating the functionality.
