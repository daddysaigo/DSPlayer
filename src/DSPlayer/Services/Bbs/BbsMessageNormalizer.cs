using System.Text.RegularExpressions;

namespace DSPlayer.Services.Bbs;

/// <summary>
/// PCRPlayer.xml &lt;board&gt;&lt;message&gt; rules ported for plain text bodies
/// (after HTML→text conversion, &lt;br&gt; is already newline).
/// </summary>
public static class BbsMessageNormalizer
{
    // Continuous w/ｗ (not part of identifiers)
    private static readonly Regex WwCollapse = new(
        @"[wWｗＷ]{2,}(?![A-Za-z0-9_%&\-/=])",
        RegexOptions.Compiled);

    // 3+ newlines → 2 (one blank line)
    private static readonly Regex MultiBlank = new(
        @"\n[ \t\u3000]*\n(?:[ \t\u3000]*\n)+",
        RegexOptions.Compiled);

    /// <summary>
    /// Apply PCRPlayer-compatible cleanup. Safe on already-clean text.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? "";

        var s = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // Leading blank lines
        s = Regex.Replace(s, @"^(?:[ \t\u3000]*\n)+", "");
        // Trailing blank lines
        s = Regex.Replace(s, @"(?:\n[ \t\u3000]*)+$", "");

        // Spaces after newline / end of line
        s = Regex.Replace(s, @"\n[ \t\u3000]+", "\n");
        s = Regex.Replace(s, @"[ \t\u3000]+\n", "\n");
        s = Regex.Replace(s, @"[ \t\u3000]+$", "", RegexOptions.Multiline);

        // Collapse 2+ blank lines to one blank line
        s = MultiBlank.Replace(s, "\n\n");

        // Continuous w → ｗｗｗ
        s = WwCollapse.Replace(s, "ｗｗｗ");

        return s.Trim();
    }

    public static BbsPost NormalizePost(BbsPost post)
    {
        if (post is null) throw new ArgumentNullException(nameof(post));
        var body = Normalize(post.BodyText);
        if (body == post.BodyText)
            return post;

        return new BbsPost
        {
            Number = post.Number,
            Name = post.Name,
            Mail = post.Mail,
            DateId = post.DateId,
            BodyHtml = post.BodyHtml,
            BodyText = body,
            Title = post.Title,
        };
    }

    public static IReadOnlyList<BbsPost> NormalizePosts(IReadOnlyList<BbsPost> posts)
    {
        if (posts is null || posts.Count == 0)
            return posts ?? Array.Empty<BbsPost>();

        var list = new List<BbsPost>(posts.Count);
        foreach (var p in posts)
            list.Add(NormalizePost(p));
        return list;
    }
}
