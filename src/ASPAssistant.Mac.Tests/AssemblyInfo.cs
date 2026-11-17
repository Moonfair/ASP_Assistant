using Xunit;

// MaaFramework's managed native-library resolver uses a process-wide mutable cache.
// Concurrent initialization can throw when two test classes load MaaFramework together.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
