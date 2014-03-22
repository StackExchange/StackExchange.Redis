The Dangers of Synchronous Continuations
===

This is more of a "don't do this" guide.

When you are using the `*Async` API of StackExchange.Redis, it will hand you either a `Task` or a `Task<T>` that represents your reply when it is available. From here you can do a few things:

- you can ignore it (if you are going to do that, you should specify `CommandFlags.FireAndForget`, to reduce overhead)
- you can asynchronously `await` it
- you can synchronously `.Wait()` it (or `Task.WaitAll` or `.Result`, which do the same)
- you can add a continuation with `.ContinueWith(...)`

The last one of these has overloads that allow you to control the behavior, including one or more overloads that accept a [`TaskContinuationOptions`][1]. And one of these options is `ExecuteSynchronously`.

To put it simply: **do not use `TaskContinuationOptions.ExecuteSynchronously` here. On other tasks, sure. But please please do not use this option on the task that StackExchange.Redis hands you. The reason
for this is that *if you do*, your continuation could end up interrupting the reader thread that is processing incoming redis data, and in a busy system blocking the reader will cause problems **very** quickly.

If you *can't* control this (and I strongly suggest you try to), then you can change `ConfigurationOptions.AllowSynchronousContinuations` to `false` when creating your `ConnectionMultiplexer` (or add `;syncCont=false` to the configuration string);
this will cause *all* tasks with continuations to be expressly moved *off* the reader thread and completed separately by the thread-pool. This *sounds* tempting, but in a busy system where the thread-pool is under heavy load, this can itself be problematic
(especially if the active workers are currently blocking waiting on responses that can't be actioned because the completions are stuck waiting for a worker - a deadlock). Unfortunately, at the current time
[there isn't much I can do about this](http://stackoverflow.com/q/22579206/23354), other than to advise you not to do it.

To be clear:

- `ContinueWith` by itself is fine
- and I'm sure there are times when `TaskContinuationOptions.ExecuteSynchronously` makes perfect sense on other tasks
- but please do not use `ContinueWith` with `TaskContinuationOptions.ExecuteSynchronously` on the tasks that StackExchange.Redis hands you

  [1]: http://msdn.microsoft.com/en-us/library/system.threading.tasks.taskcontinuationoptions(v=vs.110).aspx