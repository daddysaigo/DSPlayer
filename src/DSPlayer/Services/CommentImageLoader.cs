using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DSPlayer.Services.Bbs;

namespace DSPlayer.Services;

public sealed class LoadedCommentImage
{
    public required string SourceUrl { get; init; }
    public required BitmapSource Preview { get; init; }
    public IReadOnlyList<BitmapSource>? Frames { get; init; }
    public IReadOnlyList<int>? DelaysMs { get; init; }
    public bool IsAnimated => Frames is { Count: > 1 };
}

/// <summary>
/// Shared fetch + decode for inline comment images.
/// Caps concurrency, payload size, and cache so a busy thread cannot stall the player.
/// </summary>
public static class CommentImageLoader
{
    public const int MaxBytes = 1_500_000;
    public const int MaxCached = 32;
    public const int DecodePixelWidth = 480;
    public const double DisplayMaxHeight = 240;
    public const int MaxGifBytesToAnimate = 1_200_000;
    public const int MaxGifFrames = 48;
    public const int MaxGifPixelFrames = 2_000_000;

    /// <summary>When false, comment bodies still hyperlink URLs but do not fetch pixels.</summary>
    public static bool EmbedEnabled { get; set; } = true;

    private static readonly HttpClient Http;
    private static readonly SemaphoreSlim DownloadGate = new(2, 2);
    private static readonly ConcurrentDictionary<string, Task<LoadedCommentImage?>> InFlight =
        new(StringComparer.Ordinal);
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, LoadedCommentImage?> Cache = new(StringComparer.Ordinal);
    private static readonly Queue<string> CacheOrder = new();

    static CommentImageLoader()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxConnectionsPerServer = 2,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        Http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        Http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
        Http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
    }

    public static Task<LoadedCommentImage?> GetAsync(string sourceUrl)
    {
        if (!EmbedEnabled)
            return Task.FromResult<LoadedCommentImage?>(null);
        if (!CommentBodyParser.IsHttpUrl(sourceUrl) || CommentBodyParser.IsLoopbackUrl(sourceUrl))
            return Task.FromResult<LoadedCommentImage?>(null);

        var fetchUrl = CommentBodyParser.ToFetchUrl(sourceUrl);

        lock (CacheLock)
        {
            if (Cache.TryGetValue(fetchUrl, out var hit))
                return Task.FromResult(hit);
        }

        return InFlight.GetOrAdd(fetchUrl, u => LoadAndCacheAsync(sourceUrl, u));
    }

    private static async Task<LoadedCommentImage?> LoadAndCacheAsync(string sourceUrl, string fetchUrl)
    {
        LoadedCommentImage? image = null;
        try
        {
            await DownloadGate.WaitAsync().ConfigureAwait(false);
            try
            {
                lock (CacheLock)
                {
                    if (Cache.TryGetValue(fetchUrl, out var hit))
                        return hit;
                }

                image = await DownloadAsync(sourceUrl, fetchUrl).ConfigureAwait(false);
                if (image is null && !string.Equals(fetchUrl, sourceUrl, StringComparison.Ordinal))
                    image = await DownloadAsync(sourceUrl, sourceUrl).ConfigureAwait(false);
            }
            finally
            {
                DownloadGate.Release();
            }
        }
        catch
        {
            image = null;
        }
        finally
        {
            InFlight.TryRemove(fetchUrl, out _);
        }

        lock (CacheLock)
        {
            if (Cache.ContainsKey(fetchUrl))
                return Cache[fetchUrl];
            Cache[fetchUrl] = image;
            CacheOrder.Enqueue(fetchUrl);
            while (Cache.Count > MaxCached && CacheOrder.Count > 0)
            {
                var old = CacheOrder.Dequeue();
                Cache.Remove(old);
            }
        }

        return image;
    }

    private static async Task<LoadedCommentImage?> DownloadAsync(string sourceUrl, string fetchUrl)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, fetchUrl);
        if (Uri.TryCreate(fetchUrl, UriKind.Absolute, out var fetchUri) &&
            CommentBodyParser.IsTwimgHost(fetchUri.Host))
        {
            req.Headers.Referrer = new Uri("https://x.com/");
            req.Headers.TryAddWithoutValidation("Origin", "https://x.com");
        }

        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null;

        var media = resp.Content.Headers.ContentType?.MediaType;
        if (!string.IsNullOrEmpty(media) &&
            (media.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
             media.Contains("json", StringComparison.OrdinalIgnoreCase) ||
             media.Contains("html", StringComparison.OrdinalIgnoreCase)))
            return null;

        var declared = resp.Content.Headers.ContentLength;
        if (declared is > MaxBytes)
            return null;

        await using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buf = new byte[8192];
        var total = 0;
        while (true)
        {
            var n = await stream.ReadAsync(buf.AsMemory(0, buf.Length)).ConfigureAwait(false);
            if (n == 0) break;
            total += n;
            if (total > MaxBytes)
                return null;
            ms.Write(buf, 0, n);
        }

        if (ms.Length < 24)
            return null;

        return Decode(sourceUrl, ms.ToArray());
    }

    private static LoadedCommentImage? Decode(string sourceUrl, byte[] bytes)
    {
        if (IsGif(bytes))
        {
            var gif = DecodeGif(sourceUrl, bytes);
            if (gif is not null)
                return gif;
        }

        var still = DecodeStill(bytes);
        if (still is null)
            return null;
        return new LoadedCommentImage { SourceUrl = sourceUrl, Preview = still };
    }

    private static bool IsGif(byte[] bytes) =>
        bytes.Length >= 6 &&
        bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' &&
        bytes[3] == (byte)'8' && (bytes[4] == (byte)'7' || bytes[4] == (byte)'9') &&
        bytes[5] == (byte)'a';

    private static LoadedCommentImage? DecodeGif(string sourceUrl, byte[] bytes)
    {
        try
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess())
                return dispatcher.Invoke(() => DecodeGif(sourceUrl, bytes));

            using var ms = new MemoryStream(bytes, writable: false);
            var decoder = new GifBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
                return null;

            var canvasW = MetaInt(decoder.Metadata as BitmapMetadata, "/logscrdesc/Width", 0);
            var canvasH = MetaInt(decoder.Metadata as BitmapMetadata, "/logscrdesc/Height", 0);
            var inputs = new CommentGifCompositor.FrameInput[decoder.Frames.Count];
            for (var i = 0; i < decoder.Frames.Count; i++)
            {
                var frame = decoder.Frames[i];
                var md = frame.Metadata as BitmapMetadata;
                var fw = Math.Max(1, MetaInt(md, "/imgdesc/Width", frame.PixelWidth));
                var fh = Math.Max(1, MetaInt(md, "/imgdesc/Height", frame.PixelHeight));
                var left = Math.Max(0, MetaInt(md, "/imgdesc/Left", 0));
                var top = Math.Max(0, MetaInt(md, "/imgdesc/Top", 0));
                canvasW = Math.Max(canvasW, left + fw);
                canvasH = Math.Max(canvasH, top + fh);

                var bgra = CopyBgra(frame);
                if (bgra is null)
                    return StillFromFrame(sourceUrl, decoder.Frames[0]);

                inputs[i] = new CommentGifCompositor.FrameInput
                {
                    Bgra = bgra,
                    Width = frame.PixelWidth,
                    Height = frame.PixelHeight,
                    Left = left,
                    Top = top,
                    DelayMs = GifDelayMs(frame),
                    Disposal = MetaInt(md, "/grctlext/Disposal", 0),
                };
            }

            if (canvasW <= 0) canvasW = decoder.Frames[0].PixelWidth;
            if (canvasH <= 0) canvasH = decoder.Frames[0].PixelHeight;
            if (canvasW <= 0 || canvasH <= 0)
                return null;

            var animate = bytes.Length <= MaxGifBytesToAnimate &&
                          decoder.Frames.Count is > 1 and <= MaxGifFrames &&
                          (long)canvasW * canvasH * decoder.Frames.Count <= MaxGifPixelFrames;

            var composed = CommentGifCompositor.Compose(
                canvasW, canvasH,
                animate ? inputs : new[] { inputs[0] });
            if (composed.Length == 0)
                return StillFromFrame(sourceUrl, decoder.Frames[0]);

            var bitmaps = new BitmapSource[composed.Length];
            for (var i = 0; i < composed.Length; i++)
                bitmaps[i] = CreateFrozen(composed[i].Bgra, canvasW, canvasH);

            if (!animate)
                return new LoadedCommentImage { SourceUrl = sourceUrl, Preview = bitmaps[0] };

            var delays = new int[composed.Length];
            for (var i = 0; i < composed.Length; i++)
                delays[i] = composed[i].DelayMs;

            return new LoadedCommentImage
            {
                SourceUrl = sourceUrl,
                Preview = bitmaps[0],
                Frames = bitmaps,
                DelaysMs = delays,
            };
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource CreateFrozen(byte[] bgra, int width, int height)
    {
        var bmp = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        bmp.Freeze();
        if (DecodePixelWidth <= 0 || width <= DecodePixelWidth)
            return bmp;
        var scale = DecodePixelWidth / (double)width;
        var tb = new TransformedBitmap(bmp, new ScaleTransform(scale, scale));
        tb.Freeze();
        return tb;
    }

    private static LoadedCommentImage? StillFromFrame(string sourceUrl, BitmapFrame frame)
    {
        var still = FreezeFrame(frame, DecodePixelWidth);
        return still is null ? null : new LoadedCommentImage { SourceUrl = sourceUrl, Preview = still };
    }

    private static byte[]? CopyBgra(BitmapSource src)
    {
        try
        {
            BitmapSource conv = src;
            if (src.Format != PixelFormats.Bgra32)
            {
                var fc = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                fc.Freeze();
                conv = fc;
            }

            var stride = conv.PixelWidth * 4;
            var buf = new byte[stride * conv.PixelHeight];
            conv.CopyPixels(buf, stride, 0);
            return buf;
        }
        catch
        {
            return null;
        }
    }

    private static int MetaInt(BitmapMetadata? md, string query, int fallback)
    {
        try
        {
            if (md is null || !md.ContainsQuery(query))
                return fallback;
            var v = md.GetQuery(query);
            return v is null ? fallback : Convert.ToInt32(v);
        }
        catch
        {
            return fallback;
        }
    }

    private static BitmapSource? DecodeStill(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (bytes.Length > 250_000)
                bmp.DecodePixelWidth = DecodePixelWidth;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? FreezeFrame(BitmapFrame frame, int maxWidth)
    {
        try
        {
            BitmapSource src = frame;
            if (src.CanFreeze) src.Freeze();
            if (maxWidth > 0 && src.PixelWidth > maxWidth)
            {
                var scale = maxWidth / (double)src.PixelWidth;
                var tb = new TransformedBitmap(src, new ScaleTransform(scale, scale));
                tb.Freeze();
                return tb;
            }
            return src;
        }
        catch
        {
            return null;
        }
    }

    private static int GifDelayMs(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata md && md.ContainsQuery("/grctlext/Delay"))
            {
                var v = md.GetQuery("/grctlext/Delay");
                var hundredths = v switch
                {
                    ushort u => u,
                    short s => s,
                    int i => i,
                    _ => 10,
                };
                if (hundredths < 2)
                    return 100;
                return hundredths * 10;
            }
        }
        catch
        {
            // ignore
        }

        return 100;
    }

    public static void OpenInBrowser(string? url)
    {
        if (!CommentBodyParser.IsHttpUrl(url))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(url!) { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }
}
