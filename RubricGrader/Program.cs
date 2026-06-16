using Microsoft.EntityFrameworkCore;
using RubricGrader.Adapters;
using RubricGrader.Api;
using RubricGrader.Grading;
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

// DEMO-ONLY CORS. The static demo console (demo/index.html) is opened from a file:// or
// other origin, so the browser needs an Access-Control-Allow-Origin header to call this
// API. There are no cookies/credentials here (tenant is a plain header), so any-origin is
// safe for a local demo. This is the ONLY concession made for the demo UI; production auth
// would scope this to known origins. See ai/decisions/0005-demo-console.md.
builder.Services.AddCors(o => o.AddPolicy("demo", p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseCors("demo");

// Create the schema on startup when running against a real database.
if (usePostgres)
{
    using var scope = app.Services.CreateScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<RubricDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();
}
else
{
    // Seed the canonical approved rubric for the zero-credential grade demo (POST /evaluations
    // against rub-backend-screen-v3). Demo-path only: the postgres path stays clean and expects
    // rubrics to arrive via the real /jd -> /approve lifecycle.
    var rubrics = app.Services.GetRequiredService<IRubricStore>();
    await rubrics.SaveRubricAsync(GradeDemo.Rubric);
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", llmBackend = backend, persistence }));
app.MapEvaluationEndpoints();
app.MapRubricEndpoints();
app.MapFairnessEndpoints();

app.Run();
