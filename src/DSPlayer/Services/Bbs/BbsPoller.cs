namespace DSPlayer.Services.Bbs;

/// <summary>
/// Periodically fetches a BBS thread and raises events when new posts arrive.
/// Supports manual thread switch and auto-advance at 1000 res.
/// </summary>
public sealed class BbsPoller : IDisposable
{
    private readonly BbsClient _client;
    private readonly TimeSpan _interval;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int _lastCount;
    private string? _lastTitle;
    private readonly object _switchLock = new();

    public BbsPoller(BbsClient? client = null, TimeSpan? interval = null)
    {
        _client = client ?? new BbsClient();
        _interval = interval ?? TimeSpan.FromSeconds(5);
    }

    public BbsClient Client => _client;

    /// <summary>Original contact (may be board-only).</summary>
    public BbsThreadRef? Thread { get; private set; }

    /// <summary>Writable resolved thread (after subject.txt etc.).</summary>
    public BbsThreadRef? ResolvedThread { get; private set; }

    public IReadOnlyList<BbsPost> Posts { get; private set; } = Array.Empty<BbsPost>();
    public IReadOnlyList<BbsSubjectEntry> Subjects { get; private set; } = Array.Empty<BbsSubjectEntry>();
    public string? ThreadTitle => _lastTitle;
    public bool IsRunning => _loop is { IsCompleted: false };

    public event EventHandler<BbsUpdatedEventArgs>? Updated;
    public event EventHandler<Exception>? Error;
    public event EventHandler<IReadOnlyList<BbsSubjectEntry>>? SubjectsUpdated;
    public event EventHandler<string>? ThreadAutoAdvanced;

    public void Start(BbsThreadRef thread)
    {
        Stop();
        Thread = thread ?? throw new ArgumentNullException(nameof(thread));
        ResolvedThread = thread.CanWrite ? thread : null;
        _lastCount = 0;
        _lastTitle = null;
        Posts = Array.Empty<BbsPost>();
        Subjects = Array.Empty<BbsSubjectEntry>();
        _client.ResetStickyThread();
        if (!string.IsNullOrEmpty(thread.ThreadId) && !thread.IsBoardOnly)
            _client.SetStickyThread(thread.ThreadId);
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }

    public void RequestRefresh()
    {
        var ct = _cts?.Token ?? CancellationToken.None;
        if (ct.IsCancellationRequested) return;
        _ = Task.Run(async () =>
        {
            try { await TickAsync(ct).ConfigureAwait(false); }
            catch { /* ignore */ }
        });
    }

    /// <summary>Switch sticky thread and reload.</summary>
    public void SwitchThread(string threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId) || Thread is null)
            return;

        lock (_switchLock)
        {
            _client.SetStickyThread(threadId);
            ResolvedThread = Thread.WithThread(threadId);
            _lastCount = 0;
            _lastTitle = null;
            Posts = Array.Empty<BbsPost>();
        }

        RequestRefresh();
    }

    public async Task RefreshSubjectsAsync(CancellationToken ct = default)
    {
        if (Thread is null || string.IsNullOrWhiteSpace(Thread.SubjectUrl))
            return;
        try
        {
            var list = await _client.FetchSubjectListAsync(Thread, ct).ConfigureAwait(false);
            Subjects = list;
            SubjectsUpdated?.Invoke(this, list);
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, ex);
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        // Subject list once at start
        try { await RefreshSubjectsAsync(ct).ConfigureAwait(false); } catch { /* ignore */ }

        await TickAsync(ct).ConfigureAwait(false);

        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                await TickAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // normal stop
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (Thread is null) return;
        try
        {
            BbsThreadRef fetchTarget;
            lock (_switchLock)
            {
                fetchTarget = ResolvedThread ?? Thread;
                if (!string.IsNullOrEmpty(_client.StickyThreadId))
                    fetchTarget = Thread.WithThread(_client.StickyThreadId);
            }

            var result = await _client.FetchAsync(fetchTarget, ct).ConfigureAwait(false);
            var posts = result.Posts;

            // Auto-advance when thread is full
            if (posts.Count >= 1000 && Thread.SubjectUrl is not null)
            {
                var advanced = await _client.TryAutoAdvanceIfFullAsync(
                    Thread, result.ResolvedThreadId ?? fetchTarget.ThreadId, posts.Count, ct)
                    .ConfigureAwait(false);
                if (!string.IsNullOrEmpty(advanced))
                {
                    ThreadAutoAdvanced?.Invoke(this, advanced);
                    lock (_switchLock)
                    {
                        ResolvedThread = Thread.WithThread(advanced);
                        _lastCount = 0;
                        _lastTitle = null;
                        Posts = Array.Empty<BbsPost>();
                    }
                    // Refresh subject + load new thread immediately
                    try { await RefreshSubjectsAsync(ct).ConfigureAwait(false); } catch { /* ignore */ }
                    result = await _client.FetchAsync(Thread.WithThread(advanced), ct).ConfigureAwait(false);
                    posts = result.Posts;
                }
            }

            var added = posts.Count > _lastCount
                ? posts.Skip(_lastCount).ToList()
                : new List<BbsPost>();

            if (posts.Count < _lastCount)
                added = posts.ToList();

            Posts = posts;
            if (!string.IsNullOrWhiteSpace(result.ThreadTitle))
                _lastTitle = result.ThreadTitle;
            else if (_lastTitle is null)
                _lastTitle = posts.FirstOrDefault()?.Title;

            var baseThread = Thread;
            if (baseThread is not null)
            {
                if (!string.IsNullOrEmpty(result.ResolvedThreadId))
                    ResolvedThread = baseThread.WithThread(result.ResolvedThreadId);
                else if (baseThread.CanWrite)
                    ResolvedThread = baseThread;
            }

            var first = _lastCount == 0;
            _lastCount = posts.Count;

            Updated?.Invoke(this, new BbsUpdatedEventArgs(
                posts, added, _lastTitle, first, ResolvedThread));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, ex);
        }
    }

    public void Dispose()
    {
        Stop();
        _client.Dispose();
    }
}

public sealed class BbsUpdatedEventArgs : EventArgs
{
    public BbsUpdatedEventArgs(
        IReadOnlyList<BbsPost> allPosts,
        IReadOnlyList<BbsPost> newPosts,
        string? threadTitle,
        bool isFirstLoad,
        BbsThreadRef? resolvedThread = null)
    {
        AllPosts = allPosts;
        NewPosts = newPosts;
        ThreadTitle = threadTitle;
        IsFirstLoad = isFirstLoad;
        ResolvedThread = resolvedThread;
    }

    public IReadOnlyList<BbsPost> AllPosts { get; }
    public IReadOnlyList<BbsPost> NewPosts { get; }
    public string? ThreadTitle { get; }
    public bool IsFirstLoad { get; }
    public BbsThreadRef? ResolvedThread { get; }
}
