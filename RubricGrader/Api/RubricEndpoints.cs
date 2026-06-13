using RubricGrader.Adapters;
using RubricGrader.Domain;
using RubricGrader.Generation;
using RubricGrader.Repository;

namespace RubricGrader.Api;

/// <summary>The rubric lifecycle endpoints: generate a Draft from a JD, freeze a Draft into
/// an Approved version (audited), and read a rubric's audit trail. Tenant + acting user come
/// from the stubbed <see cref="ITenantContext"/>; persistence + audit go through the same
/// tenant-scoped stores as the grader.</summary>
public static class RubricEndpoints
{
    public sealed record GenerateRubricRequest(string JobDescription);

    public static void MapRubricEndpoints(this IEndpointRouteBuilder app)
    {
        // Generate a DRAFT rubric from a job description and persist it. The draft is not
        // usable by the grader until a human approves it; any fairness flags are returned
        // for the reviewer to weigh (never auto-applied).
        app.MapPost("/jd", async (
            GenerateRubricRequest body, ITenantContext tenant,
            ILlmClient llm, IRubricStore rubrics, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.JobDescription))
                return Results.BadRequest(new { error = "jobDescription is required." });

            var rubricId = "rub-" + Guid.NewGuid().ToString("N")[..12];
            var result = await RubricGenerator.GenerateAsync(
                tenant.TenantId, rubricId, body.JobDescription, llm, ct);

            await rubrics.SaveRubricAsync(result.Rubric, ct);

            return Results.Created($"/rubrics/{rubricId}", new
            {
                rubric = result.Rubric,
                fairnessFlags = result.FairnessFlags,
            });
        });

        // Freeze a DRAFT into an APPROVED version. Records who approved + when + the
        // draft->approved transition into the append-only audit trail.
        app.MapPost("/rubrics/{id}/approve", async (
            string id, ITenantContext tenant,
            IRubricStore rubrics, IAuditStore audit, CancellationToken ct) =>
        {
            try
            {
                var approved = await RubricApproval.ApproveAsync(
                    rubrics, audit, tenant.TenantId, id, tenant.UserId, ct);
                if (approved is null)
                    return Results.NotFound();

                // Resurface the fairness flags at approval time so the human gate — the
                // actual fairness enforcement point (DESIGN.md §8) — never approves blind.
                // Same {rubric, fairnessFlags} shape as /jd for parity.
                return Results.Ok(new
                {
                    rubric = approved,
                    fairnessFlags = approved.FairnessFlags ?? Array.Empty<FairnessFlag>(),
                });
            }
            catch (RubricApproval.AlreadyApprovedException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        // The audit trail for a rubric — demonstrably includes the approval event, so the
        // human-accountable gate is itself verifiable.
        app.MapGet("/rubrics/{id}/audit", async (
            string id, ITenantContext tenant, IAuditStore audit, CancellationToken ct) =>
        {
            var events = await audit.GetEventsAsync(tenant.TenantId, id, ct);
            return Results.Ok(events);
        });
    }
}
