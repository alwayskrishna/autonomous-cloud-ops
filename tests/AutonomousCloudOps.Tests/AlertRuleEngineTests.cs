using AutonomousCloudOps.Core;
using Xunit;

namespace AutonomousCloudOps.Tests;

public sealed class AlertRuleEngineTests
{
    private static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static TelemetrySample Sample(string resource = "prod/node-1", double cpu = 91, double memory = 20, int errors = 0) =>
        new(resource, Time, cpu, memory, errors);

    [Fact]
    public void DefaultPolicyMatchesOriginalThresholds()
    {
        var incidents = new AlertRuleEngine(AlertPolicy.Default).Evaluate(Sample(cpu: 90, memory: 90, errors: 10));
        Assert.Equal(3, incidents.Count);
        Assert.Equal(2, incidents.Count(i => i.Severity == IncidentSeverity.Critical));
    }

    [Fact]
    public void ResourceSpecificPolicyMatchesPrefix()
    {
        var engine = new AlertRuleEngine(new AlertPolicy(new[]
        {
            new AlertRule("prod-cpu", TelemetryMetric.CpuPercent, 80, IncidentSeverity.Critical, "prod/*")
        }));
        Assert.Single(engine.Evaluate(Sample()));
        Assert.Empty(engine.Evaluate(Sample("dev/node-1")));
    }

    [Fact]
    public void ExactAndSuffixPatternsMatch()
    {
        var engine = new AlertRuleEngine(new AlertPolicy(new[]
        {
            new AlertRule("exact", TelemetryMetric.CpuPercent, 80, IncidentSeverity.Warning, "prod/node-1"),
            new AlertRule("suffix", TelemetryMetric.CpuPercent, 80, IncidentSeverity.Critical, "*node-1")
        }));
        Assert.Equal(2, engine.Evaluate(Sample()).Count);
        Assert.Empty(engine.Evaluate(Sample("prod/node-2")));
    }

    [Fact]
    public void DisabledRulesDoNotAlert()
    {
        var engine = new AlertRuleEngine(new AlertPolicy(new[]
        {
            new AlertRule("off", TelemetryMetric.CpuPercent, 10, IncidentSeverity.Warning, Enabled: false)
        }));
        Assert.Empty(engine.Evaluate(Sample()));
    }

    [Fact]
    public void ThresholdIsInclusiveAndSeverityIsConfigurable()
    {
        var engine = new AlertRuleEngine(new AlertPolicy(new[]
        {
            new AlertRule("cpu", TelemetryMetric.CpuPercent, 91, IncidentSeverity.Warning)
        }));
        Assert.Equal(IncidentSeverity.Warning, Assert.Single(engine.Evaluate(Sample())).Severity);
        Assert.Empty(engine.Evaluate(Sample(cpu: 90)));
    }

    [Fact]
    public void DuplicateIdsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new AlertPolicy(new[]
        {
            new AlertRule("same", TelemetryMetric.CpuPercent, 80, IncidentSeverity.Warning),
            new AlertRule("same", TelemetryMetric.MemoryPercent, 80, IncidentSeverity.Warning)
        }));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(double.NaN)]
    public void InvalidCpuThresholdRejected(double threshold)
    {
        Assert.Throws<ArgumentException>(() =>
            new AlertPolicy(new[] { new AlertRule("cpu", TelemetryMetric.CpuPercent, threshold, IncidentSeverity.Warning) }));
    }

    [Fact]
    public void FractionalErrorCountThresholdRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new AlertPolicy(new[] { new AlertRule("errors", TelemetryMetric.Http5xxCount, 1.5, IncidentSeverity.Warning) }));
    }

    [Fact]
    public void UnsupportedWildcardPatternRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new AlertPolicy(new[] { new AlertRule("cpu", TelemetryMetric.CpuPercent, 90, IncidentSeverity.Warning, "prod/*/node") }));
    }

    [Fact]
    public void RuleSnapshotCannotBeMutatedViaOriginalList()
    {
        var rules = new List<AlertRule> { new("cpu", TelemetryMetric.CpuPercent, 90, IncidentSeverity.Warning) };
        var policy = new AlertPolicy(rules);
        rules.Clear();
        Assert.Single(policy.Rules);
    }
}
