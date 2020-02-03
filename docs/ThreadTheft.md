# Thread Theft

If you're here because you followed a link in an exception and you just want your code to work,
the short version is: try adding the following *early on* in your application startup:

``` c#
ConnectionMultiplexer.SetFeatureFlag("preventthreadtheft", true);
```

and see if that fixes things. If you want more context as to what this is about - keep reading!

## What is thread theft?

Behind the scenes, for each connection to redis, StackExchange.Redis keeps a queue of the
commands that we've sent to redis that are awaiting a reply. As each reply comes in we look
at the next pending command (order is preserved, which keeps things simple), and we trigger
the "here's your result" API for that reply. For `async`/`await` code, this then leads to
your "continuation" becoming reactivated, which is how your code comes back to life when
an `await`-ed task gets completed. That's the simple version, but reality is a bit more
nuanced.

By *default*, when you trigger `TrySetResult` (etc) on a `Task`, the continuations are
invoked *synchronously*, i.e. the thread that is setting the result now goes on immediately
to run whatever it is that your continuation wanted. In our case, that would be very bad
as that would mean that the dedicated reader loop (that is meant to be processing results
from redis) is now running your application logic instead; this is **thread theft**, and
would exhibit as lots of timeouts with `rs: CompletePendingMessage` in the information (`rs`
is the **r**eader **s**tate; you shouldn't often observe it in the `CompletePendingMessage*`
step, because it is meant to be very fast; if you are seeing it often it probably means
that the reader is being hijacked when trying to set results).

To *avoid* this, we use the
`TaskCreationOptions.RunContinuationsAsynchronously` flag. What *this* does depends a little
on whether you have a `SynchronizationContext`. If you *don't* (common for console applications,
services, etc), then the TPL uses the standard thread-pool mechanisms to schedule the
continuation. If you *do* have a `SynchronizationContext` (common in UI applications
and web-servers), then its `Post` method is used instead; the `Post` method is *meant* to
be an asynchronous dispatch API. But... not all implementations are equal. Some
`SynchronizationContext` implementations treat `Post` as a synchronous invoke. This is true
in particular of `LegacyAspNetSynchronizationContext`, which is what you get if you
configure ASP.NET with:


``` xml
<add key="aspnet:UseTaskFriendlySynchronizationContext" value="false" />
```

or

```
<httpRuntime targetFramework="4.5" />
```

([citation](https://devblogs.microsoft.com/aspnet/all-about-httpruntime-targetframework))

In these scenarios, we would once again end up with the reader being stolen and used for
processing your application logic. This can doom any further `await`s to timeouts,
either temporarily (until the application logic chooses to release the thread), or permanently
(essentially deadlocking yourself).

To avoid this, the library includes an additional layer of mistrust; specifically, if
the `preventthreadtheft` feature flag is enabled, we will *pre-emptively* queue the
completions on the thread-pool. This is a little less efficient in the *default* case,
but *if and only if* you have a misbehaving `SynchronizationContext`, this is
both appropriate and necessary, and does not represent additional overhead.

The library will attempt to detect `LegacyAspNetSynchronizationContext` in particular,
but this is not always reliable. The flag is also available for manual use with other
similar scenarios.
