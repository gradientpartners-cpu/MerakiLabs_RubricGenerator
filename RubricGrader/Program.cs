using Microsoft.EntityFrameworkCore;
using RubricGrader.Adapters;
using RubricGrader.Api;
using RubricGrader.Repository;
using RubricGrader.Worker;

var builder = WebApplication.CreateBuilder(args);

// LLM backend selection (CLAUDE.md §2 adapter discipline). Default "replay" runs the
// whole system against recorded fixtures with ZERO credentials. Set Llm:Backend=live
// (and ANTHROPIC_API_KEY) to call Claude for real.
var backend = builder.Configuration.GetValue<string>("Llm:Backend") ?? "replay";
if (string.Equals(backend, "live", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<ILlmClient, LiveClaudeClient>();
}
else
{
    // Fixtures ship under the content root so a clean clone replays with zero credentials.
    var fixtureRoot = builder.Configuration.GetValue<string>("Llm:FixtureRoot")
        ?? Path.Combine(builder.Environment.ContentRootPath, "Fixtures", "llm_replay");
    builder.Services.AddSingleton<ILlmClient>(new ReplayClaudeClient(fixtureRoot));
}

// Async grade pipeline: bounded in-process channel + a single BackgroundService worker.
builder.Services.AddSingleton<IGradeQueue, ChannelGradeQueue>();
builder.Services.AddHostedService<GradingWorker>();

// Persistence selection. Default "memory" keeps the zero-credential demo DB-free; the
// SAME store interfaces are served by EF/Postgres when Persistence:Backend=postgres, so
// the pipeline and worker are unchanged either way (EF is the swap-in, not a hard dep).
var persistence = builder.Configuration.GetValue<string>("Persistence:Backend") ?? "memory";
var usePostgres = string.Equals(persistence, "postgres", StringComparison.OrdinalIgnoreCase);
if (usePostgres)
{
    var conn = builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Persistence:Backend=postgres requires ConnectionStrings:Postgres.");
    builder.Services.AddDbContextFactory<RubricDbContext>(o => o.UseNpgsql(conn));
    builder.Services.AddSingleton<EfEvaluationStore>();
    builder.Services.AddSingleton<IEvaluationResultSink>(sp => sp.GetRequiredService<EfEvaluationStore>());
    builder.Services.AddSingleton<IEvaluationReader>(sp => sp.GetRequiredService<EfEvaluationStore>());
    builder.Services.AddSingleton<IRubricStore>(sp => sp.GetRequiredService<EfEvaluationStore>());
    builder.Services.AddSingleton<IAuditStore>(sp => sp.GetRequiredService<EfEvaluationStore>());
}
else
{
    builder.Services.AddSingleton<InMemoryEvaluationStore>();
    builder.Services.AddSingleton<IEvaluationResultSink>(sp => sp.GetRequiredService<InMemoryEvaluationStore>());
    builder.Services.AddSingleton<IEvaluationReader>(sp => sp.GetRequiredService<InMemoryEvaluationStore>());
    builder.Services.AddSingleton<IRubricStore>(sp => sp.GetRequiredService<InMemoryEvaluationStore>());
    builder.Services.AddSingleton<IAuditStore>(sp => sp.GetRequiredService<InMemoryEvaluationStore>());
}

// Stubbed tenant context (identity stubbed, enforcement real — CLAUDE.md §3.6).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HeaderTenantContext>();

var app = builder.Build();

// Create the schema on startup when running against a real database.
if (usePostgres)
{
    using var scope = app.Services.CreateScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<RubricDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", llmBackend = backend, persistence }));
app.MapEvaluationEndpoints();
app.MapRubricEndpoints();
app.MapFairnessEndpoints();

app.Run();
