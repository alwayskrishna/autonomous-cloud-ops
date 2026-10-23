using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AutonomousCloudOps.Core;

public enum IncidentStatus { Open, Acknowledged, Resolved }

public sealed record IncidentTransition(
    IncidentStatus From,
    IncidentStatus To,
    DateTimeOffset At,
    string Actor,
    string Note);

public sealed record TrackedIncident(
    Incident Incident,
    IncidentStatus Status,
    IReadOnlyList<IncidentTransition> History);

/// <summary>
/// Thread-safe incident lifecycle with optional SQLite-backed durability.
/// </summary>
public sealed class IncidentLifecycleStore
{
    private readonly object _gate = new();
    private readonly string? _connectionString;

    public IncidentLifecycleStore(string? databasePath = null)
    {
        if (databasePath is null) return;
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path must not be empty.", nameof(databasePath));
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE IF NOT EXISTS incidents (id TEXT PRIMARY KEY, payload TEXT NOT NULL)";
            create.ExecuteNonQuery();
        }
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT payload FROM incidents";
        using var reader = read.ExecuteReader();
        while (reader.Read())
        {
            var tracked = JsonSerializer.Deserialize<TrackedIncident>(reader.GetString(0))
                ?? throw new InvalidDataException("Invalid persisted incident payload.");
            _incidents.Add(tracked.Incident.Id, tracked);
        }
    }

    private void Persist(TrackedIncident tracked)
    {
        if (_connectionString is null) return;
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO incidents(id, payload) VALUES ($id, $payload) " +
            "ON CONFLICT(id) DO UPDATE SET payload = excluded.payload";
        command.Parameters.AddWithValue("$id", tracked.Incident.Id);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(tracked));
        command.ExecuteNonQuery();
    }

    private readonly Dictionary<string, TrackedIncident> _incidents = new(StringComparer.Ordinal);

    public TrackedIncident Open(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        if (string.IsNullOrWhiteSpace(incident.Id))
            throw new ArgumentException("Incident ID is required.", nameof(incident));

        lock (_gate)
        {
            if (_incidents.ContainsKey(incident.Id))
                throw new InvalidOperationException($"Incident '{incident.Id}' already exists.");
            var tracked = new TrackedIncident(incident, IncidentStatus.Open, Array.Empty<IncidentTransition>());
            Persist(tracked);
            _incidents.Add(incident.Id, tracked);
            return tracked;
        }
    }

    public TrackedIncident Get(string incidentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);
        lock (_gate)
        {
            return _incidents.TryGetValue(incidentId, out var tracked)
                ? tracked
                : throw new KeyNotFoundException($"Incident '{incidentId}' was not found.");
        }
    }

    public IReadOnlyList<TrackedIncident> List()
    {
        lock (_gate)
        {
            return _incidents.Values.OrderBy(i => i.Incident.DetectedAt).ThenBy(i => i.Incident.Id).ToArray();
        }
    }

    public TrackedIncident Acknowledge(string incidentId, string actor, DateTimeOffset at, string note = "") =>
        Transition(incidentId, IncidentStatus.Acknowledged, actor, at, note);

    public TrackedIncident Resolve(string incidentId, string actor, DateTimeOffset at, string note = "") =>
        Transition(incidentId, IncidentStatus.Resolved, actor, at, note);

    private TrackedIncident Transition(
        string incidentId, IncidentStatus target, string actor, DateTimeOffset at, string note)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentNullException.ThrowIfNull(note);

        lock (_gate)
        {
            if (!_incidents.TryGetValue(incidentId, out var current))
                throw new KeyNotFoundException($"Incident '{incidentId}' was not found.");
            if (current.Status == IncidentStatus.Resolved)
                throw new InvalidOperationException("Resolved incidents cannot be modified.");
            if (target == IncidentStatus.Acknowledged && current.Status != IncidentStatus.Open)
                throw new InvalidOperationException("Only open incidents can be acknowledged.");
            if (target == IncidentStatus.Resolved &&
                current.Status is not (IncidentStatus.Open or IncidentStatus.Acknowledged))
                throw new InvalidOperationException("Incident cannot be resolved from its current state.");
            if (at < current.Incident.DetectedAt ||
                (current.History.Count > 0 && at < current.History[^1].At))
                throw new ArgumentOutOfRangeException(nameof(at), "Transition time must not precede incident history.");

            var transition = new IncidentTransition(current.Status, target, at, actor, note);
            var history = current.History.Concat(new[] { transition }).ToArray();
            var updated = new TrackedIncident(current.Incident, target, history);
            Persist(updated);
            _incidents[incidentId] = updated;
            return updated;
        }
    }
}
