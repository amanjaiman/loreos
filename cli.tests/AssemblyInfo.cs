// The end-to-end exit-code tests (ExitCodeTests) redirect Console.Out/Error to capture CLI output,
// which is process-global state. Disabling xUnit's parallelization keeps those redirects from
// racing other tests. The CLI suite is small and fast, so the cost is negligible.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
