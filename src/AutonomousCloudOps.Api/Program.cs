using AutonomousCloudOps.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ =>
{
    // Integration tests use an isolated in-memory store; local runs use a durable SQLite file.
    var databasePath = builder.Environment.IsEnvironment("Testing")
        ? null
        : builder.Configuration["IncidentDatabasePath"] ??
          Path.Combine(builder.Environment.ContentRootPath, "data", "incidents.db");
    return new IncidentLifecycleStore(databasePath);
});
builder.Services.AddSingleton<IncidentDetector>();
builder.Services.AddSingleton<IIncidentDiagnosticProvider, MockIncidentDiagnosticProvider>();
builder.Services.AddSingleton<IncidentDiagnosticService>();
var app = builder.Build();

// Local development only. Authentication and authorization are required before deployment.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/api/incidents", (IncidentLifecycleStore store) =>
    Results.Ok(store.List()));

app.MapGet("/api/incidents/{id}", (string id, IncidentLifecycleStore store) =>
{
    try { return Results.Ok(store.Get(id)); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
});

app.MapPost("/api/telemetry/detect", (TelemetrySample sample, IncidentDetector detector, IncidentLifecycleStore store) =>
{
    try
    {
        var incidents = detector.Detect(sample);
        foreach (var incident in incidents)
            store.Open(incident);
        return Results.Ok(new { incidents, count = incidents.Count });
    }
    catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
});

app.MapPost("/api/incidents/{id}/diagnose", async (string id, IncidentLifecycleStore store,
    IncidentDiagnosticService diagnostics, CancellationToken cancellationToken) =>
{
    try
    {
        var tracked = store.Get(id);
        var assessment = await diagnostics.DiagnoseAsync(tracked.Incident, cancellationToken);
        return Results.Ok(assessment);
    }
    catch (KeyNotFoundException) { return Results.NotFound(); }
});

app.MapPost("/api/incidents/{id}/acknowledge", (string id, TransitionRequest request, IncidentLifecycleStore store) =>
    ApplyTransition(() => store.Acknowledge(id, request.Actor, request.At, request.Note ?? "")));

app.MapPost("/api/incidents/{id}/resolve", (string id, TransitionRequest request, IncidentLifecycleStore store) =>
    ApplyTransition(() => store.Resolve(id, request.Actor, request.At, request.Note ?? "")));

app.Run();

static IResult ApplyTransition(Func<TrackedIncident> action)
{
    try { return Results.Ok(action()); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
    catch (InvalidOperationException e) { return Results.Conflict(new { error = e.Message }); }
}

public sealed record TransitionRequest(string Actor, DateTimeOffset At, string? Note);
public partial class Program { }
