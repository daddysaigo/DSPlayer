using System.Text.RegularExpressions;

namespace DSPlayer.Services.Bbs;

public enum BbsBoardKind
{
    Unknown,
    /// <summary>read.cgi/{board}/{thread}/ + {board}/dat/{thread}.dat (Shift_JIS)</summary>
    TwochStyle,
    /// <summary>jbbs / shitaraba (EUC-JP)</summary>
    Shitaraba,
}

/// <summary>
/// Contact URL から掲示板種別・DAT/生取得 URL を解決する。
/// </summary>
public sealed class BbsThreadRef
{
    // Full thread: .../bbs/read.cgi/{category}/{board}/{thread}/
    private static readonly Regex ShitarabaThread = new(
        @"^https?://(?<host>[^/]*shitaraba\.[^/]+|[^/]*jbbs\.[^/]+|jbbs\.livedoor\.[^/]+)/(?:bbs/)?read\.cgi/(?<category>[^/]+)/(?<board>\d+)/(?<thread>\d+)/?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Board only: https://jbbs.shitaraba.net/{category}/{board}/
    private static readonly Regex ShitarabaBoard = new(
        @"^https?://(?<host>[^/]*shitaraba\.[^/]+|[^/]*jbbs\.[^/]+|jbbs\.livedoor\.[^/]+)/(?<category>[a-zA-Z0-9_-]+)/(?<board>\d+)/?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Board via subject.cgi / without thread
    private static readonly Regex ShitarabaBoardAlt = new(
        @"^https?://(?<host>[^/]*shitaraba\.[^/]+|[^/]*jbbs\.[^/]+)/(?:bbs/)?(?:subject\.cgi|read\.cgi)/(?<category>[^/]+)/(?<board>\d+)/?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TwochRead = new(
        @"^https?://(?<host>[^/]+)/(?:test/)?read\.cgi/(?<board>[^/]+)/(?<thread>\d+)/?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public required string ContactUrl { get; init; }
    public BbsBoardKind Kind { get; init; }
    public string? Host { get; init; }
    public string? Board { get; init; }
    public string? Category { get; init; }
    public string? ThreadId { get; init; }
    /// <summary>True when contact is board top; thread is resolved via subject.txt.</summary>
    public bool IsBoardOnly { get; init; }
    public string? SubjectUrl { get; init; }
    public string? DatUrl { get; init; }
    public string? HtmlUrl { get; init; }
    /// <summary>POST endpoint for writing a reply (null if not writable yet).</summary>
    public string? WriteUrl { get; init; }
    public string DefaultEncodingName { get; init; } = "shift_jis";

    public bool CanFetch =>
        !string.IsNullOrWhiteSpace(DatUrl) ||
        !string.IsNullOrWhiteSpace(HtmlUrl) ||
        !string.IsNullOrWhiteSpace(SubjectUrl);

    public bool CanWrite =>
        !string.IsNullOrWhiteSpace(WriteUrl) &&
        !string.IsNullOrWhiteSpace(ThreadId) &&
        !string.IsNullOrWhiteSpace(Board);

    public static BbsThreadRef? TryParse(string? contactUrl)
    {
        if (string.IsNullOrWhiteSpace(contactUrl))
            return null;

        // PCRPlayer.xml <url> rewrites (livedoor → shitaraba, lite, subject.cgi)
        var url = BbsUrlRewriter.Rewrite(contactUrl.Trim());
        var scheme = url.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";

        // --- Shitaraba / JBBS thread ---
        var st = ShitarabaThread.Match(url);
        if (st.Success && IsShitarabaHost(url))
        {
            return CreateShitaraba(
                url, scheme,
                st.Groups["host"].Value,
                st.Groups["category"].Value,
                st.Groups["board"].Value,
                st.Groups["thread"].Value,
                boardOnly: false);
        }

        // --- Shitaraba board only ---
        var sb = ShitarabaBoard.Match(url);
        if (!sb.Success)
            sb = ShitarabaBoardAlt.Match(url);

        if (sb.Success && IsShitarabaHost(url))
        {
            return CreateShitaraba(
                url, scheme,
                sb.Groups["host"].Value,
                sb.Groups["category"].Value,
                sb.Groups["board"].Value,
                threadId: null,
                boardOnly: true);
        }

        // --- 2ch-style (not shitaraba host) ---
        var two = TwochRead.Match(url);
        if (two.Success && !IsShitarabaHost(url))
        {
            var host = two.Groups["host"].Value;
            var board = two.Groups["board"].Value;
            var thread = two.Groups["thread"].Value;

            return CreateTwoch(url, scheme, host, board, thread);
        }

        // Unknown: HTML of the URL itself
        return new BbsThreadRef
        {
            ContactUrl = url,
            Kind = BbsBoardKind.Unknown,
            HtmlUrl = url,
            DefaultEncodingName = "shift_jis",
        };
    }

    private static bool IsShitarabaHost(string url) =>
        url.Contains("shitaraba", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("jbbs.", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("livedoor.jp", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("livedoor.com", StringComparison.OrdinalIgnoreCase);

    private static BbsThreadRef CreateShitaraba(
        string contactUrl,
        string scheme,
        string host,
        string category,
        string board,
        string? threadId,
        bool boardOnly)
    {
        string? dat = null;
        string? html = null;
        string? write = null;
        if (!string.IsNullOrEmpty(threadId))
        {
            dat = $"{scheme}://{host}/bbs/rawmode.cgi/{category}/{board}/{threadId}/";
            html = $"{scheme}://{host}/bbs/read.cgi/{category}/{board}/{threadId}/";
            write = $"{scheme}://{host}/bbs/write.cgi/{category}/{board}/{threadId}/";
        }

        // subject.txt lives at /{category}/{board}/subject.txt
        var subject = $"{scheme}://{host}/{category}/{board}/subject.txt";

        return new BbsThreadRef
        {
            ContactUrl = contactUrl,
            Kind = BbsBoardKind.Shitaraba,
            Host = host,
            Category = category,
            Board = board,
            ThreadId = threadId,
            IsBoardOnly = boardOnly || string.IsNullOrEmpty(threadId),
            SubjectUrl = subject,
            DatUrl = dat,
            HtmlUrl = html,
            WriteUrl = write,
            DefaultEncodingName = "euc-jp",
        };
    }

    private static BbsThreadRef CreateTwoch(
        string contactUrl, string scheme, string host, string board, string threadId)
    {
        return new BbsThreadRef
        {
            ContactUrl = contactUrl,
            Kind = BbsBoardKind.TwochStyle,
            Host = host,
            Board = board,
            ThreadId = threadId,
            IsBoardOnly = false,
            SubjectUrl = $"{scheme}://{host}/{board}/subject.txt",
            DatUrl = $"{scheme}://{host}/{board}/dat/{threadId}.dat",
            HtmlUrl = $"{scheme}://{host}/test/read.cgi/{board}/{threadId}/",
            WriteUrl = $"{scheme}://{host}/test/bbs.cgi?guid=ON",
            DefaultEncodingName = "shift_jis",
        };
    }

    /// <summary>
    /// subject から選んだ thread を結びつける（したらば / 2ch 系）。
    /// </summary>
    public BbsThreadRef WithThread(string threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId) || Host is null || Board is null)
            return this;

        var scheme = ContactUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";

        if (Kind == BbsBoardKind.Shitaraba)
        {
            return CreateShitaraba(
                ContactUrl, scheme, Host, Category ?? "", Board, threadId, boardOnly: false);
        }

        if (Kind == BbsBoardKind.TwochStyle)
            return CreateTwoch(ContactUrl, scheme, Host, Board, threadId);

        return this;
    }
}
