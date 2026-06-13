namespace RubricGrader.Api;

/// <summary>Current tenant + acting user for the request. Identity is STUBBED (no real auth
/// in this slice) but enforcement is real: TenantId flows into every repository read, which
/// filters on it (CLAUDE.md §3.6), and UserId is recorded as the accountable actor on
/// audited actions like rubric approval. Swap the resolution for real auth without touching
/// call sites.</summary>
public interface ITenantContext
{
    string TenantId { get; }

    /// <summary>The acting user — recorded as the approver on the rubric-approval audit
    /// event so "a human approved it" is verifiable, not asserted.</summary>
    string UserId { get; }
}

/// <summary>Stub resolution: read the tenant from "X-Tenant-Id" and the acting user from
/// "X-User-Id", each defaulting so the zero-credential walkthrough works out of the box.</summary>
public sealed class HeaderTenantContext : ITenantContext
{
    public const string HeaderName = "X-Tenant-Id";
    public const string UserHeaderName = "X-User-Id";
    public const string DefaultTenant = "tenant-demo";
    public const string DefaultUser = "stub-user";

    public HeaderTenantContext(IHttpContextAccessor accessor)
    {
        var headers = accessor.HttpContext?.Request.Headers;
        var tenant = headers?[HeaderName].ToString();
        var user = headers?[UserHeaderName].ToString();
        TenantId = string.IsNullOrWhiteSpace(tenant) ? DefaultTenant : tenant!;
        UserId = string.IsNullOrWhiteSpace(user) ? DefaultUser : user!;
    }

    public string TenantId { get; }
    public string UserId { get; }
}
