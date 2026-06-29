using System.Text.RegularExpressions;

namespace Nexus.Core.Parsing;

public static class StdoutSanitizer
{
    private static readonly Regex AnsiPattern =
        new(@"\x1B\[[0-9;]*[a-zA-Z]|\x1B\][^\x07]*\x07|\x1B[^[]", RegexOptions.Compiled);

    private const string JsonStart = "<<<JSON>>>";
    private const string JsonEnd = "<<<END>>>";

    public static string StripAnsi(string raw) =>
        AnsiPattern.Replace(raw, string.Empty);

    public static string? ExtractJson(string raw)
    {
        var clean = StripAnsi(raw);

        // Strategy 1: explicit delimiters
        var startIdx = clean.IndexOf(JsonStart, StringComparison.Ordinal);
        var endIdx = clean.IndexOf(JsonEnd, StringComparison.Ordinal);
        if (startIdx >= 0 && endIdx > startIdx)
        {
            var content = clean[(startIdx + JsonStart.Length)..endIdx].Trim();
            if (content.StartsWith('{') || content.StartsWith('['))
                return content;
        }

        // Strategy 2: brace-count to find first balanced {...}
        var firstBrace = clean.IndexOf('{');
        if (firstBrace < 0)
            return null;

        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = firstBrace; i < clean.Length; i++)
        {
            var ch = clean[i];

            if (escape) { escape = false; continue; }

            if (ch == '\\' && inString) { escape = true; continue; }

            if (ch == '"') { inString = !inString; continue; }

            if (inString) continue;

            if (ch == '{') depth++;
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                    return clean[firstBrace..(i + 1)];
            }
        }

        return null;
    }
}
