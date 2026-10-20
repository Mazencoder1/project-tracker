using Xunit;

// The integration tests share one scratch SQL Server database (templates, settings, the admin account),
// so they must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
