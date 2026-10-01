using System.Text;

namespace CampusSpace.Api.Notifications;

/// <summary>Small text helpers shared by the templates and the .ics.</summary>
public static class EmailText
{
    /// <summary>At most <paramref name="maxChars"/> characters (Unicode scalars, so a surrogate pair is never split), trimmed.</summary>
    public static string Cut(string value, int maxChars)
    {
        var builder = new StringBuilder();
        var count = 0;
        foreach (var rune in value.Trim().EnumerateRunes())
        {
            if (count++ == maxChars)
                break;
            builder.Append(rune.ToString());
        }
        return builder.ToString().TrimEnd();
    }

    /// <summary>One line for a subject: control characters (including CR/LF) become spaces and runs of spaces collapse.</summary>
    public static string SingleLine(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
            builder.Append(Rune.IsControl(rune) || Rune.IsWhiteSpace(rune) ? " " : rune.ToString());
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
