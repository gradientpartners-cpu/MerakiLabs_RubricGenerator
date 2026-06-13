using FluentAssertions;
using RubricGrader.Domain;
using RubricGrader.Grading;
using Xunit;

namespace RubricGrader.Tests;

/// <summary>
/// Tests for the anti-hallucination boundary (CLAUDE.md §3.2). The negative cases are
/// the point: fabricated quote, paraphrase, wrong turn, and uncited grades must all be
/// rejected — only cosmetic (whitespace/quote/case) variance is allowed to pass.
/// </summary>
public class ValidateTests
{
    private static IReadOnlyList<TranscriptTurn> Turns(params (string id, string text)[] turns)
        => turns.Select(t => new TranscriptTurn(t.id, "candidate", t.text)).ToList();

    private static CriterionGrade Grade(
        string? evidence,
        string? turnId,
        bool abstained = false,
        Confidence? confidence = Confidence.High,
        int? score = 4)
        => new("communication", abstained, abstained ? null : score, evidence, turnId, confidence);

    // ---- the happy path -----------------------------------------------------------

    [Fact]
    public void ExactQuote_IsValid()
    {
        var turns = Turns(("t1", "I led the migration to a sharded Postgres cluster."));
        var grade = Grade("led the migration to a sharded Postgres cluster", "t1");

        Validate.ValidateEvidence(grade, turns).Should().Be(ValidationStatus.Valid);
    }

    // ---- the caught hallucinations (the high-signal cases) ------------------------

    [Fact]
    public void FabricatedQuote_IsEvidenceNotFound()
    {
        var turns = Turns(("t1", "I led the migration to a sharded Postgres cluster."));
        var grade = Grade("I architected the entire payments platform single-handedly", "t1");

        Validate.ValidateEvidence(grade, turns).Should().Be(ValidationStatus.EvidenceNotFound);
    }

    [Fact]
    public void ParaphrasedQuote_IsEvidenceNotFound()
    {
        // semantically equivalent, not verbatim — must NOT pass (no fuzzy matching)
        var turns = Turns(("t1", "I led the migration to a sharded Postgres cluster."));
        var grade = Grade("led the database migration to a sharded cluster", "t1");

        Validate.ValidateEvidence(grade, turns).Should().Be(ValidationStatus.EvidenceNotFound);
    }

    [Fact]
    public void QuoteFromWrongTurn_IsEvidenceNotFound()
    {
        // the span exists in the transcript, but not in the CITED turn
        var turns = Turns(
            ("t1", "I led the migration to a sharded Postgres cluster."),
            ("t2", "We had a few outages early on."));
        var grade = Grade("led the migration to a sharded Postgres cluster", "t2");

        Validate.ValidateEvidence(grade, turns).Should().Be(ValidationStatus.EvidenceNotFound);
    }

    [Fact]
    public void CitedTurnThatDoesNotExist_IsEvidenceNotFound()
    {
        var turns = Turns(("t1", "I led the migration."));
        var grade = Grade("I led the migration.", "t99");

        Validate.ValidateEvidence(grade, turns).Should().Be(ValidationStatus.EvidenceNotFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingEvidence_OnAConfidentGrade_IsEvidenceNotFound(string? evidence)
    {
        var turns = Turns(("t1", "I led the migration."));
        var grade = Grade(evidence, "t1");

        Validate.ValidateEvidence(grade, turns).Should().Be(ValidationStatus.EvidenceNotFound);
    }

    [Fact]
    public void MissingTurnId_OnAConfidentGrade_IsEvidenceNotFound()
    {
        var turns = Turns(("t1", "I led the migration."));
        var grade = Grade("I led the migration.", turnId: null);

        Validate.ValidateEvidence(grade, turns).Should().Be(ValidationStatus.EvidenceNotFound);
    }

    // ---- cosmetic variance is tolerated (and only cosmetic) -----------------------

    [Fact]
    public void WhitespaceVariance_IsValid()
    {
        var turns = Turns(("t1", "I led   the migration\n   to a sharded cluster."));
        var grade = Grade("led the migration to a sharded cluster", "t1");

        Validate.ValidateEvidence(grade, turns).Should().Be(ValidationStatus.Valid);
    }

    [Fact]
    public void SmartQuoteVariance_IsValid()
    {
        // turn uses a curly apostrophe; evidence uses a straight one
        var turns = Turns(("t1", "I didn\u2019t ship it until the tests were green."));
        var grade = Grade("didn't ship it until the tests were green", "t1");

        Validate.ValidateEvidence(grade, turns).Should().Be(ValidationStatus.Valid);
    }

    [Fact]
    public void CaseVariance_IsValid()
    {
        var turns = Turns(("t1", "I Led The Migration."));
        var grade = Grade("led the migration", "t1");

        Validate.ValidateEvidence(grade, turns).Should().Be(ValidationStatus.Valid);
    }

    [Fact]
    public void EmDashVariance_IsValid()
    {
        var turns = Turns(("t1", "The rollout was incremental \u2014 one tenant at a time."));
        var grade = Grade("incremental - one tenant at a time", "t1");

        Validate.ValidateEvidence(grade, turns).Should().Be(ValidationStatus.Valid);
    }

    // ---- routing precedence (uncertainty wins over evidence checks) ---------------

    [Fact]
    public void Abstention_RoutesToAbstained_EvenIfEvidenceWouldValidate()
    {
        var turns = Turns(("t1", "I led the migration."));
        var grade = new CriterionGrade("communication", Abstained: true, Score: null,
            Evidence: "I led the migration.", TurnId: "t1", Confidence: Confidence.High);

        Validate.ValidateEvidence(grade, turns).Should().Be(ValidationStatus.Abstained);
    }

    [Fact]
    public void LowConfidence_RoutesToLowConfidence_EvenIfEvidenceWouldValidate()
    {
        var turns = Turns(("t1", "I led the migration."));
        var grade = Grade("I led the migration.", "t1", confidence: Confidence.Low);

        Validate.ValidateEvidence(grade, turns).Should().Be(ValidationStatus.LowConfidence);
    }

    // ---- Normalize unit behaviour -------------------------------------------------

    [Fact]
    public void Normalize_CollapsesWhitespace_TrimsAndLowercases()
    {
        Validate.Normalize("  Led   THE\n migration  ").Should().Be("led the migration");
    }

    [Fact]
    public void Normalize_EmptyOrNull_IsEmpty()
    {
        Validate.Normalize("").Should().BeEmpty();
        Validate.Normalize(null!).Should().BeEmpty();
    }
}
