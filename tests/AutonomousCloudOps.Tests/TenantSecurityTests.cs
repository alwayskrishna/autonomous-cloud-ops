using AutonomousCloudOps.Core;
using Xunit;

namespace AutonomousCloudOps.Tests;

public sealed class TenantSecurityTests
{
    private static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static TenantPrincipal User(string tenant, TenantRole role) => new(tenant, "subject-1", role);
    private static Incident Incident() => new("same-incident", "node-a", Time, IncidentSeverity.Critical, "CPU");

    [Fact]
    public void IdenticalIncidentIdsAreIsolatedAcrossTenants()
    {
        var store = new TenantIncidentStore(new TenantAuthorizationPolicy());
        store.Open(User("tenant-a", TenantRole.Operator), Incident());
        store.Open(User("tenant-b", TenantRole.Operator), Incident());
        Assert.Single(store.List(User("tenant-a", TenantRole.Reader), "tenant-a"));
        Assert.Single(store.List(User("tenant-b", TenantRole.Reader), "tenant-b"));
        store.Acknowledge(User("tenant-a", TenantRole.Operator), "tenant-a", "same-incident",
            Time.AddMinutes(1));
        Assert.Equal(IncidentStatus.Acknowledged,
            store.Get(User("tenant-a", TenantRole.Reader), "tenant-a", "same-incident").Incident.Status);
        Assert.Equal(IncidentStatus.Open,
            store.Get(User("tenant-b", TenantRole.Reader), "tenant-b", "same-incident").Incident.Status);
    }

    [Fact]
    public void CrossTenantReadsAndWritesAreDenied()
    {
        var store = new TenantIncidentStore(new TenantAuthorizationPolicy());
        store.Open(User("tenant-a", TenantRole.Operator), Incident());
        Assert.Throws<UnauthorizedAccessException>(() =>
            store.Get(User("tenant-b", TenantRole.Administrator), "tenant-a", "same-incident"));
        Assert.Throws<UnauthorizedAccessException>(() =>
            store.List(User("tenant-b", TenantRole.Administrator), "tenant-a"));
        Assert.Throws<UnauthorizedAccessException>(() =>
            store.Acknowledge(User("tenant-b", TenantRole.Operator), "tenant-a",
                "same-incident", Time.AddMinutes(1)));
    }

    [Fact]
    public void ReadersCannotModifyIncidents()
    {
        var store = new TenantIncidentStore(new TenantAuthorizationPolicy());
        Assert.Throws<UnauthorizedAccessException>(() =>
            store.Open(User("tenant-a", TenantRole.Reader), Incident()));
    }

    [Theory]
    [InlineData(TenantRole.Reader, TenantPermission.ReadIncident, true)]
    [InlineData(TenantRole.Reader, TenantPermission.ManageIncident, false)]
    [InlineData(TenantRole.Operator, TenantPermission.RequestRecovery, true)]
    [InlineData(TenantRole.Operator, TenantPermission.DecideRecovery, false)]
    [InlineData(TenantRole.Approver, TenantPermission.DecideRecovery, true)]
    [InlineData(TenantRole.Administrator, TenantPermission.DecideRecovery, true)]
    public void EnforcesRolePermissions(TenantRole role, TenantPermission permission, bool allowed)
    {
        var policy = new TenantAuthorizationPolicy();
        var principal = User("tenant-a", role);
        if (allowed) policy.Demand(principal, "tenant-a", permission);
        else Assert.Throws<UnauthorizedAccessException>(() =>
            policy.Demand(principal, "tenant-a", permission));
    }

    [Fact]
    public void MissingTenantDoesNotLeakData()
    {
        var store = new TenantIncidentStore(new TenantAuthorizationPolicy());
        Assert.Empty(store.List(User("tenant-new", TenantRole.Reader), "tenant-new"));
        Assert.Throws<KeyNotFoundException>(() =>
            store.Get(User("tenant-new", TenantRole.Reader), "tenant-new", "same-incident"));
    }
}
