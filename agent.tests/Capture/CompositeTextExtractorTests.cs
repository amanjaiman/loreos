using Lore.Agent.Capture;

namespace Lore.Agent.Tests.Capture;

public sealed class CompositeTextExtractorTests
{
    private sealed class StubExtractor : ITextExtractor
    {
        private readonly ExtractedText _result;

        public StubExtractor(ExtractedText result) => _result = result;

        public int Calls { get; private set; }

        public Task<ExtractedText> ExtractAsync(WindowSnapshot window, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }

    private static readonly WindowSnapshot Window = new(1, "app.exe", "doc");

    private static StubExtractor Uia(string text) => new(new ExtractedText(text, ExtractionSource.UiAutomation));

    private static StubExtractor Ocr(string text) => new(new ExtractedText(text, ExtractionSource.Ocr));

    private static StubExtractor Nothing() => new(ExtractedText.Empty);

    [Fact]
    public async Task Uses_the_first_extractor_when_it_returns_text()
    {
        StubExtractor uia = Uia("hello from uia");
        StubExtractor ocr = Ocr("hello from ocr");
        var composite = new CompositeTextExtractor(uia, ocr);

        ExtractedText result = await composite.ExtractAsync(Window);

        Assert.Equal("hello from uia", result.Text);
        Assert.Equal(ExtractionSource.UiAutomation, result.Source);
        Assert.Equal(1, uia.Calls);
        Assert.Equal(0, ocr.Calls); // OCR is never reached when UIA has text
    }

    [Fact]
    public async Task Falls_back_to_the_next_extractor_when_the_first_is_empty()
    {
        StubExtractor uia = Nothing();
        StubExtractor ocr = Ocr("recognized text");
        var composite = new CompositeTextExtractor(uia, ocr);

        ExtractedText result = await composite.ExtractAsync(Window);

        Assert.Equal("recognized text", result.Text);
        Assert.Equal(ExtractionSource.Ocr, result.Source);
        Assert.Equal(1, uia.Calls);
        Assert.Equal(1, ocr.Calls);
    }

    [Fact]
    public async Task Returns_empty_when_every_extractor_is_empty()
    {
        var composite = new CompositeTextExtractor(Nothing(), Nothing());

        ExtractedText result = await composite.ExtractAsync(Window);

        Assert.True(result.IsEmpty);
        Assert.Equal(ExtractionSource.None, result.Source);
    }

    [Fact]
    public async Task Whitespace_only_text_counts_as_empty_and_falls_through()
    {
        StubExtractor uia = new(new ExtractedText("   \n\t ", ExtractionSource.UiAutomation));
        StubExtractor ocr = Ocr("real text");
        var composite = new CompositeTextExtractor(uia, ocr);

        ExtractedText result = await composite.ExtractAsync(Window);

        Assert.Equal("real text", result.Text);
        Assert.Equal(ExtractionSource.Ocr, result.Source);
    }

    [Fact]
    public async Task An_empty_window_is_never_extracted()
    {
        StubExtractor uia = Uia("should not run");
        var composite = new CompositeTextExtractor(uia);

        ExtractedText result = await composite.ExtractAsync(WindowSnapshot.None);

        Assert.True(result.IsEmpty);
        Assert.Equal(0, uia.Calls);
    }

    [Fact]
    public async Task Cancellation_is_observed_before_running_extractors()
    {
        StubExtractor uia = Uia("text");
        var composite = new CompositeTextExtractor(uia);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => composite.ExtractAsync(Window, cts.Token));
        Assert.Equal(0, uia.Calls);
    }

    [Fact]
    public void Constructor_rejects_no_extractors()
    {
        Assert.Throws<ArgumentException>(() => new CompositeTextExtractor());
    }

    [Fact]
    public void Constructor_rejects_a_null_extractor()
    {
        Assert.Throws<ArgumentException>(() => new CompositeTextExtractor(Uia("x"), null!));
    }
}
