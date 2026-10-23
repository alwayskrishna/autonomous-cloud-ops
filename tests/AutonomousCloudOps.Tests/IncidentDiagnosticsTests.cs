using AutonomousCloudOps.Core;
using Xunit;

namespace AutonomousCloudOps.Tests;

public sealed class IncidentDiagnosticsTests
{
    private static Incident MakeIncident(string reason) =>
        new("incident-1", "node-1", DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            IncidentSeverity.Warning, reason);

    [Theory]
    [InlineData("Rule cpu-high: CpuPercent 95 >= 90", "CPU")]
    [InlineData("Rule memory-high: MemoryPercent 98 >= 90", "memory")]
    [InlineData("Rule errors: Http5xxCount 20 >= 10", "error")]
    public async Task MockProducesRelevantGuidance(string reason, string expected)
    {
        var service = new IncidentDiagnosticService(new MockIncidentDiagnosticProvider());
        var assessment = await service.DiagnoseAsync(MakeIncident(reason));
        Assert.Equal("incident-1", assessment.IncidentId);
        Assert.Contains(expected, assessment.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(assessment.PossibleCauses);
        Assert.NotEmpty(assessment.InvestigationSteps);
        Assert.Equal("offline-mock", assessment.Provider);
        Assert.Equal(0, assessment.Confidence);
    }

    [Fact]
    public async Task UnknownIncidentReturnsInvestigationSteps()
    {
        var service = new IncidentDiagnosticService(new MockIncidentDiagnosticProvider());
        var assessment = await service.DiagnoseAsync(MakeIncident("unrecognized anomaly"));
        Assert.NotEmpty(assessment.InvestigationSteps);
    }

    [Fact]
    public async Task CancellationIsHonored()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = new IncidentDiagnosticService(new MockIncidentDiagnosticProvider());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DiagnoseAsync(MakeIncident("CPU"), cts.Token));
    }

    [Fact]
    public async Task InvalidProviderOutputIsRejected()
    {
        var service = new IncidentDiagnosticService(new InvalidProvider());
        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.DiagnoseAsync(MakeIncident("CPU")));
    }

    [Fact]
    public async Task NullIncidentIsRejected()
    {
        var service = new IncidentDiagnosticService(new MockIncidentDiagnosticProvider());
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.DiagnoseAsync(null!));
    }

    private sealed class InvalidProvider : IIncidentDiagnosticProvider
    {
        public Task<DiagnosticAssessment> AnalyzeAsync(DiagnosticRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DiagnosticAssessment("wrong-id", "", [], [], 2, "invalid"));
    }
}
