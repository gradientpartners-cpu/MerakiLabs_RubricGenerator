using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RubricGrader.Domain;
using RubricGrader.Repository;
using RubricGrader.Worker;
using Xunit;

namespace RubricGrader.Tests;

/// <summary>
/// Both stores must behave identically: persist a grade + its criteria, reconstruct the
/// full audit chain (composite -> criterion score -> cited evidence + status -> rubric
/// version), and block cross-tenant reads. We run the SAME assertions against the
/// in-memory store and the EF store (EF Core InMemory provider) to prove the swap-in is
/// faithful and the default path stays DB-free.
/// </summary>
public class EvaluationStoreTests
{
    // (sink, reader, rubricStore) triples for each implementation.
    public static IEnumerable<object[]> Stores()
    {
        var mem = new InMemoryEvaluationStore();
        yield return new object[] { mem, mem, mem };

        var ef = new EfEvaluationStore(new TestDbContextFactory());
        yield return new object[] { ef, ef, ef };
    }

    private static RubricVersion Rubric(string tenant) => new(
        "rub-1", tenant, 3, RubricStatus.Approved,
        new[]
        {
            new RubricCriterion("comm", "Communication", 0.6, new Dictionary<int, string> { [5] = "clear" }),
            new RubricCriterion("tech", "Technical depth", 0.4, new Dictionary<int, string> { [5] = "deep" }),
        });

    private static EvaluationResult Result(string tenant) => new(
        "eval-1", tenant, "art-1", "rub-1", EvaluationState.Graded, 4.4,
        new[]
        {
            new CriterionResult("comm", 4, Confidence.High, "t1", "led the migration", ValidationStatus.Valid),
            new CriterionResult("tech", 5, Confidence.High, "t2", "cut p99 latency in half", ValidationStatus.Valid),
        },
        "replay", "grade-v1");

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task ReconstructsTheFullAuditChain(
        IEvaluationResultSink sink, IEvaluationReader reader, IRubricStore rubricStore)
    {
        const string tenant = "tenant-1";
        await rubricStore.SaveRubricAsync(Rubric(tenant));
        await sink.SaveAsync(Result(tenant));

        var chain = await reader.GetAuditChainAsync(tenant, "eval-1");

        chain.Should().NotBeNull();
        chain!.CompositeScore.Should().BeApproximately(4.4, 1e-9);
        chain.State.Should().Be("Graded");

        // traced back to the rubric version
        chain.RubricVersion.Id.Should().Be("rub-1");
        chain.RubricVersion.Version.Should().Be(3);

        // each criterion: score -> cited evidence -> validation status, with rubric label+weight
        var comm = chain.Criteria.Single(c => c.CriterionKey == "comm");
        comm.Label.Should().Be("Communication");
        comm.Weight.Should().Be(0.6);
        comm.Score.Should().Be(4);
        comm.EvidenceTurnId.Should().Be("t1");
        comm.EvidenceSpan.Should().Be("led the migration");
        comm.ValidationStatus.Should().Be("Valid");
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task CrossTenantRead_IsBlocked(
        IEvaluationResultSink sink, IEvaluationReader reader, IRubricStore rubricStore)
    {
        await rubricStore.SaveRubricAsync(Rubric("tenant-A"));
        await sink.SaveAsync(Result("tenant-A"));

        // another tenant asking for the same id gets nothing
        (await reader.GetResultAsync("tenant-B", "eval-1")).Should().BeNull();
        (await reader.GetAuditChainAsync("tenant-B", "eval-1")).Should().BeNull();

        // the owning tenant still sees it
        (await reader.GetResultAsync("tenant-A", "eval-1")).Should().NotBeNull();
    }

    [Fact]
    public async Task EfStore_RoundTrips_PersistedFairnessFlags()
    {
        // Flags raised by the generator must survive persistence so they can resurface at
        // approval time (DESIGN.md §8) — proven against the EF column, not just in-memory.
        var ef = new EfEvaluationStore(new TestDbContextFactory());
        var draft = new RubricVersion(
            "rub-flag", "tenant-1", 1, RubricStatus.Draft,
            new[] { new RubricCriterion("youth", "Youthful energy (age)", 1.0, new Dictionary<int, string> { [5] = "energetic" }) },
            FairnessFlags: new[] { new FairnessFlag("youth", "age") });

        await ef.SaveRubricAsync(draft);

        var reloaded = await ef.GetRubricAsync("tenant-1", "rub-flag");
        reloaded.Should().NotBeNull();
        reloaded!.FairnessFlags.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new FairnessFlag("youth", "age"));
    }

    /// <summary>Each created context shares one in-memory database name so writes are
    /// visible to later reads — mimics a real backing store across units of work.</summary>
    private sealed class TestDbContextFactory : IDbContextFactory<RubricDbContext>
    {
        private readonly string _dbName = "store-tests-" + Guid.NewGuid().ToString("N");

        public RubricDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<RubricDbContext>()
                .UseInMemoryDatabase(_dbName)
                .Options;
            return new RubricDbContext(options);
        }
    }
}
