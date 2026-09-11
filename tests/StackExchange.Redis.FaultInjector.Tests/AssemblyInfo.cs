using Xunit;

// One cluster, one injector, and scenarios that mutate *cluster* state - node exclusions, maintenance mode,
// endpoint policies. Running classes in parallel therefore breaks two ways at once: they interfere semantically
// (one scenario's teardown restores nodes another is relying on, giving errors like "Need at least 2 nodes with
// shards"), and they starve each other, because the injector processes actions through a queue. Measured: the
// first whole-suite run failed 20 of 26, almost all of them waiting on a queued setup.
//
// So this tier is strictly serial. It costs wall-clock - the scenarios are minutes each - and buys results that
// mean something.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
