using System.Text.Json;
using FluentAssertions;
using RubricGrader.Audit;
using RubricGrader.Domain;
using RubricGrader.Generation;
using RubricGrader.Repository;
using Xunit;

namespace RubricGrader.Tests;

/// <summary>
/// The human-approval gate is the fairness enforcement point (DESIGN.md §8), so it must be
/// traceable: approving a Draft freezes it to Approved AND appends an auditable
/// rubric.approved event (approver + timestamp + draft->approved transition) to the same
/// append-only ledger. These run against the in-memory store with zero credentials.
/// </summary>
public class RubricApprovalTests
{
    private static RubricVersion Draft(string tenant, string id) => new(
        id, tenant, 1, RubricStatus.Draft,
        new[] { new RubricCriterion("comm", "Communication", 1.0, new Dictionary<int, string> { [5] = "clear" }) });

    [Fact]
    public async Task Approve_FreezesDraft_AndRecordsApproverInAuditTrail()
    {
        var store = new InMemoryEvaluationStore();
        await store.SaveRubricAsync(Draft("tenant-1", "rub-1"));

        var approved = await RubricApproval.ApproveAsync(store, store, "tenant-1", "rub-1", "alice@acme");

        // frozen with the accountable approver stamped on the version
        approved.Should().NotBeNull();
        approved!.Status.Should().Be(RubricStatus.Approved);
        approved.ApprovedBy.Should().Be("alice@acme");
        approved.ApprovedAt.Should().NotBeNull();

        // the approval is in the append-only audit trail, keyed by the rubric version id
        var events = await store.GetEventsAsync("tenant-1", "rub-1");
        var approvalEvent = events.Should().ContainSingle(e => e.Kind == AuditTrail.RubricApproved).Subject;

        using var payload = JsonDocument.Parse(approvalEvent.PayloadJson);
        var root = payload.RootElement;
        root.GetProperty("approvedBy").GetString().Should().Be("alice@acme");
        root.GetProperty("fromStatus").GetString().Should().Be("Draft");
        root.GetProperty("toStatus").GetString().Should().Be("Approved");
        root.GetProperty("rubricVersionId").GetString().Should().Be("rub-1");
    }

    [Fact]
    public async Task Approve_ResurfacesFairnessFlags_ToTheApprover()
    {
        // A draft that tripped the protected-attribute denylist carries its flags. Approval
        // must hand those flags back so the human gate never approves blind (DESIGN.md §8).
        var store = new InMemoryEvaluationStore();
        var flagged = new RubricVersion(
            "rub-1", "tenant-1", 1, RubricStatus.Draft,
            new[] { new RubricCriterion("youth", "Youthful energy (age)", 1.0, new Dictionary<int, string> { [5] = "energetic" }) },
            FairnessFlags: new[] { new FairnessFlag("youth", "age") });
        await store.SaveRubricAsync(flagged);

        var approved = await RubricApproval.ApproveAsync(store, store, "tenant-1", "rub-1", "alice@acme");

        approved.Should().NotBeNull();
        approved!.FairnessFlags.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new FairnessFlag("youth", "age"));
    }

    [Fact]
    public async Task Approve_UnknownRubric_ReturnsNull()
    {
        var store = new InMemoryEvaluationStore();
        var result = await RubricApproval.ApproveAsync(store, store, "tenant-1", "missing", "alice");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Approve_AlreadyApproved_Throws_AndDoesNotDoubleAudit()
    {
        var store = new InMemoryEvaluationStore();
        await store.SaveRubricAsync(Draft("tenant-1", "rub-1"));
        await RubricApproval.ApproveAsync(store, store, "tenant-1", "rub-1", "alice");

        var act = () => RubricApproval.ApproveAsync(store, store, "tenant-1", "rub-1", "bob");
        await act.Should().ThrowAsync<RubricApproval.AlreadyApprovedException>();

        // still exactly one approval event — immutability of the approved version holds
        var events = await store.GetEventsAsync("tenant-1", "rub-1");
        events.Count(e => e.Kind == AuditTrail.RubricApproved).Should().Be(1);
    }

    [Fact]
    public async Task Approve_CrossTenant_IsBlocked()
    {
        var store = new InMemoryEvaluationStore();
        await store.SaveRubricAsync(Draft("tenant-A", "rub-1"));

        // a different tenant cannot see the draft, so cannot approve it (404 path)
        (await RubricApproval.ApproveAsync(store, store, "tenant-B", "rub-1", "mallory"))
            .Should().BeNull();

        // and no audit event leaked under either tenant
        (await store.GetEventsAsync("tenant-B", "rub-1")).Should().BeEmpty();
        (await store.GetEventsAsync("tenant-A", "rub-1")).Should().BeEmpty();
    }
}
