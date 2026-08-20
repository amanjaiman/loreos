namespace Lore.Agent.Capture.Episodes;

/// <summary>One post-filter observation entering episode segmentation (v2-001). This is
/// the v2 pipeline's unit of intake — already through the sensitivity filter chain, so
/// everything here is safe to store locally and, later, to distill.</summary>
public sealed record CapturedObservation(
    DateTimeOffset At,
    string Executable,
    string Title,
    string Text,
    ContentType ContentType);

/// <summary>How many of an episode's observations were classified as one
/// <see cref="ContentType"/> (v2-008 R6.1). A count, not a flag, because proportion is the
/// signal: "Shopping (7), Reading (2)" tells the distiller what the stretch was mostly
/// about, where a bare set of kinds would not.</summary>
public sealed record ContentTypeTally(ContentType Type, int Count);

/// <summary>A contiguous stretch of related activity — the v2 unit of analysis. Episodes,
/// not individual dwells, go to the distiller; the record is bounded (representative
/// samples, not full text) so one episode can never blow up prompt cost, and it is
/// persisted locally so the user can audit exactly what the distiller saw.</summary>
/// <param name="ContentTypeMix">The episode's content-type breakdown, busiest kind first
/// (v2-008 R6.1). <see cref="ContentClassifier"/> already runs on every observation and the
/// result used to die at episode close; carrying the whole mix rather than one value is
/// deliberate, because an episode can legitimately span kinds — a shopping run that starts
/// with a review article is not either one of them.
///
/// <para>This field is NOT persisted by <c>ActivityStore</c>: the <c>episodes</c> table has
/// no column for it and the repository has no schema-migration path yet, so adding one
/// would break every existing install's inserts. Nothing downstream reads the mix off a
/// stored episode — the distiller only ever sees the live in-memory episode the builder
/// closed — so episodes reconstituted from storage carry an empty mix.</para></param>
public sealed record Episode(
    string Id,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    IReadOnlyList<string> Executables,
    IReadOnlyList<string> Titles,
    IReadOnlyList<string> Samples,
    int ObservationCount,
    IReadOnlyList<ContentTypeTally> ContentTypeMix);
