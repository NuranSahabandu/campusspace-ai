using System.Text.Json;
using FluentAssertions;

namespace CampusSpace.Tests.Infrastructure;

public static class HttpAssertions
{
    /// <summary>Asserts an RFC 9457 body with the expected status and a traceId, and returns it.</summary>
    public static async Task<JsonElement> ShouldBeProblemAsync(this HttpResponseMessage response, int status)
    {
        ((int)response.StatusCode).Should().Be(status);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = json.RootElement.Clone();
        body.GetProperty("status").GetInt32().Should().Be(status);
        body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        return body;
    }

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }
}
