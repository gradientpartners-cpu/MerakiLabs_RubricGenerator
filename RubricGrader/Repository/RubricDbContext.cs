using Microsoft.EntityFrameworkCore;

namespace RubricGrader.Repository;

/// <summary>
/// EF Core persistence model. Rows are plain POCOs kept separate from the immutable domain
/// records (the domain stays free of EF concerns); the EF store maps between them. Enums
/// are stored as strings so the tables read cleanly in an audit. Every row carries
/// TenantId — the column the repository filters on for isolation (CLAUDE.md §3.6).
/// </summary>
public sealed class RubricDbContext : DbContext
{
    public RubricDbContext(DbContextOptions<RubricDbContext> options) : base(options) { }

    public DbSet<RubricVersionRow> RubricVersions => Set<RubricVersionRow>();
    public DbSet<EvaluationRow> Evaluations => Set<EvaluationRow>();
    public DbSet<CriterionResultRow> CriterionResults => Set<CriterionResultRow>();
    public DbSet<AuditEventRow> AuditEvents => Set<AuditEventRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<RubricVersionRow>(e =>
        {
            e.ToTable("rubric_versions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TenantId);
        });

        b.Entity<EvaluationRow>(e =>
        {
            e.ToTable("evaluations");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TenantId);
            e.HasMany(x => x.Criteria)
                .WithOne()
                .HasForeignKey(c => c.EvaluationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CriterionResultRow>(e =>
        {
            e.ToTable("criterion_results");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.EvaluationId);
        });

        b.Entity<AuditEventRow>(e =>
        {
            e.ToTable("audit_events");   // append-only: the repository only ever inserts
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.SubjectId });
        });
    }
}

/// <summary>Marker for tenant-owned rows so the repository's tenant filter is type-safe:
/// a query over a non-tenant-owned set can't reach the scoping helper.</summary>
public interface ITenantOwned
{
    string TenantId { get; }
}

public sealed class RubricVersionRow : ITenantOwned
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public int Version { get; set; }
    public string Status { get; set; } = "";
    public string CriteriaJson { get; set; } = "";
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? FairnessFlagsJson { get; set; }
}

public sealed class EvaluationRow : ITenantOwned
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string ArtifactId { get; set; } = "";
    public string RubricVersionId { get; set; } = "";
    public string State { get; set; } = "";
    public double? CompositeScore { get; set; }
    public string ModelId { get; set; } = "";
    public string PromptVersion { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public List<CriterionResultRow> Criteria { get; set; } = new();
}

public sealed class CriterionResultRow
{
    public string Id { get; set; } = "";
    public string EvaluationId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string CriterionKey { get; set; } = "";
    public int? Score { get; set; }
    public string? Confidence { get; set; }
    public string? EvidenceTurnId { get; set; }
    public string? EvidenceSpan { get; set; }
    public string ValidationStatus { get; set; } = "";
}

public sealed class AuditEventRow : ITenantOwned
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string SubjectId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
