// Some infrastructure is process-wide on purpose (data-change notifications, cache eviction wiring),
// so the test classes run one after another to keep them independent.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
