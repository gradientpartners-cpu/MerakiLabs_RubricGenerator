using RubricGrader.Adapters;
using RubricGrader.Domain;
using RubricGrader.Grading;
using RubricGrader.Repository;
using RubricGrader.Worker;

namespace RubricGrader.Api;

/// <summary>Endpoints over the grade store. Tenant comes from the stubbed
/// <see cref="ITenantContext"/> and is passed into the repository, which enforces
/// isolation — handlers never query unscoped.</summary>
public static class EvaluationEndpoints
{
    /// <summary>Body for the synchronous demo grade: an approved rubric (by id) and the
    /// transcript to grade against it. No enum fields, so it binds with the default
    /// System.Text.Json options.</summary>
    public sealed record GradeArtifactRequest(
        string RubricVersionId, string ArtifactId, IReadOnlyList<TranscriptTurn>? Turns);

    public static void MapEvaluationEndpoints(this IEndpointRouteBuilder app)
    {
        // SYNCHRONOUS DEMO TRIGGER. This runs the GradePipeline INLINE and returns the full
        // result (composite + every per-criterion score and validation status) in one HTTP
        // call, so the "caught hallucination" can be demonstrated live with no async timing.
        // It is the demo path ONLY. The PRODUCTION/scale design is the async route:
        // POST enqueues onto the bounded Channel<T> (IGradeQueue) and the GradingWorker
        // BackgroundService drains + grades + persists out of band. That async path is left
        // fully intact (see Worker/) — this endpoint deliberately bypasses it for the demo.
        app.MapPost("/evaluations", async (
            GradeArtifactRequest body, ITenantContext tenant,
            ILlmClient llm, IRubricStore rubrics, IEvaluationResultSink sink, CancellationToken ct) =>
        {
            if (body is null || string.IsNullOrWhiteSpace(body.RubricVersionId))
                return Results.BadRequest(new { error = "rubricVersionId is required." });
            if (body.Turns is null || body.Turns.Count == 0)
                return Results.BadRequest(new { error = "turns is required and must be non-empty." });

            // Tenant-scoped lookup — the grader only ever consumes an Approved rubric (§8).
            var rubric = await rubrics.GetRubricAsync(tenant.TenantId, body.RubricVersionId, ct);
            if (rubric is null)
                return Results.NotFound(new { error = $"No rubric '{body.RubricVersionId}' for this tenant." });
            if (rubric.Status != RubricStatus.Approved)
                return Results.UnprocessableEntity(new { error = "Rubric is not Approved; the grader consumes only approved versions." });

            var result = await GradeInlineAsync(
                rubric, tenant.TenantId, body.ArtifactId, body.Turns, GradeDemo.ModelId, llm, sink, ct);
            return Results.Ok(result);
        });

        app.MapGet("/applications/{id}", async (
            string id, ITenantContext tenant, IEvaluationReader reader, CancellationToken ct) =>
        {
            var result = await reader.GetResultAsync(tenant.TenantId, id, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // The full reconstructable chain: composite -> each criterion score -> cited
        // evidence + validation status -> rubric version.
        app.MapGet("/applications/{id}/audit", async (
            string id, ITenantContext tenant, IEvaluationReader reader, CancellationToken ct) =>
        {
            var chain = await reader.GetAuditChainAsync(tenant.TenantId, id, ct);
            return chain is null ? Results.NotFound() : Results.Ok(chain);
        });
    }

    /// <summary>The inline grade used by the synchronous demo endpoint: run the self-contained
    /// pipeline, persist the result through the same sink the async worker uses (so the GET
    /// result + audit endpoints reconstruct the run identically), and return it. Mints a fresh
    /// evaluation id per run. Factored out so it is testable without a web host.</summary>
    public static async Task<EvaluationResult> GradeInlineAsync(
        RubricVersion rubric, string tenantId, string artifactId,
        IReadOnlyList<TranscriptTurn> turns, string modelId,
        ILlmClient llm, IEvaluationResultSink sink, CancellationToken ct = default)
    {
        var evaluationId = "eval-" + Guid.NewGuid().ToString("N")[..8];
        var request = new GradeRequest(evaluationId, tenantId, artifactId, rubric, turns, modelId);
        var result = await GradePipeline.RunAsync(request, llm, ct);
        await sink.SaveAsync(result, ct);
        return result;
    }
}
