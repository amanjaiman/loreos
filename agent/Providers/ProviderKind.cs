namespace Lore.Agent.Providers;

/// <summary>The four provider types Lore supports (spec 004). Selection is a pure
/// function of <c>provider.type</c> — there is no priority chain and no implicit
/// default (constitution §3.2; plan "config is the whole truth").</summary>
public enum ProviderKind
{
    /// <summary>Anthropic Claude messages API. Key + model id.</summary>
    Anthropic,

    /// <summary>OpenAI chat completions. Key + model id.</summary>
    OpenAi,

    /// <summary>Google Gemini. User-supplied key + model id.</summary>
    Gemini,

    /// <summary>Any OpenAI-compatible chat-completions endpoint reached by base URL
    /// (Ollama, LM Studio, llama.cpp server, vLLM, OpenRouter, Together, Groq, …).
    /// The key is optional so keyless local servers work.</summary>
    OpenAiCompatible,
}
