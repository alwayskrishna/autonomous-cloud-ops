using System.Text.Json;
using AutonomousCloudOps.Core;
using Xunit;

namespace AutonomousCloudOps.Tests;

public sealed class DiagnosticToolRegistryTests
{
    private static (DiagnosticToolRegistry Tools, Incident Incident) Create()
    {
        var store = new IncidentLifecycleStore();
        var incident = new Incident("incident-1", "node-1",
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"), IncidentSeverity.Critical,
            "Rule cpu-high: CpuPercent 95 >= 90");
        store.Open(incident);
        var tools = new DiagnosticToolRegistry(store,
            new IncidentDiagnosticService(new MockIncidentDiagnosticProvider()));
        return (tools, incident);
    }

    [Fact]
    public void ListsOnlyReadOnlyTools()
    {
        var (tools, _) = Create();
        Assert.Equal(3, tools.ListTools().Count);
        Assert.DoesNotContain(tools.ListTools(), t => t.Name.Contains("resolve", StringComparison.OrdinalIgnoreCase));
        foreach (var definition in tools.ListTools())
            using (JsonDocument.Parse(definition.InputSchemaJson)) { }
    }

    [Fact]
    public async Task ListsIncidents()
    {
        var (tools, _) = Create();
        var result = await tools.InvokeAsync(new DiagnosticToolCall("list_incidents", "{}"));
        Assert.False(result.IsError);
        Assert.Contains("incident-1", result.ContentJson);
    }

    [Fact]
    public async Task GetsIncidentWithHistory()
    {
        var (tools, _) = Create();
        var result = await tools.InvokeAsync(new DiagnosticToolCall("get_incident", """{"incidentId":"incident-1"}"""));
        Assert.False(result.IsError);
        Assert.Contains("incident-1", result.ContentJson);
    }

    [Fact]
    public async Task DiagnosesIncidentWithoutChangingState()
    {
        var (tools, incident) = Create();
        var result = await tools.InvokeAsync(new DiagnosticToolCall("diagnose_incident", """{"incidentId":"incident-1"}"""));
        Assert.False(result.IsError);
        Assert.Contains("offline-mock", result.ContentJson);
        var get = await tools.InvokeAsync(new DiagnosticToolCall("get_incident", """{"incidentId":"incident-1"}"""));
        Assert.Contains("\"status\":0", get.ContentJson);
    }

    [Theory]
    [InlineData("unknown", "{}")]
    [InlineData("get_incident", "{}")]
    [InlineData("get_incident", """{"incidentId":""}""")]
    [InlineData("get_incident", "not-json")]
    [InlineData("list_incidents", """{"extra":1}""")]
    [InlineData("get_incident", """{"incidentId":"missing"}""")]
    public async Task InvalidCallsReturnErrors(string name, string arguments)
    {
        var (tools, _) = Create();
        var result = await tools.InvokeAsync(new DiagnosticToolCall(name, arguments));
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task CancellationIsHonored()
    {
        var (tools, _) = Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tools.InvokeAsync(new DiagnosticToolCall("list_incidents", "{}"), cts.Token));
    }
}
