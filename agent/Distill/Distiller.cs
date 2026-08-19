using Lore.Agent.Capture;
using Lore.Agent.Capture.Episodes;
using Lore.Agent.Inference;
using Microsoft.Extensions.Logging;

namespace Lore.Agent.Distill;

/// <summary>The one inference call per closed episode (v2-001): ask the user's model
/// what durable fact the episode supports, expecting "none" most of the time. Returns
/// <c>null</c> when the model's output was unusable — the caller records a
/// <c>distill_failed</c> decision and moves on; the loop never crashes on a bad
/// completion.</summary>
public sealed class Distiller
{
    private readonly IInferenceBackend _backend;
    private readonly LiveCaptureSettings _settings;
    private readonly ILogger<Distiller> _logger;

    /// <param name="backend">The user's configured model.</param>
    /// <param name="settings">The live capture settings, read for the "how much detail" stop and
    /// its statement cap (v2-008 R1.3). Taken from here rather than from a startup-bound options
    /// object because there is no longer one to inject — every capture value is live (R2) — and
    /// because a detail change made in Settings must reach the very next episode without a
    /// restart, which would drop the episode currently open.</param>
    /// <param name="logger">Receives the call and parse failures; neither is fatal.</param>
    public Distiller(IInferenceBackend backend, LiveCaptureSettings settings, ILogger<Distiller> logger)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        _backend = backend;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Distill one episode into candidate facts: empty = nothing durable
    /// (the normal case); <c>null</c> = the model call or parse failed.</summary>
    public async Task<IReadOnlyList<CandidateFact>?> DistillAsync(
        Episode episode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(episode);

        // One read for the whole episode (v2-008 R2): the directive and the cap it is paired with
        // must come from the same snapshot, or a PATCH landing between two reads could tell the
        // model to lead with specifics and then hold it to 120 characters.
        CaptureSnapshot snapshot = _settings.Current;

        string? raw;
        try
        {
            raw = await _backend
                .CompleteAsync(
                    DistillPrompt.Build(episode, snapshot.Detail, snapshot.StatementMaxChars),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // a provider outage is a distill_failed decision, not a crash
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex, "distillation call failed for episode {Id}", episode.Id);
            return null;
        }

        IReadOnlyList<CandidateFact>? facts = DistillParser.Parse(raw);
        if (facts is null)
        {
            _logger.LogWarning("distiller returned unusable output for episode {Id}", episode.Id);
        }

        return facts;
    }
}
