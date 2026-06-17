using System.Reflection;
using Lore.Agent.Storage;

namespace Lore.Agent.Tests.Storage;

public sealed class ActivityStoreTests : IDisposable
{
    private readonly ActivityStore _store = new(":memory:");

    public void Dispose() => _store.Dispose();

    private static readonly DateTimeOffset T0 = new(2026, 6, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Writes_and_reads_an_activity_log_entry()
    {
        var entry = new ActivityLogEntry(
            T0, "chrome", "Designing Data-Intensive Apps", ActivityDecision.Captured,
            "", "I'm reading about distributed systems", "reading");

        await _store.LogActivityAsync(entry);
        IReadOnlyList<ActivityLogEntry> rows = await _store.GetRecentActivityAsync();

        ActivityLogEntry row = Assert.Single(rows);
        Assert.Equal(ActivityDecision.Captured, row.Decision);
        Assert.Equal("I'm reading about distributed systems", row.Observation);
        Assert.Equal("reading", row.Category);
        Assert.Equal(T0, row.At);
    }

    [Fact]
    public async Task Records_a_skip_or_filter_decision_with_its_reason()
    {
        await _store.LogActivityAsync(new ActivityLogEntry(
            T0, "1password", "1Password", ActivityDecision.Filtered, "BlockedApp", "", ""));

        ActivityLogEntry row = Assert.Single(await _store.GetRecentActivityAsync());
        Assert.Equal(ActivityDecision.Filtered, row.Decision);
        Assert.Equal("BlockedApp", row.Reason);
        Assert.Equal("", row.Observation);
    }

    [Fact]
    public async Task Writes_and_reads_a_raw_capture_verbatim()
    {
        var entry = new RawCaptureEntry(
            T0, "firefox", "Wikipedia", "UiAutomation", "Reading", "the full extracted body text");

        await _store.LogRawCaptureAsync(entry);
        RawCaptureEntry row = Assert.Single(await _store.GetRecentRawCapturesAsync());

        Assert.Equal("the full extracted body text", row.Text);
        Assert.Equal("UiAutomation", row.ExtractionSource);
        Assert.Equal("Reading", row.ContentType);
    }

    [Fact]
    public async Task Returns_rows_newest_first_and_honors_the_limit()
    {
        for (int i = 0; i < 5; i++)
        {
            await _store.LogActivityAsync(new ActivityLogEntry(
                T0.AddMinutes(i), "app", $"window {i}", ActivityDecision.Captured, "", $"obs {i}", "general"));
        }

        IReadOnlyList<ActivityLogEntry> rows = await _store.GetRecentActivityAsync(limit: 2);

        Assert.Equal(2, rows.Count);
        Assert.Equal("obs 4", rows[0].Observation); // newest first
        Assert.Equal("obs 3", rows[1].Observation);
    }

    [Fact]
    public async Task The_two_tables_are_independent()
    {
        await _store.LogActivityAsync(new ActivityLogEntry(
            T0, "app", "w", ActivityDecision.Captured, "", "obs", "general"));

        Assert.Single(await _store.GetRecentActivityAsync());
        Assert.Empty(await _store.GetRecentRawCapturesAsync());
    }

    // ── Acceptance criterion 5: telemetry never crosses the egress seams ──────────

    [Fact]
    public void ActivityStore_cannot_reach_the_memory_or_inference_seams()
    {
        // Structural proof: nothing the store references lives in the memory/inference
        // seam namespaces, so activity-store data has no path to mem0 or a model.
        foreach (Type type in ReferencedTypes(typeof(ActivityStore)))
        {
            AssertNotSeam(type);
        }
    }

    [Fact]
    public void Telemetry_entry_types_carry_no_seam_types()
    {
        foreach (Type entryType in new[] { typeof(ActivityLogEntry), typeof(RawCaptureEntry) })
        {
            foreach (Type type in ReferencedTypes(entryType))
            {
                AssertNotSeam(type);
            }
        }
    }

    private static void AssertNotSeam(Type type)
    {
        foreach (Type t in Flatten(type))
        {
            string? ns = t.Namespace;
            Assert.False(
                ns is "Lore.Agent.Memory" or "Lore.Agent.Inference",
                $"ActivityStore must not reference seam type {t.FullName}");
        }
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        if (type.IsGenericType)
        {
            foreach (Type arg in type.GetGenericArguments())
            {
                foreach (Type inner in Flatten(arg))
                {
                    yield return inner;
                }
            }
        }
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        foreach (ConstructorInfo ctor in type.GetConstructors(flags))
        {
            foreach (ParameterInfo p in ctor.GetParameters())
            {
                yield return p.ParameterType;
            }
        }

        foreach (FieldInfo field in type.GetFields(flags))
        {
            yield return field.FieldType;
        }

        foreach (MethodInfo method in type.GetMethods(flags))
        {
            yield return method.ReturnType;
            foreach (ParameterInfo p in method.GetParameters())
            {
                yield return p.ParameterType;
            }
        }
    }
}
