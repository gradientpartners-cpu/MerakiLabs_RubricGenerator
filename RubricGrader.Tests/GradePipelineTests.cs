using System.Text.Json;
using FluentAssertions;
using RubricGrader.Adapters;
using RubricGrader.Domain;
using RubricGrader.Grading;
using Xunit;

namespace RubricGrader.Tests;

/// <summary>
/// End-to-end grade-pipeline tests run through the REAL replay adapter with ZERO
/// database and ZERO credentials (CLAUDE.md §4–§5). They prove the central claims:
/// the composite is deterministic and computed in code, and every non-happy path
/// (hallucination, abstention, low confidence, LLM error) routes to a human instead of
/// fabricating a score.
/// </summary>
public class GradePipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pipeline-" + Guid.NewGuid().ToString("N"));

    private static readonly IReadOnlyList<TranscriptTurn> Turns = new[]
    {
        new TranscriptTurn("t1", "candidate", "I led the migration to a sharded cluster."),
        new TranscriptTurn("t2", "candidate", "We cut p99 latency in half after the rollout."),
    };

    private static RubricCriterion Crit(string key, double weight)
        => new(key, key, weight, new Dictionary<int, string> { [1] = "weak", [3] = "ok", [5] = "strong" });

    private GradeRequest Request(params RubricCriterion[] criteria)
    {
        var rubric = new RubricVersion("rub-1", "tenant-1", 1, RubricStatus.Approved, criteria);
        return new GradeRequest("eval-1", "tenant-1", "art-1", rubric, Turns, "replay");
    }

    // Seed a fixture exactly the way the adapter will look it up: the pipeline redacts
    // turns BEFORE building the prompt, so we must too, or the key won't match.
    private void SeedGrade(RubricCriterion criterion, object response)
    {
        var redacted = Redact.RedactTurns(Turns);
        var prompt = GradingPrompt.Build(criterion, redacted);
        var dir = Path.Combine(_root, GradePipeline.Purpose);
        Directory.CreateDirectory(dir);
        var envelope = new { purpose = GradePipeline.Purpose, prompt, response };
        File.WriteAllText(
            Path.Combine(dir, ReplayClaudeClient.KeyFor(prompt) + ".json"),
            JsonSerializer.Serialize(envelope));
    }

    private static object ValidGrade(string key, int score, string evidence, string turnId, string confidence = "high")
        => new { criterionKey = key, abstained = false, score, evidence, turnId, confidence };

    // Like SeedGrade, but for a caller-supplied transcript — lets a test exercise a turn
    // whose exact wording matters (e.g. a paraphrase that is NOT a verbatim substring).
    private void SeedGradeForTurns(IReadOnlyList<TranscriptTurn> turns, RubricCriterion criterion, object response)
    {
        var redacted = Redact.RedactTurns(turns);
        var prompt = GradingPrompt.Build(criterion, redacted);
        var dir = Path.Combine(_root, GradePipeline.Purpose);
        Directory.CreateDirectory(dir);
        var envelope = new { purpose = GradePipeline.Purpose, prompt, response };
        File.WriteAllText(
            Path.Combine(dir, ReplayClaudeClient.KeyFor(prompt) + ".json"),
            JsonSerializer.Serialize(envelope));
    }

    private ReplayClaudeClient Llm => new(_root);

    // ---- the WOW test: determinism proof -----------------------------------------

    [Fact]
    public async Task DeterminismProof_SameArtifactGradedTwice_IdenticalComposite()
    {
        var comm = Crit("comm", 0.6);
        var tech = Crit("tech", 0.4);
        SeedGrade(comm, ValidGrade("comm", 4, "led the migration", "t1"));
        SeedGrade(tech, ValidGrade("tech", 5, "cut p99 latency in half", "t2"));
        var req = Request(comm, tech);

        var first = await GradePipeline.RunAsync(req, Llm);
        var second = await GradePipeline.RunAsync(req, Llm);

        // 0.6*4 + 0.4*5 = 4.4, and it is byte-for-byte reproducible.
        first.State.Should().Be(EvaluationState.Graded);
        first.CompositeScore.Should().BeApproximately(4.4, 1e-9);
        second.CompositeScore.Should().Be(first.CompositeScore);
    }

    // ---- non-happy path 1: caught hallucination ----------------------------------

    [Fact]
    public async Task FabricatedEvidence_FlagsCriterion_AndRoutesToHuman()
    {
        var comm = Crit("comm", 1.0);
        SeedGrade(comm, ValidGrade("comm", 5, "I single-handedly built the whole platform", "t1"));
        var req = Request(comm);

        var result = await GradePipeline.RunAsync(req, Llm);

        result.Results.Single().ValidationStatus.Should().Be(ValidationStatus.EvidenceNotFound);
        result.Results.Single().Score.Should().BeNull();   // rejected score never kept
        result.CompositeScore.Should().BeNull();
        result.State.Should().Be(EvaluationState.NeedsHumanGrading);
    }

    // ---- the SUBTLE caught hallucination: a faithful paraphrase still fails -------

    [Fact]
    public async Task ParaphrasedEvidence_SemanticallyTrue_ButNotVerbatim_IsCaught()
    {
        // The realistic failure mode is NOT a wild fabrication — it's the model citing a
        // span that is *true and on-topic* but reworded. Here the turn says the latency
        // cut "from 800ms to 300ms"; the model cites "cut p99 latency in half" — an
        // accurate paraphrase that is NOT a verbatim substring of the turn. Strict
        // substring validation (CLAUDE.md §3.2: no fuzzy/semantic matching) must reject
        // it. This is precisely WHY fuzzy matching was refused: it would let the model
        // paraphrase its evidence and still pass, defeating the safeguard.
        var turns = new[]
        {
            new TranscriptTurn("t1", "candidate",
                "We moved to a sharded cluster and cut p99 latency from 800ms to 300ms."),
        };
        var tech = Crit("tech", 1.0);
        SeedGradeForTurns(turns, tech,
            ValidGrade("tech", 5, "cut p99 latency in half", "t1"));   // paraphrase, not verbatim

        var rubric = new RubricVersion("rub-1", "tenant-1", 1, RubricStatus.Approved, new[] { tech });
        var req = new GradeRequest("eval-1", "tenant-1", "art-1", rubric, turns, "replay");

        var result = await GradePipeline.RunAsync(req, Llm);

        result.Results.Single().ValidationStatus.Should().Be(ValidationStatus.EvidenceNotFound);
        result.Results.Single().Score.Should().BeNull();
        result.CompositeScore.Should().BeNull();
        result.State.Should().Be(EvaluationState.NeedsHumanGrading);
    }

    // ---- non-happy path 2: abstention & low confidence ---------------------------

    [Fact]
    public async Task Abstention_RoutesToHuman()
    {
        var comm = Crit("comm", 1.0);
        SeedGrade(comm, new { criterionKey = "comm", abstained = true, score = (int?)null,
            evidence = (string?)null, turnId = (string?)null, confidence = "low" });
        var req = Request(comm);

        var result = await GradePipeline.RunAsync(req, Llm);

        result.Results.Single().ValidationStatus.Should().Be(ValidationStatus.Abstained);
        result.State.Should().Be(EvaluationState.NeedsHumanGrading);
        result.CompositeScore.Should().BeNull();
    }

    [Fact]
    public async Task LowConfidence_RoutesToHuman_EvenWithValidEvidence()
    {
        var comm = Crit("comm", 1.0);
        SeedGrade(comm, ValidGrade("comm", 4, "led the migration", "t1", confidence: "low"));
        var req = Request(comm);

        var result = await GradePipeline.RunAsync(req, Llm);

        result.Results.Single().ValidationStatus.Should().Be(ValidationStatus.LowConfidence);
        result.State.Should().Be(EvaluationState.NeedsHumanGrading);
        result.CompositeScore.Should().BeNull();
    }

    // ---- non-happy path 3: LLM unavailable / unparseable -------------------------

    [Fact]
    public async Task LlmUnavailable_RoutesToHuman_NeverFabricates()
    {
        // No fixture seeded at all -> ReplayClaudeClient throws -> pipeline records Errored.
        var comm = Crit("comm", 1.0);
        var req = Request(comm);

        var result = await GradePipeline.RunAsync(req, Llm);

        result.Results.Single().ValidationStatus.Should().Be(ValidationStatus.Errored);
        result.Results.Single().Score.Should().BeNull();
        result.State.Should().Be(EvaluationState.NeedsHumanGrading);
        result.CompositeScore.Should().BeNull();
    }

    // ---- partial: one good, one bad still routes to human ------------------------

    [Fact]
    public async Task OneValidOneEvidenceNotFound_RoutesToHuman()
    {
        var comm = Crit("comm", 0.5);
        var tech = Crit("tech", 0.5);
        SeedGrade(comm, ValidGrade("comm", 4, "led the migration", "t1"));
        SeedGrade(tech, ValidGrade("tech", 5, "fabricated quote that is not present", "t2"));
        var req = Request(comm, tech);

        var result = await GradePipeline.RunAsync(req, Llm);

        result.Results.Should().Contain(r => r.ValidationStatus == ValidationStatus.Valid);
        result.Results.Should().Contain(r => r.ValidationStatus == ValidationStatus.EvidenceNotFound);
        result.State.Should().Be(EvaluationState.NeedsHumanGrading);
        result.CompositeScore.Should().BeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
