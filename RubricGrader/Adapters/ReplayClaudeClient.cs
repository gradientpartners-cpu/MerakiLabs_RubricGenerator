using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RubricGrader.Adapters;

/// <summary>
/// Loads a recorded JSON response keyed by (purpose, prompt) from a fixtures directory,
/// so the whole system runs deterministically with ZERO credentials (CLAUDE.md §4).
/// This is the README default.
///
/// Fixture layout: {root}/{purpose}/{promptKey}.json, where each file is an envelope
/// { purpose, prompt, response }. We deserialize only the "response" element into T;
/// keeping the prompt in the file makes fixtures human-readable + supports the audit
/// story. A prompt change shifts the key, so stale fixtures fail loud rather than
/// silently replaying the wrong answer.
/// </summary>
public sealed class ReplayClaudeClient : ILlmClient
{
    // Web defaults (camelCase, case-insensitive) + string enums, because the model
    // emits "high"/"low" etc. as strings, not integers.
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _root;

    public ReplayClaudeClient(string fixtureRoot) => _root = fixtureRoot;

    public async Task<T> CompleteStructuredAsync<T>(string purpose, string prompt, CancellationToken ct = default)
    {
        var path = Path.Combine(_root, purpose, KeyFor(prompt) + ".json");
        if (!File.Exists(path))
            throw new ReplayFixtureMissingException(purpose, prompt, path);

        var json = await File.ReadAllTextAsync(path, ct);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("response", out var response))
            throw new InvalidOperationException($"Replay fixture '{path}' has no 'response' field.");

        // Throws on shape mismatch -> callers translate to graceful degradation (§3.5).
        return response.Deserialize<T>(JsonOpts)
            ?? throw new InvalidOperationException($"Replay fixture '{path}' response was null.");
    }

    /// <summary>Stable fixture key for a prompt: first 16 hex of its SHA-256. Exposed so
    /// the seed tooling and tests name fixture files the same way the adapter reads them.</summary>
    public static string KeyFor(string prompt)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(prompt));
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }
}

/// <summary>Thrown when no recorded fixture matches (purpose, prompt) — the message tells
/// the operator exactly which file to record, so the zero-cred path stays debuggable.</summary>
public sealed class ReplayFixtureMissingException : Exception
{
    public ReplayFixtureMissingException(string purpose, string prompt, string expectedPath)
        : base($"No replay fixture for purpose '{purpose}'. Expected at '{expectedPath}'. " +
               $"Record it (run live once) or seed it by hand. Prompt key: {ReplayClaudeClient.KeyFor(prompt)}.")
    {
    }
}
