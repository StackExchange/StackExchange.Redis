# RESPite

RESPite is a high-performance low-level RESP (Redis, etc) library, used as the IO core for
StackExchange.Redis v3+. It is also available for direct use from other places!

## Getting Started

RESPite has two key primitives:

- a *connection*, `RespConnection`.
- a *context*, `RespContext` - which is a connection plus other local ambient context such as database, cancellation, etc. 

The first thing we need, then, is to create a connection. There are many ways to do this, but to
create a connection to the local default Redis instance:

``` c#
using var conn = RespConnection.Create();
// ...
```

This gives us a single socket-based connection. Usually a *connection* is long-lived and used for
a great many RESP operations, with the `using` here  closing socket eventually.

Once we have a connection, we can start using it immediately, via the default *context*, from
`.Context`. Usually, it is the *context* that we should be passing around, not a connection:
the context *has* a connection plus local ambient configuration. So:

``` c#
var ctx = conn.Context;
```

Once we have a *context*, we can use that to execute commands:

``` c#
ctx.SomeOperation(...);
```

But: what is `SomeOperation(...)`? ***That's up to you.***

### Defining commands

The RESPite libary only handles the RESP  layer - it doesn't add the methods associated with Redis
(don't worry: RESPite.Redis does that - we're not animals!). However, in the general case where you
want to add your own RESP methods, we can do exactly that. The easiest way is by letting the tools do
the work for us:

``` c#
static class MyCommands
{
    [RespOperation("incr")] // arg optional - it would assume "increment" if omitted
    public partial static int Increment(this in RespContext ctx, string key);

    [RespOperation("incrby")]
    public partial static int Increment(this in RespContext ctx, string key, int value);
}
```

Build-time tools will provide the implementation for us, including adding an `async` version. The code
for this isn't *difficult* - simply: it is *unnecessary*, since in most cases the intent can be clearly
understood. This avoids opportunities to fat-finger things (or get things wrong between the synchronous
and asynchronous versions).

We can now use:

``` c#
var x = ctx.Increment("mykey");
var y = await ctx.IncrementAsync("mykey", 42);
```

That's *basically* it. If you need more control over how non-trivial commands are formatted and parsed,
APIs exist for that. But for most common scenarios: that's all we need.

### Cancellation

Unusually, our `IncrementAsync` method *does not* have a `CancellationToken cancellationToken = default`
parameter; instead, cancellation is conveyed *in the context*. This also means that cancellation works
for *both* the synchronous and asynchronous versions! We can supply our own cancellation:

``` c#
var ctx = conn.Context.WithCancellationToken(request.CancellationToken);
// use ctx for commands
```

Now `ctx` is not just the *default* context - it has the cancellation token we supplied, and it is used
everywhere automatically! The `RespContext` type is cheap and allocation-free; it has no lifetime etc - it
is just a bundle of state required for RESP operations. We can freely `With...` them:

``` c#
var db = conn.Context.WithDatabase(4).WithCancellationToken(request.CancellationToken);
// use db for commands
```

If you're thinking "Wait - if `RespContext` carries cancellation, does `WithCancellationToken(...)` *replace*
the cancellation, or *combine* the two cancellations?", then: have a cookie. The answer is "replace", but we can also
combine multiple cancellations, noting that now we need to scope that to a *lifetime*:

``` c#
using var lifetime = db.WithDatabase(4).WithCombineCancellationToken(anotherCancellationToken);
// use lifetime.Context for commands
```

This will automatically do the most appropriate thing based on whether neither, one, or both tokens
are cancellable. We can do the same thing with a timeout:

``` c#
using var lifetime = db.WithCombineTimeout(TimeSpan.FromSeconds(5));
// use lifetime.Context for commands
```

Note that this timeout applies to the *lifetime*, not individual operations (i.e. if we loop forever
performing fast operations: it  will still cancel after five seconds). From the name
`WithCombineTimeout`, you can probably guess that this works *in addition to* the
existing cancellation state. Help yourself to another cookie.

## Summary

With the combination of `RespConnection` for the long-lived connection,
`RespContext` for the transient local configuration (via various `With*` methods),
and our automatically generated `[RespCommand]` methods: we can easily and
efficiently talk to a range of RESP databases.
