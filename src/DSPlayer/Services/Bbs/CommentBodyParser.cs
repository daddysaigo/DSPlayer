using System.IO;
using System.Text.RegularExpressions;

namespace DSPlayer.Services.Bbs;

public enum CommentSegmentKind
{
    Text,
    Anchor,
    Url,
    Image,
}

public readonly record struct CommentSegment(
    CommentSegmentKind Kind,
    string Text,
    int ResNumber = 0,
    string? NavigateUrl = null);

/// <summary>
/// Split a comment body into text, &gt;&gt;N anchors, and http(s)/ttp(s) URLs.
/// Image vs link is extension-based — no network.
/// </summary>
public static class CommentBodyParser
{
    private static readonly Regex AnchorRegex = new(
        @"(>>|＞＞|>)(\d{1,4})",
        RegexOptions.Compiled);

    // 2ch-style ttp/ttps (missing h) plus normal http(s)
    private static readonly Regex UrlRegex = new(
        @"(?i)h?ttps?://[^\s<>""'）\]】」』]+",
        RegexOptions.Compiled);

    private static readonly char[] TrailingPunct =
    {
        '.', ',', ';', ':', '!', '?', '。', '、',
        ')', '）', ']', '】', '」', '』', '>', '＞', '"', '\'',
    };

    public static IReadOnlyList<CommentSegment> Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<CommentSegment>();

        var hits = new List<(int Index, int Length, CommentSegment Seg)>(8);

        foreach (Match m in AnchorRegex.Matches(text))
        {
            if (!int.TryParse(m.Groups[2].Value, out var num) || num <= 0)
                continue;
            hits.Add((m.Index, m.Length, new CommentSegment(CommentSegmentKind.Anchor, m.Value, ResNumber: num)));
        }

        foreach (Match m in UrlRegex.Matches(text))
        {
            var raw = TrimTrailingPunct(m.Value);
            if (raw.Length < 8)
                continue;
            var href = NormalizeHref(raw);
            if (!IsHttpUrl(href))
                continue;
            var kind = LooksLikeImage(href) ? CommentSegmentKind.Image : CommentSegmentKind.Url;
            hits.Add((m.Index, raw.Length, new CommentSegment(kind, raw, NavigateUrl: href)));
        }

        if (hits.Count == 0)
            return new[] { new CommentSegment(CommentSegmentKind.Text, text) };

        hits.Sort((a, b) =>
        {
            var c = a.Index.CompareTo(b.Index);
            return c != 0 ? c : b.Length.CompareTo(a.Length);
        });

        var list = new List<CommentSegment>(hits.Count * 2);
        var idx = 0;
        foreach (var hit in hits)
        {
            if (hit.Index < idx)
                continue;
            if (hit.Index > idx)
                list.Add(new CommentSegment(CommentSegmentKind.Text, text[idx..hit.Index]));
            list.Add(hit.Seg);
            idx = hit.Index + hit.Length;
        }

        if (idx < text.Length)
            list.Add(new CommentSegment(CommentSegmentKind.Text, text[idx..]));

        return list;
    }

    public static string NormalizeHref(string raw)
    {
        var s = (raw ?? "").Trim();
        if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return s;
        if (s.StartsWith("ttps://", StringComparison.OrdinalIgnoreCase))
            return "https://" + s[7..];
        if (s.StartsWith("ttp://", StringComparison.OrdinalIgnoreCase))
            return "http://" + s[6..];
        return s;
    }

    public static bool IsHttpUrl(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return false;
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri))
            return false;
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    /// <summary>
    /// True when the path (or Twitter-style format= query / pbs.twimg.com host)
    /// is a type we can try to decode. WebP stays a link — BitmapImage cannot decode it.
    /// </summary>
    public static bool LooksLikeImage(string? href)
    {
        if (string.IsNullOrEmpty(href) || !IsHttpUrl(href))
            return false;
        var uri = new Uri(href);
        var ext = ImageExtension(uri);

        if (ext is "mp4" or "m3u8" or "webm" or "mov")
            return false;

        if (IsTwimgHost(uri.Host))
            return ext is not "webp";

        if (ext is "jpg" or "jpeg" or "png" or "gif" or "bmp")
            return true;

        var q = uri.Query;
        if (string.IsNullOrEmpty(q))
            return false;
        return q.Contains("format=jpg", StringComparison.OrdinalIgnoreCase) ||
               q.Contains("format=jpeg", StringComparison.OrdinalIgnoreCase) ||
               q.Contains("format=png", StringComparison.OrdinalIgnoreCase) ||
               q.Contains("format=gif", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTwimgHost(string? host) =>
        !string.IsNullOrEmpty(host) &&
        (host.Equals("pbs.twimg.com", StringComparison.OrdinalIgnoreCase) ||
         host.EndsWith(".twimg.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Twitter media URLs often have no file extension. Fetch a modest <c>name=small</c>
    /// variant so the comment column stays light; the original href is kept for the browser.
    /// </summary>
    public static string ToFetchUrl(string href)
    {
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri) || !IsTwimgHost(uri.Host))
            return href;

        var path = uri.AbsolutePath;
        var colon = path.IndexOf(':');
        if (colon >= 0)
            path = path[..colon];

        if (path.IndexOf("/media/", StringComparison.OrdinalIgnoreCase) < 0 &&
            path.IndexOf("_thumb", StringComparison.OrdinalIgnoreCase) < 0 &&
            path.IndexOf("profile_images", StringComparison.OrdinalIgnoreCase) < 0)
            return href;

        var format = QueryValue(uri, "format");
        if (string.IsNullOrEmpty(format))
        {
            var ext = ImageExtension(uri);
            format = ext is "jpg" or "jpeg" or "png" or "gif" or "webp"
                ? (ext == "jpeg" ? "jpg" : ext)
                : "jpg";
        }

        return uri.GetLeftPart(UriPartial.Authority) + path + "?format=" + format + "&name=small";
    }

    private static string ImageExtension(Uri uri)
    {
        var path = uri.AbsolutePath;
        var colon = path.IndexOf(':');
        if (colon >= 0)
            path = path[..colon];
        return Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
    }

    private static string? QueryValue(Uri uri, string key)
    {
        var q = uri.Query;
        if (string.IsNullOrEmpty(q))
            return null;
        var prefix = key + "=";
        foreach (var part in q.TrimStart('?').Split('&'))
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return part[prefix.Length..];
        }
        return null;
    }

    public static bool IsLoopbackUrl(string? href)
    {
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri))
            return false;
        return uri.IsLoopback;
    }

    private static string TrimTrailingPunct(string s)
    {
        var end = s.Length;
        while (end > 0 && Array.IndexOf(TrailingPunct, s[end - 1]) >= 0)
            end--;
        return end == s.Length ? s : s[..end];
    }
}
