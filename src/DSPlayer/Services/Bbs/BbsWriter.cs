using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace DSPlayer.Services.Bbs;

public sealed class BbsWriteResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public string? ResponseSnippet { get; init; }
    public bool NeedsConfirmRetry { get; init; }
    /// <summary>Server rejected the post as too soon after the previous one.</summary>
    public bool IsFloodLimited { get; init; }
    /// <summary>Seconds to wait before retrying, when the board said so (or a safe default).</summary>
    public int? RetryAfterSeconds { get; init; }
}

public sealed class BbsWriter : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly CookieContainer _cookies = new();

    public BbsWriter(HttpClient? http = null, string? userAgent = null)
    {
        if (http is null)
        {
            var handler = new HttpClientHandler
            {
                CookieContainer = _cookies,
                AllowAutoRedirect = true,
                UseCookies = true,
            };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            _http.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent", userAgent ?? BbsClient.DefaultUserAgent);
            _ownsHttp = true;
        }
        else
        {
            _http = http;
            _ownsHttp = false;
        }
    }

    public async Task<BbsWriteResult> PostAsync(
        BbsThreadRef thread,
        string name,
        string mail,
        string message,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(thread);
        message = (message ?? "").Trim();
        if (message.Length == 0)
            return new BbsWriteResult { Success = false, Message = "本文が空です" };
        if (!thread.CanWrite)
            return new BbsWriteResult { Success = false, Message = "この掲示板には書き込めません（スレ未解決）" };

        return thread.Kind switch
        {
            BbsBoardKind.Shitaraba => await PostShitarabaAsync(thread, name, mail, message, ct).ConfigureAwait(false),
            BbsBoardKind.TwochStyle => await PostTwochAsync(thread, name, mail, message, ct).ConfigureAwait(false),
            _ => new BbsWriteResult { Success = false, Message = "未対応の掲示板種別です" },
        };
    }

    private async Task<BbsWriteResult> PostShitarabaAsync(
        BbsThreadRef thread, string name, string mail, string message, CancellationToken ct)
    {
        var enc = BbsDatParser.TryGetEncoding("euc-jp") ?? Encoding.UTF8;
        var fields = new Dictionary<string, string>
        {
            ["DIR"] = thread.Category ?? "",
            ["BBS"] = thread.Board ?? "",
            ["KEY"] = thread.ThreadId ?? "",
            ["NAME"] = name ?? "",
            ["MAIL"] = string.IsNullOrWhiteSpace(mail) ? "sage" : mail,
            ["MESSAGE"] = message,
        };

        var body = EncodeForm(fields, enc);
        using var content = new ByteArrayContent(body);
        content.Headers.TryAddWithoutValidation("Content-Type", "application/x-www-form-urlencoded");

        using var req = new HttpRequestMessage(HttpMethod.Post, thread.WriteUrl);
        req.Content = content;
        if (!string.IsNullOrWhiteSpace(thread.HtmlUrl))
            req.Headers.Referrer = new Uri(thread.HtmlUrl);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var text = DecodeBest(bytes, enc);
        return InterpretResponse(resp.IsSuccessStatusCode, text, "したらば");
    }

    private async Task<BbsWriteResult> PostTwochAsync(
        BbsThreadRef thread, string name, string mail, string message, CancellationToken ct)
    {
        var enc = BbsDatParser.TryGetEncoding("shift_jis") ?? Encoding.UTF8;
        var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var fields = new Dictionary<string, string>
        {
            ["bbs"] = thread.Board ?? "",
            ["key"] = thread.ThreadId ?? "",
            ["time"] = unix,
            ["FROM"] = name ?? "",
            ["mail"] = string.IsNullOrWhiteSpace(mail) ? "sage" : mail,
            ["MESSAGE"] = message,
            ["submit"] = "書き込む",
        };

        var body = EncodeForm(fields, enc);
        using var content = new ByteArrayContent(body);
        content.Headers.TryAddWithoutValidation("Content-Type", "application/x-www-form-urlencoded");

        using var req = new HttpRequestMessage(HttpMethod.Post, thread.WriteUrl);
        req.Content = content;
        if (!string.IsNullOrWhiteSpace(thread.HtmlUrl))
            req.Headers.Referrer = new Uri(thread.HtmlUrl);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        // response may be sjis or utf-8
        var text = DecodeBest(bytes, enc);
        return InterpretResponse(resp.IsSuccessStatusCode, text, "2ch系");
    }

    private static byte[] EncodeForm(IReadOnlyDictionary<string, string> fields, Encoding enc)
    {
        // application/x-www-form-urlencoded with the board's charset (not always UTF-8)
        static string Escape(string s, Encoding e)
        {
            var bytes = e.GetBytes(s);
            var sb = new StringBuilder(bytes.Length * 3);
            foreach (var b in bytes)
            {
                // unreserved-ish for form: alnum and * - . _
                if ((b >= 'a' && b <= 'z') || (b >= 'A' && b <= 'Z') || (b >= '0' && b <= '9') ||
                    b is (byte)'-' or (byte)'_' or (byte)'.' or (byte)'*')
                {
                    sb.Append((char)b);
                }
                else if (b == (byte)' ')
                {
                    sb.Append('+');
                }
                else
                {
                    sb.Append('%');
                    sb.Append(b.ToString("X2"));
                }
            }
            return sb.ToString();
        }

        var parts = fields.Select(kv => Escape(kv.Key, Encoding.ASCII) + "=" + Escape(kv.Value ?? "", enc));
        return Encoding.ASCII.GetBytes(string.Join("&", parts));
    }

    private static string DecodeBest(byte[] bytes, Encoding preferred)
    {
        try { return preferred.GetString(bytes); }
        catch { return Encoding.UTF8.GetString(bytes); }
    }

    private static readonly Regex FloodRemainSeconds = new(
        @"(\d+)\s*(?:秒|sec)\s*たたないと",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex FloodAnySeconds = new(
        @"(\d+)\s*(?:秒|sec)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Classify a write.cgi / bbs.cgi HTML (or plain) response.</summary>
    public static BbsWriteResult InterpretResponse(bool httpOk, string text, string kind)
    {
        var flat = text.Replace("\r", "").Replace("\n", " ");
        var snippet = flat.Length > 200 ? flat[..200] : flat;

        // Cookie / first-write confirm — must be checked before success,
        // because those pages also say 「書きこみました」 / 「1秒待って」.
        if (LooksLikeCookieConfirm(text))
        {
            return new BbsWriteResult
            {
                Success = false,
                NeedsConfirmRetry = true,
                Message = "確認画面です。もう一度「書込」を押してください。",
                ResponseSnippet = snippet,
            };
        }

        // したらば: 「書きこみが終わりました」「しばらくお待ち下さい」— 成功ページ
        if (LooksLikeWriteSuccess(text))
        {
            return new BbsWriteResult
            {
                Success = true,
                Message = "書き込みました",
                ResponseSnippet = snippet,
            };
        }

        if (LooksLikeFloodLimit(text))
            return FloodResult(text, snippet);

        if (LooksLikeHardError(text))
        {
            return new BbsWriteResult
            {
                Success = false,
                Message = kind + " 書き込みエラー",
                ResponseSnippet = snippet,
            };
        }

        // Many boards redirect / return 200 with short body on success
        if (httpOk && text.Length < 80)
        {
            return new BbsWriteResult
            {
                Success = true,
                Message = "書き込みを送信しました",
                ResponseSnippet = snippet,
            };
        }

        if (httpOk)
        {
            // Ambiguous — treat as likely success if no explicit error
            return new BbsWriteResult
            {
                Success = true,
                Message = "書き込みを送信しました（応答を確認）",
                ResponseSnippet = snippet,
            };
        }

        return new BbsWriteResult
        {
            Success = false,
            Message = kind + " HTTP エラー",
            ResponseSnippet = snippet,
        };
    }

    private static bool LooksLikeWriteSuccess(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        return text.Contains("書きこみました", StringComparison.Ordinal) ||
               text.Contains("書き込みました", StringComparison.Ordinal) ||
               text.Contains("書きこみが終わ", StringComparison.Ordinal) ||
               text.Contains("書き込みが終わ", StringComparison.Ordinal) ||
               text.Contains("書き込みが完了", StringComparison.Ordinal) ||
               text.Contains("投稿が完了", StringComparison.Ordinal) ||
               text.Contains("書き込みを受け付け", StringComparison.Ordinal);
    }

    private static bool LooksLikeCookieConfirm(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        return text.Contains("変更せずもう一度", StringComparison.Ordinal) ||
               text.Contains("クッキーを設定", StringComparison.Ordinal) ||
               text.Contains("cookieを設定", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("１回目", StringComparison.Ordinal) ||
               text.Contains("1回目", StringComparison.Ordinal);
    }

    private static bool LooksLikeHardError(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        // Thread pages often have 「エラー報告」— that is not a write failure.
        if (text.Contains("エラー報告", StringComparison.Ordinal))
        {
            var without = text.Replace("エラー報告", "", StringComparison.Ordinal);
            if (!without.Contains("エラー", StringComparison.Ordinal) &&
                !without.Contains("ＥＲＲＯＲ", StringComparison.Ordinal) &&
                !without.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return text.Contains("ＥＲＲＯＲ", StringComparison.Ordinal) ||
               text.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("エラー", StringComparison.Ordinal) ||
               text.Contains("書込できません", StringComparison.Ordinal) ||
               text.Contains("書き込めません", StringComparison.Ordinal);
    }

    private static bool LooksLikeFloodLimit(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        // Do not treat success-page “しばらくお待ち下さい” as flood.
        return text.Contains("連投", StringComparison.Ordinal) ||
               text.Contains("投稿間隔", StringComparison.Ordinal) ||
               text.Contains("連続投稿", StringComparison.Ordinal) ||
               text.Contains("書き込み間隔", StringComparison.Ordinal) ||
               text.Contains("短時間に", StringComparison.Ordinal) ||
               text.Contains("多重書き込み", StringComparison.Ordinal) ||
               text.Contains("秒たたないと", StringComparison.Ordinal) ||
               text.Contains("秒待たないと", StringComparison.Ordinal) ||
               text.Contains("たたないと書", StringComparison.Ordinal) ||
               text.Contains("samba", StringComparison.OrdinalIgnoreCase);
    }

    private static BbsWriteResult FloodResult(string text, string snippet)
    {
        var seconds = TryParseFloodSeconds(text);
        var message = seconds is > 0
            ? "連投規制中です。あと " + seconds + " 秒待ってください。"
            : "連投規制中です。";
        return new BbsWriteResult
        {
            Success = false,
            IsFloodLimited = true,
            RetryAfterSeconds = seconds,
            Message = message,
            ResponseSnippet = snippet,
        };
    }

    private static int? TryParseFloodSeconds(string text)
    {
        var prefer = FloodRemainSeconds.Match(text);
        if (prefer.Success && TryReadSeconds(prefer.Groups[1].Value, out var remain))
            return remain;

        var m = FloodAnySeconds.Match(text);
        if (m.Success && TryReadSeconds(m.Groups[1].Value, out var n))
            return n;
        return null;
    }

    private static bool TryReadSeconds(string raw, out int seconds)
    {
        seconds = 0;
        return int.TryParse(raw, out seconds) && seconds is >= 1 and <= 300;
    }

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
    }
}
