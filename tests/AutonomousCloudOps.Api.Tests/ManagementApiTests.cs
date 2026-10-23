using Microsoft.AspNetCore.Hosting;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AutonomousCloudOps.Api.Tests;

public sealed class ManagementApiTests
{
    [Fact]
    public async Task HealthEndpointReturnsHealthy()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        var response = await factory.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task IncidentListStartsEmpty()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        var response = await factory.CreateClient().GetAsync("/api/incidents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DetectAndTransitionIncident()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        var client = factory.CreateClient();
        var detectedAt = DateTimeOffset.UtcNow;
        var detect = await client.PostAsJsonAsync("/api/telemetry/detect",
            new { resourceId = "demo/node", timestamp = detectedAt, cpuPercent = 95, memoryPercent = 20, http5xxCount = 0 });
        Assert.Equal(HttpStatusCode.OK, detect.StatusCode);
        var list = await client.GetFromJsonAsync<System.Text.Json.JsonElement[]>("/api/incidents");
        Assert.NotNull(list);
        var incident = Assert.Single(list);
        var id = incident.GetProperty("incident").GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));
        var ack = await client.PostAsJsonAsync($"/api/incidents/{id}/acknowledge",
            new { actor = "local-test", at = detectedAt.AddMinutes(1), note = "investigating" });
        Assert.Equal(HttpStatusCode.OK, ack.StatusCode);
        var resolve = await client.PostAsJsonAsync($"/api/incidents/{id}/resolve",
            new { actor = "local-test", at = detectedAt.AddMinutes(2), note = "resolved" });
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        var duplicate = await client.PostAsJsonAsync($"/api/incidents/{id}/resolve",
            new { actor = "local-test", at = detectedAt.AddMinutes(3) });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task MissingIncidentReturnsNotFound()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        var response = await factory.CreateClient().GetAsync("/api/incidents/missing");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InvalidTelemetryReturnsBadRequest()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        var response = await factory.CreateClient().PostAsJsonAsync("/api/telemetry/detect",
            new { resourceId = "demo/node", timestamp = DateTimeOffset.UtcNow, cpuPercent = 200, memoryPercent = 20, http5xxCount = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
