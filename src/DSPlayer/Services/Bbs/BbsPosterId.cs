using System.Text.RegularExpressions;

namespace DSPlayer.Services.Bbs;

/// <summary>Extract 2ch / したらば poster IDs from a date+ID field.</summary>
public static class BbsPosterId
{
    // ID:Ab12+./  /  ID:SNnL3gbc00  /  ID:xxxx???
    private static readonly Regex IdToken = new(
        @"\bID:([0-9A-Za-z+/.]{2,}\?{0,3})",
        RegexOptions.Compiled);

    public static string? Extract(string? dateId)
    {
        if (string.IsNullOrWhiteSpace(dateId))
            return null;

        var m = IdToken.Match(dateId);
        if (!m.Success)
            return null;

        var id = m.Groups[1].Value.TrimEnd('?');
        if (id.Length < 2)
            return null;
        if (id.All(c => c == '?'))
            return null;
        return id;
    }

    public static string DateWithoutId(string? dateId)
    {
        if (string.IsNullOrWhiteSpace(dateId))
            return "";

        var m = IdToken.Match(dateId);
        if (!m.Success)
            return dateId.Trim();

        var left = dateId[..m.Index].Trim();
        var right = dateId[(m.Index + m.Length)..].Trim();
        // drop a leftover BE:… or extra spaces
        if (right.StartsWith("BE:", StringComparison.OrdinalIgnoreCase))
            right = "";
        return string.IsNullOrWhiteSpace(right) ? left : (left + " " + right).Trim();
    }

    public static bool Same(string? a, string? b) =>
        !string.IsNullOrEmpty(a) &&
        !string.IsNullOrEmpty(b) &&
        a.Equals(b, StringComparison.Ordinal);
}
