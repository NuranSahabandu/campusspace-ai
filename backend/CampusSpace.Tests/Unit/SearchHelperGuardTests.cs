using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace CampusSpace.Tests.Unit;

/// <summary>
/// Source scan that keeps QueryableExtensions.WhereContains the only contains search. It reads every .cs file of
/// CampusSpace.Api (not bin/, obj/ or Data/Migrations/), splits each into code and string literals with a small C#
/// lexer (comments are dropped), and fails when
/// 1. the code calls Like(/ILike( (EF.Functions.Like/ILike, also after `using static`) outside QueryableExtensions.cs:
///    the two-argument ILike is translated with ESCAPE '', which breaks literal %, _ and \ matching; or
/// 2. a string literal holds an upper-case SQL LIKE/ILIKE operator (raw SQL search), anywhere in the API.
/// Lower-case "like" in messages and anything in comments are ignored.
/// </summary>
public class SearchHelperGuardTests
{
    private const string HelperFile = "Extensions/QueryableExtensions.cs";
    private static readonly Regex LikeCall = new(@"\bI?Like\s*\(", RegexOptions.Compiled);
    private static readonly Regex SqlLike = new(@"\bI?LIKE\b", RegexOptions.Compiled);

    [Fact]
    public void Only_the_helper_calls_ILike_or_Like_and_no_raw_SQL_uses_LIKE()
    {
        var files = ApiSourceFiles();
        files.Select(f => f.Relative).Should().Contain(HelperFile, "the scan must reach the API sources");
        files.Should().HaveCountGreaterThan(50, "the scan must reach the API sources");

        var offences = new List<string>();
        foreach (var (path, relative) in files)
        {
            var (code, strings) = Lex(File.ReadAllText(path));
            if (relative != HelperFile)
                offences.AddRange(Matches(LikeCall, code).Select(line => $"{relative}:{line} calls Like/ILike directly"));
            foreach (var (text, line) in strings)
                if (SqlLike.IsMatch(text))
                    offences.Add($"{relative}:{line} has LIKE/ILIKE in raw SQL");
        }

        offences.Should().BeEmpty("contains searches go only through QueryableExtensions.WhereContains");
    }

    [Fact]
    public void Lexer_ignores_comments_and_finds_calls_and_SQL_in_strings()
    {
        var (code, strings) = Lex(string.Join('\n',
            "// EF.Functions.ILike(x, y) in a comment",
            "/* LIKE */ var a = EF.Functions.ILike(x, \"%a%\"); var c = '\"';",
            "var m = \"Looks like nothing\"; var s = @\"SELECT 1 WHERE \"\"A\"\" ILIKE @p\";",
            "var r = \"\"\"",
            "  x LIKE y",
            "  \"\"\"; var i = $\"{(a ? \"Like(\" : \"b\")} done\"; var e = \"\";"));

        Matches(LikeCall, code).Should().Equal(2);
        strings.Where(s => SqlLike.IsMatch(s.Text)).Select(s => s.Line).Should().Equal(3, 4);
        strings.Select(s => s.Text).Should().Contain("Looks like nothing").And.Contain("");
    }

    private static IEnumerable<int> Matches(Regex regex, string code) =>
        regex.Matches(code).Select(m => code[..m.Index].Count(c => c == '\n') + 1);

    private static List<(string Path, string Relative)> ApiSourceFiles()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend", "CampusSpace.Api")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the tests run inside the repository checkout");
        var root = Path.Combine(dir!.FullName, "backend", "CampusSpace.Api");

        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(p => (Path: p, Relative: Path.GetRelativePath(root, p).Replace('\\', '/')))
            .Where(f => !f.Relative.StartsWith("bin/") && !f.Relative.StartsWith("obj/")
                && !f.Relative.StartsWith("Data/Migrations/"))
            .ToList();
    }

    /// <summary>
    /// Splits C# source into code (comments and literals blanked, newlines kept) and the string literals with their
    /// starting line. Handles //, /* */, "…" with escapes, @"…", raw """…""" (any quote count; their holes stay
    /// in the text), $"…{holes}…" and chars.
    /// </summary>
    private static (string Code, List<(string Text, int Line)> Strings) Lex(string src)
    {
        var code = new StringBuilder(src.Length);
        var strings = new List<(string, int)>();
        var line = 1;
        var i = 0;

        void Skip(int to, StringBuilder? into)
        {
            for (; i < to && i < src.Length; i++)
            {
                if (src[i] == '\n') { line++; code.Append('\n'); }
                into?.Append(src[i]);
            }
        }

        // An interpolation hole of a single-quote $"…" string: skips to its closing brace, past nested "…" literals.
        void SkipHole()
        {
            var depth = 0;
            while (i < src.Length)
            {
                var h = src[i];
                if (h == '"')
                {
                    var end = i + 1;
                    while (end < src.Length && src[end] != '"') end += src[end] == '\\' ? 2 : 1;
                    Skip(end + 1, null);
                    continue;
                }
                Skip(i + 1, null);
                if (h == '{') depth++;
                else if (h == '}' && --depth == 0) return;
            }
        }

        while (i < src.Length)
        {
            var c = src[i];
            var next = i + 1 < src.Length ? src[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                var end = src.IndexOf('\n', i);
                Skip(end < 0 ? src.Length : end, null);
            }
            else if (c == '/' && next == '*')
            {
                var end = src.IndexOf("*/", i + 2, StringComparison.Ordinal);
                Skip(end < 0 ? src.Length : end + 2, null);
            }
            else if (c is '"' or '@' or '$' && StringStart(src, i) is { } start)
            {
                var (open, verbatim, interpolated, quotes) = start;
                var startLine = line;
                var text = new StringBuilder();
                Skip(open, null);
                if (quotes >= 3)
                {
                    var close = new string('"', quotes);
                    var end = src.IndexOf(close, i, StringComparison.Ordinal);
                    Skip(end < 0 ? src.Length : end, text);
                    Skip(i + quotes, null);
                }
                else
                {
                    while (i < src.Length)
                    {
                        if (interpolated && src[i] == '{' && i + 1 < src.Length && src[i + 1] == '{') { text.Append("{{"); i += 2; }
                        else if (interpolated && src[i] == '{') SkipHole();
                        else if (verbatim && src[i] == '"' && i + 1 < src.Length && src[i + 1] == '"') { text.Append('"'); i += 2; }
                        else if (!verbatim && src[i] == '\\') { text.Append(src, i, Math.Min(2, src.Length - i)); i += 2; }
                        else if (src[i] == '"') { i++; break; }
                        else Skip(i + 1, text);
                    }
                }
                strings.Add((text.ToString(), startLine));
                code.Append("\"\"");
            }
            else if (c == '\'')
            {
                var end = i + 1;
                while (end < src.Length && src[end] != '\'') end += src[end] == '\\' ? 2 : 1;
                Skip(end + 1, null);
            }
            else
            {
                Skip(i + 1, null);
                if (c != '\n') code.Append(c);
            }
        }
        return (code.ToString(), strings);
    }

    /// <summary>A string literal starting at i: (index after the opening quotes, verbatim?, interpolated?, quote count) or null.</summary>
    private static (int Open, bool Verbatim, bool Interpolated, int Quotes)? StringStart(string src, int i)
    {
        var j = i;
        var verbatim = false;
        var interpolated = false;
        while (j < src.Length && src[j] is '$' or '@')
        {
            verbatim |= src[j] == '@';
            interpolated |= src[j] == '$';
            j++;
        }
        if (j >= src.Length || src[j] != '"') return null;
        // A $/@ prefix belongs to the literal only if it directly precedes the quote and isn't part of an identifier.
        if (j > i && i > 0 && (char.IsLetterOrDigit(src[i - 1]) || src[i - 1] == '_')) return null;
        var quotes = 0;
        while (j + quotes < src.Length && src[j + quotes] == '"') quotes++;
        // "" is an empty regular string, not the start of a raw one.
        if (quotes == 2) quotes = 1;
        return (j + quotes, verbatim, interpolated, quotes);
    }
}
