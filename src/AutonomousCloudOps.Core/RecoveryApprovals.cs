using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AutonomousCloudOps.Core;

public enum RecoveryApprovalStatus { Pending, Approved, Rejected }

public sealed record RecoveryApprovalDecision(
    RecoveryApprovalStatus Status, string Actor, DateTimeOffset At, string Reason);

public sealed record RecoveryApprovalRequest(
    string Id,
    RecoveryAction Action,
    RecoveryApprovalStatus Status,
    DateTimeOffset RequestedAt,
    IReadOnlyList<RecoveryApprovalDecision> Decisions);

/// <summary>
/// Thread-safe, process-local approval ledger. Approval never triggers infrastructure execution.
/// An approved or rejected request is terminal and cannot be decided again.
/// </summary>
public sealed class RecoveryApprovalStore
{
    private readonly object _gate = new();
    private readonly string? _connectionString;

    public RecoveryApprovalStore(string? databasePath = null)
    {
        if (databasePath is null) return;
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS recovery_approvals (id TEXT PRIMARY KEY, payload TEXT NOT NULL)";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT payload FROM recovery_approvals";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var request = JsonSerializer.Deserialize<RecoveryApprovalRequest>(reader.GetString(0))
                ?? throw new InvalidDataException("Invalid persisted approval payload.");
            _requests.Add(request.Id, request);
        }
    }

    private void Persist(RecoveryApprovalRequest request)
    {
        if (_connectionString is null) return;
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO recovery_approvals(id, payload) VALUES ($id, $payload) " +
            "ON CONFLICT(id) DO UPDATE SET payload = excluded.payload";
        command.Parameters.AddWithValue("$id", request.Id);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(request));
        command.ExecuteNonQuery();
    }
    private readonly Dictionary<string, RecoveryApprovalRequest> _requests = new(StringComparer.Ordinal);

    public RecoveryApprovalRequest Submit(RecoveryAction action, DateTimeOffset requestedAt)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(action.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(action.IncidentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(action.ResourceId);
        // Reject invalid action contracts before accepting them for review.
        new RecoveryDryRunService().Simulate(action);
        var request = new RecoveryApprovalRequest(
            Guid.NewGuid().ToString("N"), action, RecoveryApprovalStatus.Pending,
            requestedAt, Array.Empty<RecoveryApprovalDecision>());
        lock (_gate)
        {
            _requests.Add(request.Id, request);
            return request;
        }
    }

    public RecoveryApprovalRequest Get(string requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        lock (_gate)
        {
            return _requests.TryGetValue(requestId, out var request)
                ? request : throw new KeyNotFoundException($"Approval '{requestId}' not found.");
        }
    }

    public IReadOnlyList<RecoveryApprovalRequest> List()
    {
        lock (_gate)
        {
            return _requests.Values.OrderBy(r => r.RequestedAt)
                .ThenBy(r => r.Id, StringComparer.Ordinal).ToArray();
        }
    }

    public RecoveryApprovalRequest Approve(string requestId, string actor, DateTimeOffset at, string reason) =>
        Decide(requestId, RecoveryApprovalStatus.Approved, actor, at, reason);

    public RecoveryApprovalRequest Reject(string requestId, string actor, DateTimeOffset at, string reason) =>
        Decide(requestId, RecoveryApprovalStatus.Rejected, actor, at, reason);

    private RecoveryApprovalRequest Decide(
        string requestId, RecoveryApprovalStatus target, string actor, DateTimeOffset at, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        lock (_gate)
        {
            if (!_requests.TryGetValue(requestId, out var current))
                throw new KeyNotFoundException($"Approval '{requestId}' not found.");
            if (current.Status != RecoveryApprovalStatus.Pending)
                throw new InvalidOperationException("Approval request already decided.");
            if (at < current.RequestedAt)
                throw new ArgumentOutOfRangeException(nameof(at), "Decision cannot predate the request.");
            var decision = new RecoveryApprovalDecision(target, actor, at, reason);
            var updated = current with
            {
                Status = target,
                Decisions = current.Decisions.Concat(new[] { decision }).ToArray()
            };
            Persist(updated);
            _requests[requestId] = updated;
            return updated;
        }
    }
}
