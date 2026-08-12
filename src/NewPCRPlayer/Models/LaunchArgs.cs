using System.Text.RegularExpressions;

namespace NewPCRPlayer.Models;

/// <summary>
/// PeCaRecorder / PCRPlayer compatible launch arguments.
/// <para>
/// Canonical PeCaRecorder player args (matches stock PCRPlayer registration):
/// <code>"$x" "$0" "$3"</code>
/// </para>
/// <list type="table">
/// <item><term>$x</term><description>Stream or playlist URL (/stream/ or /pls/, often with ?tip=)</description></item>
/// <item><term>$0</term><description>Channel name</description></item>
/// <item><term>$3</term><description>Contact URL (BBS / thread / board)</description></item>
/// </list>
/// Real example:
/// <code>
/// NewPCRPlayer.exe
///   "http://127.0.0.1:7144/pls/&lt;ChannelID&gt;?tip=host:port"
///   "チャンネル名"
///   "https://.../read.cgi/..."
/// </code>
/// </summary>
public sealed class LaunchArgs
{
    // /stream/ or /pls/ + 32-hex ChannelID, optional query (tip=...)
    private static readonly Regex PeerCastMediaRegex = new(
        @"^https?://[^/\s]+/(?<kind>stream|pls)/(?<id>[0-9a-fA-F]{32})(?<query>\?[^#\s]*)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public string? StreamUrl { get; init; }
    /// <summary>URL actually passed to the player engine (pls converted to stream when needed).</summary>
    public string? PlaybackUrl { get; init; }
    public string? ChannelName { get; init; }
    public string? ChannelId { get; init; }
    /// <summary>Contact / BBS URL from PeCaRecorder <c>$3</c> (or detected).</summary>
    public string? ContactUrl { get; init; }
    public IReadOnlyList<string> RawArgs { get; init; } = Array.Empty<string>();

    /// <summary>True when a playable stream/playlist URL was resolved.</summary>
    public bool HasStream => !string.IsNullOrWhiteSpace(PlaybackUrl ?? StreamUrl);

    /// <summary>True when a contact URL was provided or detected.</summary>
    public bool HasContact => !string.IsNullOrWhiteSpace(ContactUrl);

    public static LaunchArgs Parse(string[] args)
    {
        args ??= Array.Empty<string>();
        var cleaned = args
            .Select(a => a?.Trim().Trim('"') ?? string.Empty)
            .Where(a => a.Length > 0)
            .ToArray();

        // Prefer canonical PeCaRecorder order: $x, $0, $3
        if (TryParsePositional(cleaned, out var positional))
            return positional!;

        return ParseHeuristic(cleaned);
    }

    /// <summary>
    /// Positional parse for the standard registration:
    /// <c>"$x" "$0" "$3"</c> (media, channel name, contact).
    /// Also accepts 1-arg (media only) and 2-arg (media + name, or media + contact).
    /// </summary>
    private static bool TryParsePositional(string[] cleaned, out LaunchArgs? result)
    {
        result = null;
        if (cleaned.Length == 0)
            return false;

        // Arg0 must look like PeerCast media for positional mode
        if (!IsPeerCastMediaUrl(cleaned[0]))
            return false;

        var mediaUrl = cleaned[0];
        string? channelName = null;
        string? contactUrl = null;

        if (cleaned.Length >= 2)
        {
            var a1 = cleaned[1];
            if (IsContactUrl(a1) || (LooksLikeHttpUrl(a1) && !IsPeerCastMediaUrl(a1)))
            {
                // "$x" "$3"  (name omitted)
                contactUrl = a1;
            }
            else if (!LooksLikeHttpUrl(a1))
            {
                channelName = a1;
            }
            else
            {
                // Unknown second HTTP URL — keep as name only if not media; else contact
                if (!IsPeerCastMediaUrl(a1))
                    contactUrl = a1;
            }
        }

        if (cleaned.Length >= 3)
        {
            var a2 = cleaned[2];
            // Canonical $3: any non-media URL, or any leftover token treated as contact if URL-like
            if (LooksLikeHttpUrl(a2) && !IsPeerCastMediaUrl(a2))
            {
                contactUrl = a2;
            }
            else if (contactUrl is null && IsContactUrl(a2))
            {
                contactUrl = a2;
            }
            else if (channelName is null && !LooksLikeHttpUrl(a2))
            {
                // Rare: "$x" "$3" "name" reordered — ignore for positional strictness
            }
        }

        // Extra args beyond 3: if contact still missing, scan for contact-like URL
        if (contactUrl is null && cleaned.Length > 3)
        {
            contactUrl = cleaned.Skip(1).FirstOrDefault(a =>
                LooksLikeHttpUrl(a) && !IsPeerCastMediaUrl(a));
        }

        result = Build(mediaUrl, channelName, contactUrl, cleaned);
        return true;
    }

    private static LaunchArgs ParseHeuristic(string[] cleaned)
    {
        string? mediaUrl = null;
        string? channelName = null;
        string? contactUrl = null;

        foreach (var arg in cleaned)
        {
            if (mediaUrl is null && IsPeerCastMediaUrl(arg))
            {
                mediaUrl = arg;
                continue;
            }

            if (contactUrl is null && IsContactUrl(arg))
            {
                contactUrl = arg;
                continue;
            }

            if (channelName is null && !LooksLikeHttpUrl(arg) && !arg.StartsWith('-'))
            {
                channelName = arg;
            }
        }

        mediaUrl ??= cleaned.FirstOrDefault(IsPeerCastMediaUrl)
                     ?? cleaned.FirstOrDefault(a => LooksLikeHttpUrl(a) && !IsContactUrl(a));

        channelName ??= cleaned.FirstOrDefault(a =>
            !LooksLikeHttpUrl(a) && !a.StartsWith('-'));

        contactUrl ??= cleaned.FirstOrDefault(a =>
            LooksLikeHttpUrl(a) && !IsPeerCastMediaUrl(a) &&
            (IsContactUrl(a) || true)); // any non-media HTTP URL as contact fallback

        // Refine: only assign non-media HTTP as contact if we still lack one
        if (contactUrl is null)
        {
            contactUrl = cleaned.FirstOrDefault(a =>
                LooksLikeHttpUrl(a) && !IsPeerCastMediaUrl(a));
        }

        return Build(mediaUrl, channelName, contactUrl, cleaned);
    }

    private static LaunchArgs Build(
        string? mediaUrl, string? channelName, string? contactUrl, string[] cleaned)
    {
        return new LaunchArgs
        {
            StreamUrl = mediaUrl,
            PlaybackUrl = ToPlaybackUrl(mediaUrl),
            ChannelName = channelName,
            ChannelId = ExtractChannelId(mediaUrl),
            ContactUrl = contactUrl,
            RawArgs = cleaned,
        };
    }

    public static bool LooksLikeHttpUrl(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
         value.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    /// <summary>PeerCast media URL: /stream/ or /pls/ with channel id.</summary>
    public static bool IsPeerCastMediaUrl(string value)
    {
        if (!LooksLikeHttpUrl(value))
            return false;
        if (PeerCastMediaRegex.IsMatch(value))
            return true;
        // Non-standard id length still counts if path is stream/pls
        return value.Contains("/stream/", StringComparison.OrdinalIgnoreCase)
               || value.Contains("/pls/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Backward-compatible name used by older tests/callers.</summary>
    public static bool LooksLikeStreamUrl(string value) => IsPeerCastMediaUrl(value);

    /// <summary>
    /// Detect BBS / contact URLs. PeCaRecorder <c>$3</c> may be a full thread, board top,
    /// or generic contact page — positional parse accepts any non-media HTTP as contact.
    /// </summary>
    public static bool IsContactUrl(string value)
    {
        if (!LooksLikeHttpUrl(value))
            return false;
        if (IsPeerCastMediaUrl(value))
            return false;

        // BBS / contact patterns common on PeerCast channels
        return value.Contains("read.cgi", StringComparison.OrdinalIgnoreCase)
               || value.Contains("/test/read", StringComparison.OrdinalIgnoreCase)
               || value.Contains("write.cgi", StringComparison.OrdinalIgnoreCase)
               || value.Contains("subject", StringComparison.OrdinalIgnoreCase)
               || value.Contains("bbs", StringComparison.OrdinalIgnoreCase)
               || value.Contains("jbbs", StringComparison.OrdinalIgnoreCase)
               || value.Contains("shitaraba", StringComparison.OrdinalIgnoreCase)
               || value.Contains("2ch", StringComparison.OrdinalIgnoreCase)
               || value.Contains("5ch", StringComparison.OrdinalIgnoreCase)
               || value.Contains("jpnkn.com", StringComparison.OrdinalIgnoreCase)
               || value.Contains("open2ch", StringComparison.OrdinalIgnoreCase);
    }

    public static string? ExtractChannelId(string? mediaUrl)
    {
        if (string.IsNullOrWhiteSpace(mediaUrl))
            return null;

        var match = PeerCastMediaRegex.Match(mediaUrl);
        return match.Success ? match.Groups["id"].Value : null;
    }

    /// <summary>
    /// PeCaRecorder often passes /pls/ (playlist). Direct FLV playback needs /stream/.
    /// Keep query string (tip=...) so PeerCastStation can locate the source.
    /// </summary>
    public static string? ToPlaybackUrl(string? mediaUrl)
    {
        if (string.IsNullOrWhiteSpace(mediaUrl))
            return null;

        // String replace keeps tip=host:port exactly (UriBuilder can mangle it)
        var idx = mediaUrl.IndexOf("/pls/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return mediaUrl;

        return string.Concat(
            mediaUrl.AsSpan(0, idx),
            "/stream/",
            mediaUrl.AsSpan(idx + "/pls/".Length));
    }
}
