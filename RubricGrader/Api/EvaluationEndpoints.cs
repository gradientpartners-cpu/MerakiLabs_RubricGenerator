using RubricGrader.Repository;

namespace RubricGrader.Api;

/// <summary>Read endpoints over the grade store. Tenant comes from the stubbed
/// <see cref="ITenantContext"/> and is passed into the repository, which enforces
/// isolation — handlers never query unscoped.</summary>
public static class EvaluationEndpoints
{
    public static void MapEvaluationEndpoints(this IEndpointRouteBuilder app)
    {
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
}
