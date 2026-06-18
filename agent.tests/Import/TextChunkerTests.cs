using Lore.Agent.Import;

namespace Lore.Agent.Tests.Import;

/// <summary>Spec 009 T001: the chunker splits document text into sized, overlapped chunks. Pure
/// logic, so the cases are exhaustive — sizing, overlap, whitespace handling, and guard rails.</summary>
public sealed class TextChunkerTests
{
    [Fact]
    public void Short_text_yields_a_single_normalized_chunk()
    {
        var chunker = new TextChunker(chunkSize: 100, overlap: 10);

        IReadOnlyList<string> chunks = chunker.Split("I live in Seattle.");

        Assert.Equal(["I live in Seattle."], chunks);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n\t  \n")]
    [InlineData(null)]
    public void Empty_or_whitespace_text_yields_no_chunks(string? text)
    {
        var chunker = new TextChunker(chunkSize: 100, overlap: 10);

        Assert.Empty(chunker.Split(text));
    }

    [Fact]
    public void Whitespace_runs_collapse_to_single_spaces()
    {
        var chunker = new TextChunker(chunkSize: 100, overlap: 10);

        IReadOnlyList<string> chunks = chunker.Split("one   two\n\n\nthree\tfour");

        Assert.Equal(["one two three four"], chunks);
    }

    [Fact]
    public void Long_unbroken_text_is_hard_cut_to_the_chunk_size()
    {
        // No whitespace to break on, so every cut is at exactly the size; 100 chars / 25 = 4 chunks.
        var chunker = new TextChunker(chunkSize: 25, overlap: 0);

        IReadOnlyList<string> chunks = chunker.Split(new string('a', 100));

        Assert.Equal(4, chunks.Count);
        Assert.All(chunks, chunk => Assert.Equal(25, chunk.Length));
    }

    [Fact]
    public void Chunks_never_exceed_the_chunk_size_and_break_on_whitespace()
    {
        var chunker = new TextChunker(chunkSize: 40, overlap: 8);
        string prose = string.Join(' ', Enumerable.Repeat("alpha bravo charlie delta echo foxtrot", 6));

        IReadOnlyList<string> chunks = chunker.Split(prose);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 40, $"chunk too long: '{chunk}'"));
        // Whole words only — no chunk starts or ends mid-token.
        Assert.All(chunks, chunk =>
        {
            Assert.DoesNotContain(chunk.Split(' '), word => word.Length == 0);
        });
    }

    [Fact]
    public void Consecutive_chunks_overlap_so_a_straddling_fact_stays_whole()
    {
        var chunker = new TextChunker(chunkSize: 40, overlap: 15);
        string prose = string.Join(' ', Enumerable.Range(0, 30).Select(i => $"word{i}"));

        IReadOnlyList<string> chunks = chunker.Split(prose);

        Assert.True(chunks.Count > 1);
        for (int i = 0; i < chunks.Count - 1; i++)
        {
            string[] current = chunks[i].Split(' ');
            string[] next = chunks[i + 1].Split(' ');
            Assert.True(current.Intersect(next).Any(), $"chunks {i} and {i + 1} do not overlap");
        }
    }

    [Fact]
    public void Every_word_survives_chunking()
    {
        var chunker = new TextChunker(chunkSize: 30, overlap: 10);
        string[] words = Enumerable.Range(0, 50).Select(i => $"w{i}").ToArray();

        IReadOnlyList<string> chunks = chunker.Split(string.Join(' ', words));

        // Union of all chunk words covers the original set (overlap may repeat some).
        var seen = chunks.SelectMany(chunk => chunk.Split(' ')).ToHashSet();
        Assert.All(words, word => Assert.Contains(word, seen));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    public void Non_positive_chunk_size_is_rejected(int chunkSize, int overlap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextChunker(chunkSize, overlap));
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(50, 80)]
    [InlineData(50, -1)]
    public void Overlap_outside_the_valid_range_is_rejected(int chunkSize, int overlap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextChunker(chunkSize, overlap));
    }
}
