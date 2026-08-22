using System.Text.RegularExpressions;

namespace DSPlayer.Services.Bbs;

/// <summary>
/// Thread heat from post clocks (not fetch time): how many res landed in the last 5 minutes.
/// Score is 0–100. The status bar always shows it, including 0.
/// Tune <see cref="ScoreAnchors"/> when the curve needs a nudge — no other call sites change.
/// </summary>
public sealed class ThreadMomentum
{
    /// <summary>Posts older than this drop out of the window.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Piecewise-linear (count-in-window → score). Keep sorted by count.
    /// 5 posts / 5 min (≈ 1/min) sits in the low-to-mid 40s.
    /// </summary>
    public static readonly (int Count, int Score)[] ScoreAnchors =
    {
        (0, 0),
        (1, 10),
        (2, 20),
        (3, 30),
        (4, 35),
        (5, 43),
        (6, 50),
        (7, 55),
        (10, 70),
        (11, 75),
        (15, 85),
        (16, 88),
        (25, 95),
        (35, 100),
    };

    // 2026/08/09(日) 12:00:00.00 ID:Ab12Xy
    private static readonly Regex PostedAt = new(
        @"^(?<y>\d{4})/(?<m>\d{1,2})/(?<d>\d{1,2})(?:\([^)]*\))?\s*(?<h>\d{1,2}):(?<min>\d{2}):(?<s>\d{2})",
        RegexOptions.Compiled);

    private static readonly TimeZoneInfo Jst = ResolveJst();

    /// <summary>Newest-last ticks of posts inside the window. Peek is the oldest.</summary>
    private readonly Queue<long> _postedUtcTicks = new();

    public int Count { get; private set; }
    public int Score { get; private set; }

    public void Clear()
    {
        _postedUtcTicks.Clear();
        Count = 0;
        Score = 0;
    }

    /// <summary>
    /// Rebuild the 5-minute window from DAT post times (Japan).
    /// Walks the tail only — cheap even on a 1000-res thread.
    /// </summary>
    public void UpdateFromPosts(IReadOnlyList<BbsPost> posts, DateTime utcNow)
    {
        _postedUtcTicks.Clear();
        if (posts is not { Count: > 0 })
        {
            Recalculate(utcNow);
            return;
        }

        var now = utcNow.ToUniversalTime();
        var cutoff = now - Window;
        var slack = now + TimeSpan.FromMinutes(2);
        const int maxScan = 80;
        var recent = new List<long>(16);
        var scanned = 0;

        for (var i = posts.Count - 1; i >= 0 && scanned < maxScan; i--)
        {
            scanned++;
            var utc = TryParsePostedUtc(posts[i].DateId);
            if (utc is null)
                continue;
            if (utc.Value > slack)
                continue;
            if (utc.Value < cutoff)
                break;
            recent.Add(utc.Value.Ticks);
        }

        for (var i = recent.Count - 1; i >= 0; i--)
            _postedUtcTicks.Enqueue(recent[i]);

        Recalculate(utcNow);
    }

    public void Recalculate(DateTime utcNow)
    {
        var cutoff = utcNow.ToUniversalTime().Ticks - Window.Ticks;
        while (_postedUtcTicks.Count > 0 && _postedUtcTicks.Peek() < cutoff)
            _postedUtcTicks.Dequeue();
        Count = _postedUtcTicks.Count;
        Score = ScoreFromCount(Count);
    }

    /// <summary>2ch / したらば date field → UTC. Null when the stamp cannot be read.</summary>
    public static DateTime? TryParsePostedUtc(string? dateId)
    {
        if (string.IsNullOrWhiteSpace(dateId))
            return null;

        var m = PostedAt.Match(dateId.Trim());
        if (!m.Success)
            return null;

        try
        {
            var local = new DateTime(
                int.Parse(m.Groups["y"].Value),
                int.Parse(m.Groups["m"].Value),
                int.Parse(m.Groups["d"].Value),
                int.Parse(m.Groups["h"].Value),
                int.Parse(m.Groups["min"].Value),
                int.Parse(m.Groups["s"].Value),
                DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(local, Jst);
        }
        catch
        {
            return null;
        }
    }

    public static int ScoreFromCount(int count)
    {
        if (count <= 0) return 0;
        var anchors = ScoreAnchors;
        var last = anchors[^1];
        if (count >= last.Count) return 100;

        for (var i = 1; i < anchors.Length; i++)
        {
            var (c1, s1) = anchors[i];
            if (count > c1) continue;
            var (c0, s0) = anchors[i - 1];
            if (c1 == c0) return s1;
            var t = (double)(count - c0) / (c1 - c0);
            return (int)Math.Round(s0 + t * (s1 - s0), MidpointRounding.AwayFromZero);
        }

        return 100;
    }

    public static string LabelFromScore(int score)
    {
        if (score < 35) return "過疎";
        if (score < 55) return "落ち着いてる";
        if (score < 75) return "普通";
        if (score < 88) return "活発";
        return "熱い";
    }

    public const char MeterOn = '\u25B0';  // ▰
    public const char MeterOff = '\u25B1'; // ▱
    public const int MeterSlots = 5;

    /// <summary>
    /// One color for the whole filled meter, by band:
    /// 過疎 スチール青 / ゆったり 青 / 平常運転 緑 / にぎやか 黄 / 大盛況 コーラル橙.
    /// </summary>
    public static readonly (byte R, byte G, byte B)[] MeterBandRgb =
    {
        (0x6E, 0x90, 0xB4),
        (0x3B, 0x7E, 0xE0),
        (0x3C, 0xA3, 0x72),
        (0xE0, 0xC0, 0x40),
        (0xE0, 0x5A, 0x38),
    };

    public static (byte R, byte G, byte B) MeterColorFromScore(int score)
    {
        var n = MeterFilledFromScore(score);
        if (n <= 0) return MeterBandRgb[0];
        return MeterBandRgb[n - 1];
    }

    /// <summary>Heat chrome labels. Same score bands as <see cref="LabelFromScore"/>.</summary>
    public static string HeatLabelFromScore(int score)
    {
        if (score < 35) return "過疎";
        if (score < 55) return "ゆったり";
        if (score < 75) return "平常運転";
        if (score < 88) return "にぎやか";
        return "大盛況";
    }

    /// <summary>0–5 filled slots. Score 0 is empty; each verbal band adds one block.</summary>
    public static int MeterFilledFromScore(int score)
    {
        if (score <= 0) return 0;
        if (score < 35) return 1;
        if (score < 55) return 2;
        if (score < 75) return 3;
        if (score < 88) return 4;
        return 5;
    }

    public static void SplitMeter(int score, out string filled, out string empty)
    {
        var n = MeterFilledFromScore(score);
        filled = n > 0 ? new string(MeterOn, n) : "";
        empty = n < MeterSlots ? new string(MeterOff, MeterSlots - n) : "";
    }

    /// <summary>Simple chrome caption, e.g. <c>勢い 58　普通</c>.</summary>
    public static string FormatDisplay(int score)
    {
        var n = Math.Clamp(score, 0, 100);
        return "勢い " + n + "　" + LabelFromScore(n);
    }

    public static string NormalizeStyle(string? id)
    {
        var k = (id ?? "").Trim();
        if (k.Equals("Simple", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("Compact", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("Classic", StringComparison.OrdinalIgnoreCase))
            return "Simple";
        return "Heat";
    }

    public static bool IsHeatStyle(string? id) =>
        NormalizeStyle(id) == "Heat";

    private static TimeZoneInfo ResolveJst()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        }
    }
}
