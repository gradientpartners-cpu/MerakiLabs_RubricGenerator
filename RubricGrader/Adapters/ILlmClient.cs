namespace RubricGrader.Adapters;

/// <summary>
/// The ONLY seam allowed to talk to the LLM (CLAUDE.md §5 adapter discipline).
/// Everything else depends on this interface, never on HttpClient or the Anthropic API.
/// Two implementations: <see cref="ReplayClaudeClient"/> (recorded fixtures, zero
/// credentials — the README default) and <see cref="LiveClaudeClient"/> (real API).
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Return one structured response, deserialized into <typeparamref name="T"/>.
    /// Throws on unparseable output — callers translate that into graceful degradation
    /// (CLAUDE.md §3.5), never a fabricated result.
    /// </summary>
    /// <param name="purpose">Stable label (e.g. "grade-criterion", "generate-rubric")
    /// used for replay-fixture keying and audit.</param>
    Task<T> CompleteStructuredAsync<T>(string purpose, string prompt, CancellationToken ct = default);
}
