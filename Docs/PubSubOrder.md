Pub/Sub Message Order
===

When using the pub/sub API, there is a decision to be made as to whether messages from the same connection should be processed *sequentially* vs *concurrently*.

Processing them sequentially means that you don't need to worry (quite as much) about thread-safety, and means that you preserve the order of events -
they will be processed in exactly the same order in which they are received (via a queue) - but as a consequence it means that messages can delay each-other.

The other option is *concurrent* processing. This makes **no specific guarantees** about the order in which work gets processed, and your code is entirely
responsible for ensuring that concurrent messages don't corrupt your internal state - but it can be significantly faster and much more scalable.
This works *particularly* well if messages are generally unrelated.

For safety, **the default is sequential**; however, it is strongly recommended that you use concurrent processing whenever possible. This is a simple change:

```C#
multiplexer.PreserveAsyncOrder = false;
```

The reason that this is not a *configuration* option is that whether it is appropriate to do this depends *entirely* on the code that is subscribing to
messages.