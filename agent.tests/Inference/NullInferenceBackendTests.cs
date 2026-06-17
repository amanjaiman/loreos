using Lore.Agent.Inference;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lore.Agent.Tests.Inference;

public sealed class NullInferenceBackendTests
{
    [Fact]
    public async Task Produces_no_completion_until_a_real_backend_is_wired()
    {
        var backend = new NullInferenceBackend(NullLogger<NullInferenceBackend>.Instance);

        string? first = await backend.CompleteAsync(new InferenceRequest("system", "user"));
        string? second = await backend.CompleteAsync(new InferenceRequest("system", "user"));

        Assert.Null(first);
        Assert.Null(second);
    }

    [Fact]
    public void Constructor_rejects_a_null_logger()
    {
        Assert.Throws<ArgumentNullException>(() => new NullInferenceBackend(null!));
    }
}
