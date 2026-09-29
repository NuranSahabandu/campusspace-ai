using System.Text.Json;
using CampusSpace.Api.Models;

namespace CampusSpace.Api.Services;

/// <summary>
/// Which policy keys changed since an agent run took its snapshot (addendum: "policy changed since this proposal").
/// Compares the stored snapshot JSON with the current values key by key: numbers by value (3 equals 3.0), objects and
/// arrays structurally. A key missing from the snapshot counts as changed; no snapshot means nothing to compare.
/// </summary>
public static class PolicyDiff
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<string> ChangedKeys(string? snapshotJson, IReadOnlyDictionary<string, object?> current)
    {
        if (snapshotJson is null)
            return [];

        JsonElement snapshot;
        try
        {
            using var doc = JsonDocument.Parse(snapshotJson);
            snapshot = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return PolicyKeys.All;
        }
        if (snapshot.ValueKind != JsonValueKind.Object)
            return PolicyKeys.All;

        var now = JsonSerializer.SerializeToElement(current, Web);
        return PolicyKeys.All
            .Where(key => !snapshot.TryGetProperty(key, out var before)
                          || !now.TryGetProperty(key, out var after)
                          || !JsonEquals(before, after))
            .ToList();
    }

    internal static bool JsonEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind == JsonValueKind.Number && b.ValueKind == JsonValueKind.Number)
            return a.TryGetDecimal(out var x) && b.TryGetDecimal(out var y) ? x == y : a.GetDouble().Equals(b.GetDouble());
        if (a.ValueKind != b.ValueKind)
            return false;
        return a.ValueKind switch
        {
            JsonValueKind.Object => a.EnumerateObject().Count() == b.EnumerateObject().Count()
                                    && a.EnumerateObject().All(p => b.TryGetProperty(p.Name, out var other) && JsonEquals(p.Value, other)),
            JsonValueKind.Array => a.GetArrayLength() == b.GetArrayLength()
                                   && a.EnumerateArray().Zip(b.EnumerateArray()).All(pair => JsonEquals(pair.First, pair.Second)),
            JsonValueKind.String => a.GetString() == b.GetString(),
            _ => true,
        };
    }
}
