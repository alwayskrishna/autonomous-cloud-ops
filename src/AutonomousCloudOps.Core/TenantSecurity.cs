namespace AutonomousCloudOps.Core;

public enum TenantRole { Reader, Operator, Approver, Administrator }
public enum TenantPermission { ReadIncident, ManageIncident, RequestRecovery, DecideRecovery }

public sealed record TenantPrincipal(string TenantId, string SubjectId, TenantRole Role);

public sealed record TenantIncident(string TenantId, TrackedIncident Incident);

/// <summary>
/// Policy engine for explicit tenant and role checks. The caller must supply a trusted principal.
/// No authentication or token verification is performed by this component.
/// </summary>
public sealed class TenantAuthorizationPolicy
{
    public void Demand(TenantPrincipal principal, string tenantId, TenantPermission permission)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrWhiteSpace(principal.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(principal.SubjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (!string.Equals(principal.TenantId, tenantId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Cross-tenant access is denied.");
        if (!Enum.IsDefined(principal.Role) || !Enum.IsDefined(permission))
            throw new UnauthorizedAccessException("Unsupported role or permission.");
        var allowed = permission switch
        {
            TenantPermission.ReadIncident => true,
            TenantPermission.ManageIncident or TenantPermission.RequestRecovery =>
                principal.Role is TenantRole.Operator or TenantRole.Administrator,
            TenantPermission.DecideRecovery =>
                principal.Role is TenantRole.Approver or TenantRole.Administrator,
            _ => false
        };
        if (!allowed)
            throw new UnauthorizedAccessException("Role is not permitted to perform this operation.");
    }
}

/// <summary>
/// Tenant-keyed incident facade for security tests and future authenticated adapters.
/// Does not change the legacy single-tenant API or provide production authentication.
/// </summary>
public sealed class TenantIncidentStore
{
    private readonly TenantAuthorizationPolicy _policy;
    private readonly object _gate = new();
    private readonly Dictionary<string, IncidentLifecycleStore> _stores = new(StringComparer.Ordinal);

    public TenantIncidentStore(TenantAuthorizationPolicy policy) =>
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));

    public TenantIncident Open(TenantPrincipal principal, Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        _policy.Demand(principal, principal.TenantId, TenantPermission.ManageIncident);
        lock (_gate)
            return new TenantIncident(principal.TenantId, Store(principal.TenantId).Open(incident));
    }

    public TenantIncident Get(TenantPrincipal principal, string tenantId, string incidentId)
    {
        _policy.Demand(principal, tenantId, TenantPermission.ReadIncident);
        lock (_gate)
        {
            if (!_stores.TryGetValue(tenantId, out var store))
                throw new KeyNotFoundException("Incident not found.");
            return new TenantIncident(tenantId, store.Get(incidentId));
        }
    }

    public IReadOnlyList<TenantIncident> List(TenantPrincipal principal, string tenantId)
    {
        _policy.Demand(principal, tenantId, TenantPermission.ReadIncident);
        lock (_gate)
            return _stores.TryGetValue(tenantId, out var store)
                ? store.List().Select(incident => new TenantIncident(tenantId, incident)).ToArray()
                : Array.Empty<TenantIncident>();
    }

    public TenantIncident Acknowledge(TenantPrincipal principal, string tenantId, string incidentId,
        DateTimeOffset at, string note = "")
    {
        _policy.Demand(principal, tenantId, TenantPermission.ManageIncident);
        lock (_gate)
        {
            if (!_stores.TryGetValue(tenantId, out var store))
                throw new KeyNotFoundException("Incident not found.");
            return new TenantIncident(tenantId, store.Acknowledge(incidentId, principal.SubjectId, at, note));
        }
    }

    private IncidentLifecycleStore Store(string tenantId)
    {
        if (!_stores.TryGetValue(tenantId, out var store))
            _stores.Add(tenantId, store = new IncidentLifecycleStore());
        return store;
    }
}
