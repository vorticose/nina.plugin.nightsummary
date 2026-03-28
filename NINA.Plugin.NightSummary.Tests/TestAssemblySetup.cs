using Xunit;

// Settings.Default is a shared singleton. Running test classes in parallel
// causes races where one class's constructor resets settings mid-test in
// another class. Disable parallelism at the assembly level to prevent this.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
