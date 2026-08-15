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

    // 2ch-style board via read.cgi without a thread id
    private static readonly Regex TwochReadBoard = new(
        @"^https?://(?<host>[^/]+)/(?:test/)?read\.cgi/(?<board>[^/]+)/?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // 2ch-style board top or subject.txt: https://bbs.jpnkn.com/ubereats/
    private static readonly Regex TwochBoard = new(
        @"^https?://(?<host>[^/]+)/(?<board>[a-zA-Z0-9_-]+)(?:/(?:subject\.txt)?)?$",
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

    /// <summary>
    /// Client-side wait after a successful post. See <see cref="BbsWriteCooldown"/>.
    /// 0 = no extra client cooldown.
    /// </summary>
    public int DefaultPostCooldownSeconds => BbsWriteCooldown.DefaultIntervalSeconds(this);

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

        // --- 2ch-style thread (not shitaraba host) ---
        var two = TwochRead.Match(url);
        if (two.Success && !IsShitarabaHost(url))
        {
            var host = two.Groups["host"].Value;
            var board = two.Groups["board"].Value;
            var thread = two.Groups["thread"].Value;

            return CreateTwoch(url, scheme, host, board, thread);
        }

        // --- 2ch-style board via read.cgi (no thread) ---
        var twoBoardRead = TwochReadBoard.Match(url);
        if (twoBoardRead.Success && !IsShitarabaHost(url))
        {
            return CreateTwoch(
                url, scheme,
                twoBoardRead.Groups["host"].Value,
                twoBoardRead.Groups["board"].Value,
                threadId: null);
        }

        // --- 2ch-style board top / subject.txt (jpnkn etc.) ---
        var twoBoard = TwochBoard.Match(url);
        if (twoBoard.Success && !IsShitarabaHost(url) && IsLikelyTwochBoardHost(url))
        {
            return CreateTwoch(
                url, scheme,
                twoBoard.Groups["host"].Value,
                twoBoard.Groups["board"].Value,
                threadId: null);
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

    /// <summary>
    /// Board-top URLs like https://bbs.jpnkn.com/ubereats/ should resolve via subject.txt.
    /// Keep this conservative so random contact pages stay Unknown/HTML.
    /// </summary>
    private static bool IsLikelyTwochBoardHost(string url)
    {
        if (url.Contains("jpnkn", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("open2ch", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("bbspink", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("5ch.", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("2ch.", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("2ch.net", StringComparison.OrdinalIgnoreCase))
            return true;

        // "bbs." in host (bbs.jpnkn.com already matched; also bbs.example.net/board/)
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                uri.Host.StartsWith("bbs.", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch
        {
            // ignore
        }

        return url.Contains("/subject.txt", StringComparison.OrdinalIgnoreCase);
    }

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
        string contactUrl, string scheme, string host, string board, string? threadId)
    {
        string? dat = null;
        string? html = null;
        string? write = null;
        if (!string.IsNullOrEmpty(threadId))
        {
            dat = $"{scheme}://{host}/{board}/dat/{threadId}.dat";
            html = $"{scheme}://{host}/test/read.cgi/{board}/{threadId}/";
            write = $"{scheme}://{host}/test/bbs.cgi?guid=ON";
        }

        return new BbsThreadRef
        {
            ContactUrl = contactUrl,
            Kind = BbsBoardKind.TwochStyle,
            Host = host,
            Board = board,
            ThreadId = threadId,
            IsBoardOnly = string.IsNullOrEmpty(threadId),
            SubjectUrl = $"{scheme}://{host}/{board}/subject.txt",
            DatUrl = dat,
            HtmlUrl = html,
            WriteUrl = write,
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
