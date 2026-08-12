using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace DSPlayer.Services.Bbs;

public sealed class BbsFetchResult
{
    public required IReadOnlyList<BbsPost> Posts { get; init; }
    public string? ThreadTitle { get; init; }
    public string? SourceUrl { get; init; }
    public string SourceKind { get; init; } = "";
    public string? ResolvedThreadId { get; init; }
}

public sealed class BbsClient : IDisposable
{
    public const string DefaultUserAgent = "Monazilla/1.00 (DSPlayer/1.00)";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly bool _normalizeMessages;

    /// <summary>Sticky thread id for board-only contacts (resolved once per session).</summary>
    private string? _stickyThreadId;

    public BbsClient(HttpClient? http = null, string? userAgent = null, bool normalizeMessages = true)
    {
        _normalizeMessages = normalizeMessages;
        if (http is null)
        {
            _http = CreateHttp(userAgent ?? DefaultUserAgent);
            _ownsHttp = true;
        }
        else
        {
            _http = http;
            _ownsHttp = false;
        }
    }

    private static HttpClient CreateHttp(string userAgent)
    {
        var c = new HttpClient();
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        c.Timeout = TimeSpan.FromSeconds(15);
        return c;
    }

    public void ResetStickyThread() => _stickyThreadId = null;

    public void SetStickyThread(string threadId) => _stickyThreadId = threadId;

    public string? StickyThreadId => _stickyThreadId;

    public async Task<IReadOnlyList<BbsSubjectEntry>> FetchSubjectListAsync(
        BbsThreadRef board, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (string.IsNullOrWhiteSpace(board.SubjectUrl))
            return Array.Empty<BbsSubjectEntry>();

        var (bytes, charset) = await DownloadAsync(board.SubjectUrl, ct).ConfigureAwait(false);
        var enc = BbsDatParser.GetEncoding(board.Kind, bytes, charset);
        var text = enc.GetString(bytes);
        return BbsDatParser.ParseSubjectTxt(text);
    }

    public async Task<BbsFetchResult> FetchAsync(BbsThreadRef thread, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(thread);

        // Resolve board-only / sticky thread via subject.txt
        if ((thread.Kind == BbsBoardKind.Shitaraba || thread.Kind == BbsBoardKind.TwochStyle) &&
            (thread.IsBoardOnly || string.IsNullOrEmpty(thread.ThreadId) || !string.IsNullOrEmpty(_stickyThreadId)))
        {
            if (!string.IsNullOrEmpty(_stickyThreadId))
                thread = thread.WithThread(_stickyThreadId);
            else if (thread.IsBoardOnly || string.IsNullOrEmpty(thread.ThreadId))
                thread = await ResolveBoardThreadAsync(thread, ct).ConfigureAwait(false);
        }

        Exception? lastError = null;

        if (!string.IsNullOrWhiteSpace(thread.DatUrl))
        {
            try
            {
                var result = await FetchDatAsync(thread, ct).ConfigureAwait(false);
                if (result.Posts.Count > 0)
                    return result;
                lastError = new InvalidOperationException("DAT/rawmode returned 0 posts");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }
        }

        var htmlUrl = thread.HtmlUrl ?? thread.ContactUrl;
        if (!string.IsNullOrWhiteSpace(htmlUrl))
        {
            try
            {
                var result = await FetchHtmlAsync(thread, htmlUrl, ct).ConfigureAwait(false);
                if (result.Posts.Count > 0)
                    return result;
                lastError = new InvalidOperationException(
                    "HTML returned 0 posts (" + (lastError?.Message ?? "no prior error") + ")");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException(
            "掲示板の取得に失敗しました: " + (lastError?.Message ?? "unknown"),
            lastError);
    }

    private async Task<BbsThreadRef> ResolveBoardThreadAsync(BbsThreadRef board, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_stickyThreadId))
            return board.WithThread(_stickyThreadId);

        var subjects = await FetchSubjectListAsync(board, ct).ConfigureAwait(false);
        if (subjects.Count == 0)
            throw new InvalidOperationException("subject.txt にスレッドがありません: " + board.SubjectUrl);

        var pick = subjects.FirstOrDefault(s => s.ResCount < 1000) ?? subjects[0];
        _stickyThreadId = pick.ThreadId;
        return board.WithThread(pick.ThreadId);
    }

    /// <summary>
    /// If current sticky thread is full (>=1000), move sticky to next open thread.
    /// Returns the new thread id if switched, otherwise null.
    /// </summary>
    public async Task<string?> TryAutoAdvanceIfFullAsync(
        BbsThreadRef board, string? currentThreadId, int currentResCount, CancellationToken ct = default)
    {
        if (currentResCount < 1000)
            return null;

        var subjects = await FetchSubjectListAsync(board, ct).ConfigureAwait(false);
        var next = BbsDatParser.PickNextOpenThread(subjects, currentThreadId);
        if (next is null)
            return null;
        if (!string.IsNullOrEmpty(currentThreadId) &&
            next.ThreadId.Equals(currentThreadId, StringComparison.Ordinal))
            return null;

        _stickyThreadId = next.ThreadId;
        return next.ThreadId;
    }

    private async Task<BbsFetchResult> FetchDatAsync(BbsThreadRef thread, CancellationToken ct)
    {
        var (bytes, charset) = await DownloadAsync(thread.DatUrl!, ct).ConfigureAwait(false);
        var enc = BbsDatParser.GetEncoding(thread.Kind, bytes, charset);
        var text = enc.GetString(bytes);

        IReadOnlyList<BbsPost> posts = thread.Kind == BbsBoardKind.Shitaraba
            ? BbsDatParser.ParseShitarabaRaw(text)
            : BbsDatParser.ParseTwochDat(text);

        if (posts.Count == 0 && thread.Kind == BbsBoardKind.Shitaraba)
            posts = BbsDatParser.ParseTwochDat(text);
        else if (posts.Count == 0)
            posts = BbsDatParser.ParseShitarabaRaw(text);

        // Title: first res title, or any non-empty title field
        string? title = posts.Select(p => p.Title).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

        if (_normalizeMessages)
            posts = BbsMessageNormalizer.NormalizePosts(posts);

        return new BbsFetchResult
        {
            Posts = posts,
            ThreadTitle = title,
            SourceUrl = thread.DatUrl,
            SourceKind = thread.Kind == BbsBoardKind.Shitaraba ? "rawmode/" + enc.WebName : "dat/" + enc.WebName,
            ResolvedThreadId = thread.ThreadId,
        };
    }

    private async Task<BbsFetchResult> FetchHtmlAsync(BbsThreadRef thread, string url, CancellationToken ct)
    {
        var candidates = new List<string>();
        var trimmed = url.TrimEnd('/');
        // Shitaraba: /l50 works; avoid inventing paths that 404
        candidates.Add(trimmed + "/l100");
        candidates.Add(trimmed + "/");
        candidates.Add(url);

        byte[]? bytes = null;
        string? charset = null;
        string fetchUrl = url;
        Exception? last = null;

        foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                (bytes, charset) = await DownloadAsync(c, ct).ConfigureAwait(false);
                fetchUrl = c;
                last = null;
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
            }
        }

        if (bytes is null)
            throw last ?? new InvalidOperationException("HTML download failed");

        var enc = BbsDatParser.GetEncoding(thread.Kind, bytes, charset);
        var html = enc.GetString(bytes);
        var posts = BbsDatParser.ParseHtmlThread(html);

        string? title = null;
        var tm = System.Text.RegularExpressions.Regex.Match(
            html, @"(?is)<title>(?<t>.*?)</title>");
        if (tm.Success)
            title = BbsDatParser.HtmlToText(tm.Groups["t"].Value);

        if (_normalizeMessages)
            posts = BbsMessageNormalizer.NormalizePosts(posts);

        return new BbsFetchResult
        {
            Posts = posts,
            ThreadTitle = title,
            SourceUrl = fetchUrl,
            SourceKind = "html/" + enc.WebName,
            ResolvedThreadId = thread.ThreadId,
        };
    }

    private async Task<(byte[] Bytes, string? Charset)> DownloadAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        string? charset = resp.Content.Headers.ContentType?.CharSet;
        // Content-Type: text/plain; charset=EUC-JP
        if (!string.IsNullOrWhiteSpace(charset))
            charset = charset.Trim().Trim('"');

        var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        return (bytes, charset);
    }

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
    }
}
