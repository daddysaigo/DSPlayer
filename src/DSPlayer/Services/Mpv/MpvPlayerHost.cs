using System.Runtime.InteropServices;
using System.Text;

namespace DSPlayer.Services.Mpv;

/// <summary>
/// libmpv player host. Embeds video into a native window (HWND) via the "wid" option.
/// Tuned for PeerCast live FLV/HTTP streams.
/// </summary>
public sealed class MpvPlayerHost : IDisposable
{
    private IntPtr _handle;
    private MpvNative.WakeupCallback? _wakeupCallback;
    private readonly object _sync = new();
    private bool _disposed;
    private double _volume = 0;
    private bool _muted;

    public bool IsInitialized => _handle != IntPtr.Zero;

    public double Volume
    {
        get => _volume;
        set => SetVolume(value);
    }

    public bool IsMuted => _muted;

    public event EventHandler<string>? Log;
    public event EventHandler? FileLoaded;
    public event EventHandler<EndFileEventArgs>? EndFile;

    public void Initialize(IntPtr hwnd, string? libMpvPath = null)
    {
        if (hwnd == IntPtr.Zero)
            throw new ArgumentException("HWND is required for embedded playback.", nameof(hwnd));

        lock (_sync)
        {
            if (_handle != IntPtr.Zero)
                throw new InvalidOperationException("Player already initialized.");

            MpvNative.Load(libMpvPath);
            Log?.Invoke(this, "libmpv loaded: " + (MpvNative.LoadedPath ?? "(unknown)"));

            _handle = MpvNative.Create();
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("mpv_create returned null.");

            // Embed into host HWND (must be set before initialize)
            SetOption("wid", ToWidString(hwnd));

            // Prefer GPU; fall back paths handled by mpv
            SetOption("vo", "gpu");
            SetOption("hwdec", "auto-safe");
            // Live PeerCast: do NOT keep-open on EOF (would freeze last frame without end-file recovery path).
            // idle=yes so after stop we can loadfile again cleanly.
            SetOption("keep-open", "no");
            SetOption("idle", "yes");
            SetOption("osc", "no");
            SetOption("input-default-bindings", "no");
            SetOption("input-vo-keyboard", "no");
            SetOption("force-window", "no");
            SetOption("border", "no");
            SetOption("cursor-autohide", "no");
            SetOption("msg-level", "all=warn");

            // Keep AR inside the host (letterbox). Window itself resizes freely (smooth).
            // Do NOT resize the WPF window to match AR on every SizeChanged — that causes bounce.
            SetOption("keepaspect", "yes");
            SetOption("panscan", "0");
            SetOption("video-unscaled", "no");
            SetOption("hidpi-window-scale", "no");

            // Never treat PeerCast URLs as youtube-dl targets
            SetOption("ytdl", "no");

            // PeerCast: live FLV over HTTP. Avoid aggressive low-latency profile
            // (it sets demuxer-lavf-probe-info=nostreams which breaks some FLV).
            // Live PeerCast: modest forward demuxer cap; no backward/seek cache (no rewind).
            // Caps are maxima, not reserved — limits growth vs default large back-buffer.
            SetOption("cache", "yes");
            SetOption("demuxer-max-bytes", "8MiB");
            SetOption("demuxer-max-back-bytes", "0");
            SetOption("demuxer-readahead-secs", "1.5");
            SetOption("demuxer-seekable-cache", "no");
            SetOption("demuxer-donate-buffer", "no");
            // Fail faster so the app can reconnect (PCRPlayer-like) instead of hanging on a dead socket.
            SetOption("network-timeout", "8");
            // Do NOT let lavf silently reconnect for a long time — frozen last-frame with no end-file.
            // App-level reconnect (MainWindow) owns retries for live streams.
            SetOption("stream-lavf-o", "fflags=+genpts");

            // Probe enough to detect FLV/H.264 from PeerCast
            SetOption("demuxer-lavf-probesize", "65536");
            SetOption("demuxer-lavf-analyzeduration", "1.5");
            SetOption("demuxer-lavf-probe-info", "yes");

            // Do not force infinite wait on incomplete live headers
            SetOption("force-seekable", "no");

            MpvNative.Check(MpvNative.Initialize(_handle), "mpv_initialize");

            // Start silent (user raises volume with wheel / keys)
            SetVolume(0);

            // Request log messages after init
            try { MpvNative.RequestLogMessages(_handle, "info"); } catch { /* optional */ }

            _wakeupCallback = OnWakeup;
            MpvNative.SetWakeupCallback(_handle, _wakeupCallback, IntPtr.Zero);

            Task.Run(EventLoop);
        }
    }

    public void Load(string url, bool play = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("Player not initialized.");
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Stream URL is required.", nameof(url));

        Log?.Invoke(this, "loadfile: " + url);

        // Prefer async command string path as a robust alternative, but use argv form
        Command("loadfile", url, "replace");
        if (play)
            SetProperty("pause", "no");
    }

    public void Stop()
    {
        if (_handle == IntPtr.Zero) return;
        try { Command("stop"); } catch { /* ignore */ }
    }

    public void Pause(bool pause = true) => SetProperty("pause", pause ? "yes" : "no");

    public void TogglePause()
    {
        var current = GetProperty("pause");
        Pause(current != "yes");
    }

    public void SetVolume(double volume)
    {
        volume = Math.Clamp(volume, 0, 100);
        _volume = volume;
        if (_handle == IntPtr.Zero) return;
        try
        {
            SetProperty("volume", volume.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            Log?.Invoke(this, "volume set failed: " + ex.Message);
        }
    }

    public void AdjustVolume(double delta) => SetVolume(_volume + delta);

    public void SetMuted(bool muted)
    {
        _muted = muted;
        if (_handle == IntPtr.Zero) return;
        try
        {
            SetProperty("mute", muted ? "yes" : "no");
        }
        catch (Exception ex)
        {
            Log?.Invoke(this, "mute set failed: " + ex.Message);
        }
    }

    public void ToggleMute() => SetMuted(!_muted);

    public void Command(params string[] args)
    {
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("Player not initialized.");
        if (args is null || args.Length == 0)
            throw new ArgumentException("Command requires at least one argument.");

        // Build NULL-terminated char* array in unmanaged memory for stability
        var utf8Pointers = new IntPtr[args.Length];
        var block = IntPtr.Zero;
        try
        {
            for (var i = 0; i < args.Length; i++)
                utf8Pointers[i] = AllocUtf8(args[i]);

            // Layout: [ptr0][ptr1]...[ptrN][NULL]
            var arrayBytes = IntPtr.Size * (args.Length + 1);
            block = Marshal.AllocHGlobal(arrayBytes);
            for (var i = 0; i < args.Length; i++)
                Marshal.WriteIntPtr(block, i * IntPtr.Size, utf8Pointers[i]);
            Marshal.WriteIntPtr(block, args.Length * IntPtr.Size, IntPtr.Zero);

            var error = MpvNative.Command(_handle, block);
            MpvNative.Check(error, "mpv_command(" + args[0] + ")");
        }
        finally
        {
            if (block != IntPtr.Zero)
                Marshal.FreeHGlobal(block);
            for (var i = 0; i < utf8Pointers.Length; i++)
            {
                if (utf8Pointers[i] != IntPtr.Zero)
                    Marshal.FreeHGlobal(utf8Pointers[i]);
            }
        }
    }

    public void SetProperty(string name, string value)
    {
        if (_handle == IntPtr.Zero) return;
        var error = MpvNative.SetPropertyString(_handle, name, value);
        MpvNative.Check(error, "mpv_set_property_string(" + name + ")");
    }

    public string? GetProperty(string name)
    {
        if (_handle == IntPtr.Zero) return null;
        var ptr = MpvNative.GetPropertyString(_handle, name);
        if (ptr == IntPtr.Zero) return null;
        try
        {
            return Marshal.PtrToStringUTF8(ptr);
        }
        finally
        {
            MpvNative.Free(ptr);
        }
    }

    public double? GetPropertyDouble(string name)
    {
        var s = GetProperty(name);
        if (string.IsNullOrWhiteSpace(s))
            return null;
        if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            return v;
        return null;
    }

    public int? GetPropertyInt(string name)
    {
        var d = GetPropertyDouble(name);
        if (d is null) return null;
        return (int)Math.Round(d.Value);
    }

    /// <summary>Video pixel size (w,h) if available.</summary>
    public (int W, int H)? GetVideoSize()
    {
        var w = GetPropertyInt("video-params/w") ?? GetPropertyInt("dwidth");
        var h = GetPropertyInt("video-params/h") ?? GetPropertyInt("dheight");
        if (w is > 0 && h is > 0)
            return (w.Value, h.Value);
        return null;
    }

    public double? GetFps()
    {
        return GetPropertyDouble("estimated-vf-fps")
               ?? GetPropertyDouble("container-fps")
               ?? GetPropertyDouble("fps");
    }

    public double? GetTimePos() => GetPropertyDouble("time-pos");

    /// <summary>
    /// Recently received bitrate in kbps (video+audio packet rate preferred).
    /// Null when nothing is flowing (idle / stalled / not playing).
    /// </summary>
    public int? GetReceivedBitrateKbps()
    {
        // packet-* = recent actual flow (best for live). track bitrates = nominal/estimated.
        var v = GetPropertyDouble("packet-video-bitrate")
                ?? GetPropertyDouble("video-bitrate");
        var a = GetPropertyDouble("packet-audio-bitrate")
                ?? GetPropertyDouble("audio-bitrate");

        double bps = 0;
        if (v is > 0) bps += v.Value;
        if (a is > 0) bps += a.Value;

        if (bps <= 0)
        {
            // cache-speed is bytes/sec when demuxer is pulling network data
            var cacheSpeed = GetPropertyDouble("cache-speed");
            if (cacheSpeed is > 0)
                bps = cacheSpeed.Value * 8.0;
        }

        if (bps <= 0)
            return null;

        return Math.Max(0, (int)Math.Round(bps / 1000.0));
    }

    /// <summary>True when mpv has nothing to play (idle / finished / stopped).</summary>
    public bool IsCoreIdle()
    {
        var s = GetProperty("core-idle");
        return string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsEofReached()
    {
        var s = GetProperty("eof-reached");
        return string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsPausedForCache()
    {
        var s = GetProperty("paused-for-cache");
        return string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Stop current file without quitting mpv (triggers end-file reason=stop).</summary>
    public void StopCurrentFile()
    {
        if (_handle == IntPtr.Zero) return;
        try { Command("stop"); }
        catch (Exception ex) { Log?.Invoke(this, "stop: " + ex.Message); }
    }

    private void SetOption(string name, string value)
    {
        var error = MpvNative.SetOptionString(_handle, name, value);
        if (error < 0)
            Log?.Invoke(this, "option " + name + "=" + value + ": " + MpvNative.GetError(error));
    }

    private void OnWakeup(IntPtr data)
    {
    }

    private void EventLoop()
    {
        while (!_disposed && _handle != IntPtr.Zero)
        {
            try
            {
                var eventPtr = MpvNative.WaitEvent(_handle, 0.1);
                if (eventPtr == IntPtr.Zero)
                    continue;

                var ev = Marshal.PtrToStructure<MpvNative.MpvEvent>(eventPtr);
                switch (ev.EventId)
                {
                    case MpvNative.MpvEventNone:
                        break;
                    case MpvNative.MpvEventShutdown:
                        return;
                    case MpvNative.MpvEventFileLoaded:
                        FileLoaded?.Invoke(this, EventArgs.Empty);
                        break;
                    case MpvNative.MpvEventEndFile:
                        HandleEndFile(ev);
                        break;
                    case MpvNative.MpvEventLogMessage:
                        HandleLogMessage(ev);
                        break;
                    case MpvNative.MpvEventStartFile:
                        Log?.Invoke(this, "start-file");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke(this, "event loop: " + ex.Message);
                Thread.Sleep(50);
            }
        }
    }

    private void HandleEndFile(MpvNative.MpvEvent ev)
    {
        // struct mpv_event_end_file { reason; error; ... }
        var reason = 0;
        var error = 0;
        if (ev.Data != IntPtr.Zero)
        {
            reason = Marshal.ReadInt32(ev.Data, 0);
            error = Marshal.ReadInt32(ev.Data, 4);
        }

        var reasonText = reason switch
        {
            0 => "eof",
            2 => "stop",
            3 => "quit",
            4 => "error",
            5 => "redirect",
            _ => "reason=" + reason,
        };

        var errorText = error < 0 ? MpvNative.GetError(error) : null;
        var summary = errorText is null
            ? reasonText
            : reasonText + ": " + errorText + " (" + error + ")";

        Log?.Invoke(this, "end-file " + summary);
        EndFile?.Invoke(this, new EndFileEventArgs(reasonText, error, errorText));
    }

    private void HandleLogMessage(MpvNative.MpvEvent ev)
    {
        if (ev.Data == IntPtr.Zero) return;
        try
        {
            // struct mpv_event_log_message { prefix; level; text; log_level; }
            var prefixPtr = Marshal.ReadIntPtr(ev.Data, 0);
            var levelPtr = Marshal.ReadIntPtr(ev.Data, IntPtr.Size);
            var textPtr = Marshal.ReadIntPtr(ev.Data, IntPtr.Size * 2);
            var prefix = Marshal.PtrToStringUTF8(prefixPtr) ?? "";
            var level = Marshal.PtrToStringUTF8(levelPtr) ?? "";
            var text = (Marshal.PtrToStringUTF8(textPtr) ?? "").TrimEnd();
            if (text.Length == 0) return;

            // Surface warn/error/fatal to UI path
            if (level is "error" or "fatal" or "warn" or "warning")
                Log?.Invoke(this, "[" + level + "] " + prefix + ": " + text);
            else
                System.Diagnostics.Debug.WriteLine("[mpv " + level + "] " + prefix + ": " + text);
        }
        catch
        {
            // ignore log parse failures
        }
    }

    /// <summary>
    /// mpv wid option: decimal HWND as string. On 64-bit, use unsigned decimal of the handle.
    /// </summary>
    private static string ToWidString(IntPtr hwnd)
    {
        if (IntPtr.Size == 8)
            return unchecked((ulong)hwnd.ToInt64()).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return unchecked((uint)hwnd.ToInt32()).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static IntPtr AllocUtf8(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s + "\0");
        var ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        return ptr;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Drop handlers so late events cannot reach a closed UI
        try { Log = null; } catch { /* ignore */ }
        try { FileLoaded = null; } catch { /* ignore */ }
        try { EndFile = null; } catch { /* ignore */ }

        lock (_sync)
        {
            if (_handle != IntPtr.Zero)
            {
                try { Command("quit"); } catch { /* ignore */ }
                try
                {
                    // Brief wait so event loop can exit on shutdown
                    Thread.Sleep(50);
                }
                catch { /* ignore */ }
                try
                {
                    MpvNative.TerminateDestroy(_handle);
                }
                catch
                {
                    // ignore during shutdown
                }
                _handle = IntPtr.Zero;
            }
        }

        _wakeupCallback = null;
        GC.SuppressFinalize(this);
    }
}

public sealed class EndFileEventArgs : EventArgs
{
    public EndFileEventArgs(string reason, int errorCode, string? errorMessage)
    {
        Reason = reason;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public string Reason { get; }
    public int ErrorCode { get; }
    public string? ErrorMessage { get; }
    public bool IsError => Reason == "error" || ErrorCode < 0;
}
