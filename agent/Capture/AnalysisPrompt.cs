using System.Globalization;
using Lore.Agent.Inference;

namespace Lore.Agent.Capture;

/// <summary>Builds the analysis prompt — a trimmed port of v1's <c>analysis.txt</c> — that
/// turns a window's filtered title and text into a JSON observation. The system prompt
/// asks for ONE first-person sentence plus a category, and gives the model an explicit way
/// to say "nothing worth remembering" (an empty observation), which the parser treats as a
/// skip. The user content is bounded so a huge page can't blow up token cost.</summary>
public static class AnalysisPrompt
{
    private const int MaxTextChars = 4000;

    private const string System =
        "You are Lore's capture analyst. You are given the title and visible text of a " +
        "window the user is looking at. Write ONE concise, first-person observation of what " +
        "the user is doing or learning, phrased as if the user wrote it (for example, " +
        "\"I'm researching mechanical keyboards\" or \"I'm reading about the French " +
        "Revolution\"). Summarize the activity; never quote raw screen text. Also assign a " +
        "short, lowercase category such as reading, research, shopping, coding, messaging, " +
        "or travel. Respond with ONLY a JSON object of the form " +
        "{\"observation\": \"...\", \"category\": \"...\"}. If the content is not meaningful " +
        "enough to remember, respond with {\"observation\": \"\", \"category\": \"\"}.";

    /// <summary>Build the request for the given filtered title and text.</summary>
    public static InferenceRequest Build(string? title, string? text)
    {
        string safeTitle = string.IsNullOrWhiteSpace(title) ? "(untitled)" : title.Trim();
        string body = (text ?? string.Empty).Trim();
        if (body.Length > MaxTextChars)
        {
            body = body[..MaxTextChars];
        }

        string user = string.Format(
            CultureInfo.InvariantCulture, "Title: {0}\n\nVisible text:\n{1}", safeTitle, body);
        return new InferenceRequest(System, user);
    }
}
