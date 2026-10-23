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
builder.Services.AddSingleton<RecoveryPlanner>();
builder.Services.AddSingleton<RecoveryDryRunService>();
builder.Services.AddSingleton<RecoveryApprovalStore>();
builder.Services.AddSingleton<IIncidentEvidenceSource>(_ => new InMemoryIncidentEvidenceSource([]));
builder.Services.AddSingleton<IncidentEvidenceService>(sp =>
    new IncidentEvidenceService(sp.GetRequiredService<IIncidentEvidenceSource>()));
builder.Services.AddSingleton<RecoveryWorkflowService>();
builder.Services.AddSingleton<IPlatformRecoveryAdapter, AksRecoveryPreviewAdapter>();
builder.Services.AddSingleton<IPlatformRecoveryAdapter, ContainerAppsRecoveryPreviewAdapter>();
builder.Services.AddSingleton<PlatformRecoveryPreviewService>();
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

// Local prototype only: actor is caller-supplied, NOT authenticated identity.
app.MapGet("/api/recovery/approvals", (RecoveryApprovalStore approvals) =>
    Results.Ok(approvals.List()));

app.MapGet("/api/recovery/approvals/{requestId}", (string requestId, RecoveryApprovalStore approvals) =>
{
    try { return Results.Ok(approvals.Get(requestId)); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
});

app.MapPost("/api/incidents/{id}/recovery/approvals",
    (string id, IncidentLifecycleStore incidents, RecoveryPlanner planner,
        RecoveryApprovalStore approvals) =>
{
    try
    {
        var incident = incidents.Get(id).Incident;
        var action = planner.Propose(incident).Actions.Single();
        var approval = approvals.Submit(action, DateTimeOffset.UtcNow);
        return Results.Created($"/api/recovery/approvals/{approval.Id}", approval);
    }
    catch (KeyNotFoundException) { return Results.NotFound(); }
});

app.MapPost("/api/recovery/approvals/{requestId}/approve",
    (string requestId, ApprovalDecisionRequest decision, RecoveryApprovalStore approvals) =>
    ApplyApproval(() => approvals.Approve(requestId, decision.Actor, decision.At, decision.Reason)));

app.MapPost("/api/recovery/approvals/{requestId}/reject",
    (string requestId, ApprovalDecisionRequest decision, RecoveryApprovalStore approvals) =>
    ApplyApproval(() => approvals.Reject(requestId, decision.Actor, decision.At, decision.Reason)));

// Local orchestration only: approval does not authorize infrastructure execution.
app.MapPost("/api/incidents/{id}/recovery/workflows",
    async (string id, RecoveryWorkflowService workflows, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await workflows.StartAsync(id, cancellationToken));
    }
    catch (KeyNotFoundException) { return Results.NotFound(); }
});

app.MapGet("/api/recovery/workflows", (RecoveryWorkflowService workflows) =>
    Results.Ok(workflows.List()));

app.MapGet("/api/recovery/workflows/{workflowId}",
    (string workflowId, RecoveryWorkflowService workflows) =>
{
    try { return Results.Ok(workflows.Get(workflowId)); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
});

// Explicit target and action must agree; this endpoint only produces a simulation.
app.MapPost("/api/recovery/platform-preview",
    (PlatformRecoveryPreviewRequest request, PlatformRecoveryPreviewService previews) =>
{
    try { return Results.Ok(previews.Preview(request.Action, request.Target)); }
    catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
    catch (NotSupportedException e) { return Results.BadRequest(new { error = e.Message }); }
});

app.MapPost("/api/incidents/{id}/recovery/dry-run",
    (string id, IncidentLifecycleStore store, RecoveryPlanner planner, RecoveryDryRunService simulator) =>
{
    try
    {
        var incident = store.Get(id).Incident;
        var plan = planner.Propose(incident);
        return Results.Ok(new
        {
            plan,
            simulations = plan.Actions.Select(simulator.Simulate).ToArray()
        });
    }
    catch (KeyNotFoundException) { return Results.NotFound(); }
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

static IResult ApplyApproval(Func<RecoveryApprovalRequest> action)
{
    try { return Results.Ok(action()); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
    catch (InvalidOperationException e) { return Results.Conflict(new { error = e.Message }); }
}

public sealed record PlatformRecoveryPreviewRequest(RecoveryAction Action, RecoveryTarget Target);
public sealed record ApprovalDecisionRequest(string Actor, DateTimeOffset At, string Reason);
public sealed record TransitionRequest(string Actor, DateTimeOffset At, string? Note);
public partial class Program { }
