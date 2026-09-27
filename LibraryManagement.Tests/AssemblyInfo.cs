using Xunit;

// Disable parallel test execution because AuthService.CurrentUser is a static application state
[assembly: CollectionBehavior(DisableTestParallelization = true)]
