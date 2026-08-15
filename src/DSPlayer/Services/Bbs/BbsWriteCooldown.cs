namespace DSPlayer.Services.Bbs;

/// <summary>
/// Per-board write clock. Boards do not publish the exact flood interval
/// (SETTING.TXT / setting.cgi omit it), so we combine engine defaults with
/// remaining-seconds from the write response and remember what we learn.
/// </summary>
public sealed class BbsWriteCooldown
{
    /// <summary>したらば公式の二重投稿判定の下限。</summary>
    public const int ShitarabaDefaultSeconds = 10;

    /// <summary>jpnkn / 0ch 系でよくある最短間隔。</summary>
    public const int JpnknDefaultSeconds = 5;

    private readonly Dictionary<string, BoardClock> _boards = new(StringComparer.OrdinalIgnoreCase);

    public static string BoardKey(BbsThreadRef thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        return (thread.Host ?? "") + "/" + (thread.Category ?? "") + "/" + (thread.Board ?? "");
    }

    public static int DefaultIntervalSeconds(BbsThreadRef thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        if (thread.Kind == BbsBoardKind.Shitaraba)
            return ShitarabaDefaultSeconds;
        if (thread.Host is not null &&
            thread.Host.Contains("jpnkn", StringComparison.OrdinalIgnoreCase))
            return JpnknDefaultSeconds;
        return 0;
    }

    public int RemainingSeconds(BbsThreadRef? thread, DateTime? utc = null)
    {
        if (thread is null)
            return 0;
        if (!_boards.TryGetValue(BoardKey(thread), out var clock))
            return 0;
        var remain = (clock.UntilUtc - (utc ?? DateTime.UtcNow)).TotalSeconds;
        return remain <= 0 ? 0 : (int)Math.Ceiling(remain);
    }

    public int KnownIntervalSeconds(BbsThreadRef thread)
    {
        if (_boards.TryGetValue(BoardKey(thread), out var clock) && clock.IntervalSeconds > 0)
            return clock.IntervalSeconds;
        return DefaultIntervalSeconds(thread);
    }

    public void NoteSuccess(BbsThreadRef thread, DateTime? utc = null)
    {
        ArgumentNullException.ThrowIfNull(thread);
        var now = utc ?? DateTime.UtcNow;
        var clock = GetOrCreate(thread);
        clock.LastSuccessUtc = now;
        var interval = clock.IntervalSeconds > 0
            ? clock.IntervalSeconds
            : DefaultIntervalSeconds(thread);
        if (interval <= 0)
        {
            clock.UntilUtc = DateTime.MinValue;
            return;
        }

        if (clock.IntervalSeconds <= 0)
            clock.IntervalSeconds = interval;
        clock.UntilUtc = now.AddSeconds(interval);
    }

    public void NoteFlood(BbsThreadRef thread, int? retryAfterSeconds, DateTime? utc = null)
    {
        ArgumentNullException.ThrowIfNull(thread);
        var now = utc ?? DateTime.UtcNow;
        var clock = GetOrCreate(thread);
        var remain = retryAfterSeconds
                     ?? (clock.IntervalSeconds > 0 ? clock.IntervalSeconds : DefaultIntervalSeconds(thread));
        if (remain <= 0)
            remain = JpnknDefaultSeconds;
        clock.UntilUtc = now.AddSeconds(remain);

        if (clock.LastSuccessUtc != default)
        {
            var learned = (int)Math.Round((now - clock.LastSuccessUtc).TotalSeconds) + remain;
            if (learned is >= 3 and <= 180)
            {
                clock.IntervalSeconds = learned;
                clock.IntervalLearned = true;
            }
        }
        else if (retryAfterSeconds is > 0)
        {
            clock.IntervalSeconds = Math.Max(clock.IntervalSeconds, retryAfterSeconds.Value);
        }
    }

    private BoardClock GetOrCreate(BbsThreadRef thread)
    {
        var key = BoardKey(thread);
        if (!_boards.TryGetValue(key, out var clock))
        {
            clock = new BoardClock { IntervalSeconds = DefaultIntervalSeconds(thread) };
            _boards[key] = clock;
        }
        return clock;
    }

    private sealed class BoardClock
    {
        public int IntervalSeconds;
        public DateTime LastSuccessUtc;
        public DateTime UntilUtc;
        public bool IntervalLearned;
    }
}
