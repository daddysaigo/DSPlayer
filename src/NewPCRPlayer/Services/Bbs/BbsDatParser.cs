using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace NewPCRPlayer.Services.Bbs;

public static class BbsDatParser
{
    // 2ch-style: <dt>1 ：name：date</dt><dd>body</dd>
    // Shitaraba: <dt ...><a>1</a>：name：date</dt><dd>body</dd>
    private static readonly Regex HtmlDtDd = new(
        @"(?is)<dt[^>]*>\s*(?:<a[^>]*>)?\s*(?<num>\d+)\s*(?:</a>)?\s*：(?<header>.*?)</dt>\s*<dd[^>]*>(?<body>.*?)</dd>",
        RegexOptions.Compiled);

    private static readonly Regex StripTags = new(
        @"<[^>]+>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // shitaraba subject.txt: 1786107484.cgi,6157 はよPC買え(577)
    private static readonly Regex SubjectLineShitaraba = new(
        @"^(?<id>\d+)\.cgi,(?<title>.*?)\((?<count>\d+)\)\s*$",
        RegexOptions.Compiled);

    // 2ch-style subject.txt: 1786069077.dat<>title (265)
    private static readonly Regex SubjectLineTwoch = new(
        @"^(?<id>\d+)\.dat<>(?<title>.*?)\s*\((?<count>\d+)\)\s*$",
        RegexOptions.Compiled);

    public static IReadOnlyList<BbsPost> ParseTwochDat(string datText)
    {
        var posts = new List<BbsPost>();
        if (string.IsNullOrEmpty(datText))
            return posts;

        var lines = SplitLines(datText);
        var n = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            n++;
            var fields = line.Split(new[] { "<>" }, StringSplitOptions.None);
            var name = fields.Length > 0 ? DecodeEntities(fields[0]) : "";
            var mail = fields.Length > 1 ? DecodeEntities(fields[1]) : "";
            var dateId = fields.Length > 2 ? DecodeEntities(fields[2]) : "";
            var bodyHtml = fields.Length > 3 ? fields[3] : "";
            var title = fields.Length > 4 ? DecodeEntities(fields[4]) : null;

            posts.Add(new BbsPost
            {
                Number = n,
                Name = StripHtml(name).Trim(),
                Mail = mail.Trim(),
                DateId = dateId.Trim(),
                BodyHtml = bodyHtml,
                BodyText = HtmlToText(bodyHtml),
                Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
            });
        }

        return posts;
    }

    /// <summary>
    /// Shitaraba rawmode: number&lt;&gt;name&lt;&gt;mail&lt;&gt;date&lt;&gt;body&lt;&gt;title&lt;&gt;id
    /// </summary>
    public static IReadOnlyList<BbsPost> ParseShitarabaRaw(string rawText)
    {
        var posts = new List<BbsPost>();
        if (string.IsNullOrEmpty(rawText))
            return posts;

        foreach (var line in SplitLines(rawText))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = line.Split(new[] { "<>" }, StringSplitOptions.None);
            if (fields.Length < 5)
                continue;

            if (!int.TryParse(fields[0], out var num))
                continue;

            var name = DecodeEntities(fields[1]);
            var mail = DecodeEntities(fields[2]);
            var dateId = DecodeEntities(fields[3]);
            if (fields.Length > 6 && !string.IsNullOrWhiteSpace(fields[6]))
                dateId = $"{dateId} ID:{DecodeEntities(fields[6])}";

            var bodyHtml = fields[4];
            var title = fields.Length > 5 ? DecodeEntities(fields[5]) : null;

            posts.Add(new BbsPost
            {
                Number = num,
                Name = StripHtml(name).Trim(),
                Mail = mail.Trim(),
                DateId = dateId.Trim(),
                BodyHtml = bodyHtml,
                BodyText = HtmlToText(bodyHtml),
                Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
            });
        }

        return posts;
    }

    public static IReadOnlyList<BbsPost> ParseHtmlThread(string html)
    {
        var posts = new List<BbsPost>();
        foreach (Match m in HtmlDtDd.Matches(html))
        {
            if (!int.TryParse(m.Groups["num"].Value, out var num))
                continue;

            var header = m.Groups["header"].Value;
            var name = "";
            var nameMatch = Regex.Match(header, @"(?is)<b>(?<n>.*?)</b>");
            if (nameMatch.Success)
                name = StripHtml(DecodeEntities(nameMatch.Groups["n"].Value)).Trim();
            else
                name = StripHtml(DecodeEntities(header)).Trim();

            // Shitaraba: name：date ID:xxx  (second fullwidth colon)
            var dateId = "";
            var dateMatch = Regex.Match(header, @"：\s*(?<d>\d{4}/\d{2}/\d{2}[^<]*)");
            if (!dateMatch.Success)
                dateMatch = Regex.Match(header, @"：\s*(?<d>[^<]+)$");
            if (dateMatch.Success)
                dateId = DecodeEntities(dateMatch.Groups["d"].Value).Trim();

            var bodyHtml = m.Groups["body"].Value;
            posts.Add(new BbsPost
            {
                Number = num,
                Name = name,
                DateId = dateId,
                BodyHtml = bodyHtml,
                BodyText = HtmlToText(bodyHtml),
            });
        }

        return posts;
    }

    /// <summary>
    /// Parse subject.txt (したらば / 2ch 系). Newest-first where the board lists it that way.
    /// </summary>
    public static IReadOnlyList<BbsSubjectEntry> ParseSubjectTxt(string text)
    {
        var list = new List<BbsSubjectEntry>();
        if (string.IsNullOrEmpty(text))
            return list;

        foreach (var line in SplitLines(text))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var trimmed = line.Trim();

            var m2 = SubjectLineTwoch.Match(trimmed);
            if (m2.Success)
            {
                list.Add(new BbsSubjectEntry(
                    m2.Groups["id"].Value,
                    DecodeEntities(m2.Groups["title"].Value.Trim()),
                    int.Parse(m2.Groups["count"].Value)));
                continue;
            }

            var m = SubjectLineShitaraba.Match(trimmed);
            if (m.Success)
            {
                list.Add(new BbsSubjectEntry(
                    m.Groups["id"].Value,
                    m.Groups["title"].Value.Trim(),
                    int.Parse(m.Groups["count"].Value)));
                continue;
            }

            // Fallback: id.cgi,title  /  id.dat<>title
            var cgiIdx = trimmed.IndexOf(".cgi,", StringComparison.Ordinal);
            if (cgiIdx > 0)
            {
                var id = trimmed[..cgiIdx];
                if (id.All(char.IsDigit))
                {
                    var rest = trimmed[(cgiIdx + 5)..];
                    var count = 0;
                    var cm = Regex.Match(rest, @"\((\d+)\)\s*$");
                    if (cm.Success)
                    {
                        count = int.Parse(cm.Groups[1].Value);
                        rest = rest[..cm.Index].TrimEnd();
                    }
                    list.Add(new BbsSubjectEntry(id, rest, count));
                }
                continue;
            }

            var datIdx = trimmed.IndexOf(".dat<>", StringComparison.Ordinal);
            if (datIdx > 0)
            {
                var id = trimmed[..datIdx];
                if (id.All(char.IsDigit))
                {
                    var rest = trimmed[(datIdx + 6)..];
                    var count = 0;
                    var cm = Regex.Match(rest, @"\((\d+)\)\s*$");
                    if (cm.Success)
                    {
                        count = int.Parse(cm.Groups[1].Value);
                        rest = rest[..cm.Index].TrimEnd();
                    }
                    list.Add(new BbsSubjectEntry(id, DecodeEntities(rest), count));
                }
            }
        }

        return list;
    }

    /// <summary>
    /// Pick next open thread after a 1000-capped one.
    /// Prefer newest subject entry with ResCount &lt; 1000 other than currentId.
    /// </summary>
    public static BbsSubjectEntry? PickNextOpenThread(
        IReadOnlyList<BbsSubjectEntry> subjects,
        string? currentThreadId)
    {
        if (subjects.Count == 0)
            return null;

        // Prefer first open thread that is not the current (subject is usually newest-first)
        foreach (var s in subjects)
        {
            if (s.ResCount >= 1000)
                continue;
            if (!string.IsNullOrEmpty(currentThreadId) &&
                s.ThreadId.Equals(currentThreadId, StringComparison.Ordinal))
                continue;
            return s;
        }

        // All full or only current open — fall back to absolute first entry
        return subjects[0];
    }

    public static string HtmlToText(string html)
    {
        if (string.IsNullOrEmpty(html))
            return "";

        var t = html;
        t = Regex.Replace(t, @"(?i)<br\s*/?>", "\n");
        t = Regex.Replace(t, @"(?i)</?div[^>]*>", "\n");
        t = Regex.Replace(t, @"(?i)</p>", "\n");
        t = StripHtml(t);
        t = DecodeEntities(t);
        t = Regex.Replace(t, @"[ \t]+\n", "\n");
        t = Regex.Replace(t, @"\n{3,}", "\n\n");
        return t.Trim();
    }

    public static string StripHtml(string s) =>
        string.IsNullOrEmpty(s) ? "" : StripTags.Replace(s, "");

    public static string DecodeEntities(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        return WebUtility.HtmlDecode(s);
    }

    public static Encoding GetEncoding(BbsBoardKind kind, byte[] bytes, string? contentTypeCharset = null)
    {
        // Explicit Content-Type charset first
        if (!string.IsNullOrWhiteSpace(contentTypeCharset))
        {
            var resolved = TryGetEncoding(contentTypeCharset);
            if (resolved is not null)
                return resolved;
        }

        // HTML meta charset
        var headLen = Math.Min(bytes.Length, 2048);
        var head = Encoding.ASCII.GetString(bytes, 0, headLen);
        var m = Regex.Match(head, @"charset\s*=\s*[""']?(?<c>[\w-]+)", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var resolved = TryGetEncoding(m.Groups["c"].Value);
            if (resolved is not null)
                return resolved;
        }

        // Kind defaults: Shitaraba = EUC-JP, others = Shift_JIS
        if (kind == BbsBoardKind.Shitaraba)
            return TryGetEncoding("euc-jp") ?? Encoding.UTF8;

        return TryGetEncoding("shift_jis") ?? Encoding.UTF8;
    }

    /// <summary>Legacy helper — prefer GetEncoding(kind, ...).</summary>
    public static Encoding DetectEncoding(byte[] bytes, string? contentTypeCharset = null) =>
        GetEncoding(BbsBoardKind.TwochStyle, bytes, contentTypeCharset);

    public static Encoding? TryGetEncoding(string name)
    {
        try
        {
            // Normalize common aliases
            name = name.Trim().ToLowerInvariant() switch
            {
                "shift-jis" or "shift_jis" or "sjis" or "x-sjis" or "windows-31j" or "cp932" => "shift_jis",
                "euc-jp" or "euc_jp" or "x-euc-jp" => "euc-jp",
                "utf8" => "utf-8",
                _ => name,
            };
            return Encoding.GetEncoding(name);
        }
        catch
        {
            return null;
        }
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
}

public sealed record BbsSubjectEntry(string ThreadId, string Title, int ResCount);
