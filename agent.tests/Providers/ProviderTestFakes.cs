using System.Net.Http;
using Lore.Agent.Inference;
using Lore.Agent.Providers;

namespace Lore.Agent.Tests.Providers;

/// <summary>An <see cref="IHttpClientFactory"/> that always hands out one client.</summary>
internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly HttpClient _client;

    public StubHttpClientFactory(HttpClient client) => _client = client;

    public HttpClient CreateClient(string name) => _client;
}

/// <summary>A backend that returns a fixed completion or throws a fixed exception.</summary>
internal sealed class StubBackend : IInferenceBackend
{
    private readonly string? _result;
    private readonly Exception? _exception;

    private StubBackend(string? result, Exception? exception)
    {
        _result = result;
        _exception = exception;
    }

    public static StubBackend Returns(string? result) => new(result, null);

    public static StubBackend Throws(Exception exception) => new(null, exception);

    public Task<string?> CompleteAsync(InferenceRequest request, CancellationToken cancellationToken = default) =>
        _exception is not null ? Task.FromException<string?>(_exception) : Task.FromResult(_result);
}

/// <summary>A backend factory that records the provider it was asked to build and delegates
/// construction to a supplied function.</summary>
internal sealed class FakeBackendFactory : IProviderBackendFactory
{
    private readonly Func<ResolvedProvider, IInferenceBackend> _create;

    public FakeBackendFactory(Func<ResolvedProvider, IInferenceBackend> create) => _create = create;

    public ResolvedProvider? LastProvider { get; private set; }

    public IInferenceBackend Create(ResolvedProvider provider)
    {
        LastProvider = provider;
        return _create(provider);
    }
}
