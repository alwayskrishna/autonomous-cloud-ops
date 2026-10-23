using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AutonomousCloudOps.Core;

/// <summary>Caller-supplied credential provider; production integration must use managed identity.</summary>
public interface IAzureManagementTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>Opt-in Azure Monitor Metrics REST adapter. Never registered by default.</summary>
public sealed class AzureMonitorRestMetricReader : IAzureMetricReader
{
    private readonly HttpClient _http;
    private readonly IAzureManagementTokenProvider _tokens;
    private readonly string _resourceId;
    private readonly TimeSpan _lookback;

    public AzureMonitorRestMetricReader(HttpClient http, IAzureManagementTokenProvider tokens,
        string resourceId, TimeSpan? lookback = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        if (!resourceId.StartsWith("/subscriptions/", StringComparison.OrdinalIgnoreCase) ||
            resourceId.Contains('?') || resourceId.Contains('#') || resourceId.Contains(".."))
            throw new ArgumentException("Expected a valid Azure ARM resource ID.", nameof(resourceId));
        _resourceId = resourceId;
        _lookback = lookback ?? TimeSpan.FromMinutes(5);
        if (_lookback <= TimeSpan.Zero || _lookback > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(lookback));
    }

    public async IAsyncEnumerable<AzureMetricPoint> ReadMetricsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var end = DateTimeOffset.UtcNow;
        var start = end - _lookback;
        var names = Uri.EscapeDataString("Percentage CPU,Memory Percentage,Http 5xx");
        var uri = new Uri($"https://management.azure.com{_resourceId}/providers/microsoft.insights/metrics" +
            $"?api-version=2018-01-01&metricnames={names}&timespan={Uri.EscapeDataString($"{start:O}/{end:O}")}&aggregation=Average,Total");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var token = await _tokens.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Azure management access token is required.");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var points = new List<AzureMetricPoint>();
        if (doc.RootElement.TryGetProperty("value", out var metrics))
            foreach (var metric in metrics.EnumerateArray())
            {
                var name = metric.GetProperty("name").GetProperty("value").GetString();
                if (name is not ("Percentage CPU" or "Memory Percentage" or "Http 5xx")) continue;
                if (!metric.TryGetProperty("timeseries", out var series)) continue;
                foreach (var timeSeries in series.EnumerateArray())
                {
                    if (!timeSeries.TryGetProperty("data", out var data)) continue;
                    foreach (var item in data.EnumerateArray())
                    {
                        var field = name == "Http 5xx" ? "total" : "average";
                        if (!item.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.Number)
                            continue;
                        var timestamp = item.GetProperty("timeStamp").GetDateTimeOffset();
                        points.Add(new AzureMetricPoint(_resourceId, name, timestamp, value.GetDouble()));
                    }
                }
            }
        foreach (var point in points.OrderBy(p => p.Timestamp).ThenBy(p => p.MetricName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return point;
        }
    }
}
