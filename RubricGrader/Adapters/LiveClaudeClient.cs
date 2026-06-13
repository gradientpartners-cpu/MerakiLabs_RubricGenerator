namespace RubricGrader.Adapters;

/// <summary>
/// Thin wrapper over the Anthropic Messages API via the injected <see cref="HttpClient"/>
/// (CLAUDE.md §2/§5). Opt-in via Llm:Backend=live + ANTHROPIC_API_KEY. The only type
/// permitted to perform LLM HTTP.
/// </summary>
public sealed class LiveClaudeClient : ILlmClient
{
    private readonly HttpClient _http;

    public LiveClaudeClient(HttpClient http) => _http = http;

    public Task<T> CompleteStructuredAsync<T>(string purpose, string prompt, CancellationToken ct = default)
        // TODO: POST a structured/tool-constrained request -> validate/deserialize into T.
        => throw new NotImplementedException();
}
