using FluentAssertions;
using RubricGrader.Domain;
using RubricGrader.Grading;
using Xunit;

namespace RubricGrader.Tests;

/// <summary>
/// Tests for pre-LLM redaction (CLAUDE.md §4). It must mask unambiguous PII and
/// self-introduced names, must NOT shred technical nouns, and must preserve turn ids +
/// speakers so downstream evidence validation still lines up with the redacted text.
/// </summary>
public class RedactTests
{
    private static IReadOnlyList<TranscriptTurn> One(string text)
        => new[] { new TranscriptTurn("t1", "candidate", text) };

    private static string Redact1(string text)
        => Redact.RedactTurns(One(text)).Single().Text;

    [Fact]
    public void MasksEmail()
        => Redact1("Reach me at jane.doe@example.com please")
            .Should().Be("Reach me at [EMAIL] please");

    [Fact]
    public void MasksPhone()
        => Redact1("Call +1 (555) 123-4567 anytime")
            .Should().Be("Call [PHONE] anytime");

    [Fact]
    public void MasksUrl()
        => Redact1("Portfolio at https://janedoe.dev/work here")
            .Should().Be("Portfolio at [URL] here");

    [Fact]
    public void MasksSelfIntroducedName_KeepsLeadIn()
        => Redact1("My name is Jane Doe and I led the team")
            .Should().Be("My name is [NAME] and I led the team");

    [Fact]
    public void DoesNotMaskTechnicalNouns()
        => Redact1("I scaled Postgres on Azure to a million users by 2019")
            .Should().Be("I scaled Postgres on Azure to a million users by 2019");

    [Fact]
    public void DoesNotMaskPlainLargeNumbers()
        => Redact1("We served 1000000 requests a day")
            .Should().Be("We served 1000000 requests a day");

    [Fact]
    public void PreservesTurnIdAndSpeaker()
    {
        var turns = new[] { new TranscriptTurn("t7", "interviewer", "Email me: a@b.com") };
        var redacted = Redact.RedactTurns(turns).Single();

        redacted.TurnId.Should().Be("t7");
        redacted.Speaker.Should().Be("interviewer");
        redacted.Text.Should().Be("Email me: [EMAIL]");
    }

    [Fact]
    public void RedactedText_IsStillValidatable()
    {
        // the LLM sees redacted text, so it can only cite redacted text — and Validate
        // runs against the same redacted turns. This proves the two stay consistent.
        var redacted = Redact.RedactTurns(One("My name is Jane and I owned the migration"));
        var grade = new CriterionGrade("ownership", false, 4,
            "I owned the migration", "t1", Confidence.High);

        Validate.ValidateEvidence(grade, redacted).Should().Be(ValidationStatus.Valid);
    }
}
