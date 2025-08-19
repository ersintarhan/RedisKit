using Xunit;

// Disable parallel test execution for integration tests
[assembly: CollectionBehavior(DisableTestParallelization = false, MaxParallelThreads = 4)]

// Integration tests should not run in parallel with each other
[assembly: TestFramework("Xunit.Sdk.TestFramework", "xunit.execution.{Platform}")]