using FluentAssertions;

namespace CampusSpace.Tests.Infrastructure;

/// <summary>
/// Shared cases for literal %, _ and \ in a contains search (QueryableExtensions.WhereContains). A test seeds
/// <see cref="Rows"/> into the searched column and checks every case with <see cref="AssertExactAsync"/>: each term must
/// return exactly its own row, never the decoy that the character would match as a LIKE wildcard or escape.
/// Every row starts with a per-test marker, so other rows in the shared database can't match.
/// </summary>
public static class SearchCases
{
    /// <summary>Lower-case letters and digits, so an upper-cased term still has to match case-insensitively.</summary>
    public static string Marker() => "q" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>The rows to seed, in pairs: a literal match, then its decoy (at most 19 characters).</summary>
    public static IReadOnlyList<string> Rows(string marker) =>
    [
        $"{marker}-50% full", $"{marker}-500 seats",   // % as a wildcard would also match "500 seats"
        $"{marker}-a_b night", $"{marker}-axb night",  // _ as a wildcard would also match "axb"
        $@"{marker}-c\d path", $"{marker}-cd path",    // \ as an escape would make "\d" mean "d", matching "cd"
    ];

    /// <summary>(search term, the only row it may return).</summary>
    public static IReadOnlyList<(string Term, string Match)> Cases(string marker) =>
    [
        ($"{marker}-50%", $"{marker}-50% full"),
        ($"{marker}-a_b", $"{marker}-a_b night"),
        ($@"{marker}-c\d", $@"{marker}-c\d path"),
        // Trimming and case-insensitivity are unchanged.
        ($"  {marker.ToUpperInvariant()}-A_B  ", $"{marker}-a_b night"),
    ];

    /// <summary>The search term, URL-encoded for a query string.</summary>
    public static string Q(string term) => Uri.EscapeDataString(term);

    /// <summary>Runs every case through <paramref name="search"/> (term → the searched column of each returned row).</summary>
    public static async Task AssertExactAsync(string marker, Func<string, Task<IReadOnlyList<string>>> search, string target)
    {
        foreach (var (term, match) in Cases(marker))
            (await search(term)).Should().Equal([match], $"{target} searched for \"{term}\" must return exactly that row");
    }
}
