namespace DSPlayer.Services;

/// <summary>
/// GIF frames are often dirty-rects with transparency (unchanged pixels).
/// Playing them as standalone images punches holes and changes control size.
/// This composites onto a fixed canvas using GIF disposal methods.
/// </summary>
internal static class CommentGifCompositor
{
    public readonly struct FrameInput
    {
        public required byte[] Bgra { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public int Left { get; init; }
        public int Top { get; init; }
        public int DelayMs { get; init; }
        /// <summary>0/1 keep, 2 restore background, 3 restore previous.</summary>
        public int Disposal { get; init; }
    }

    public readonly struct FrameOutput
    {
        public required byte[] Bgra { get; init; }
        public required int DelayMs { get; init; }
    }

    public static FrameOutput[] Compose(int canvasW, int canvasH, IReadOnlyList<FrameInput> frames)
    {
        if (canvasW <= 0 || canvasH <= 0 || frames is not { Count: > 0 })
            return Array.Empty<FrameOutput>();

        var stride = canvasW * 4;
        var canvas = new byte[stride * canvasH];
        byte[]? backup = null;
        var prevDisposal = 0;
        var prevLeft = 0;
        var prevTop = 0;
        var prevW = 0;
        var prevH = 0;
        var result = new FrameOutput[frames.Count];

        for (var i = 0; i < frames.Count; i++)
        {
            var f = frames[i];
            ApplyPreviousDisposal(canvas, stride, canvasW, canvasH,
                prevDisposal, prevLeft, prevTop, prevW, prevH, backup);

            if (f.Disposal == 3)
                backup = (byte[])canvas.Clone();

            Blit(canvas, canvasW, canvasH, f.Bgra, f.Width, f.Height, f.Left, f.Top);

            result[i] = new FrameOutput
            {
                Bgra = (byte[])canvas.Clone(),
                DelayMs = f.DelayMs > 0 ? f.DelayMs : 100,
            };

            prevDisposal = f.Disposal;
            prevLeft = f.Left;
            prevTop = f.Top;
            prevW = f.Width;
            prevH = f.Height;
        }

        return result;
    }

    private static void ApplyPreviousDisposal(
        byte[] canvas, int stride, int canvasW, int canvasH,
        int disposal, int left, int top, int w, int h, byte[]? backup)
    {
        if (disposal == 2)
        {
            ClearRect(canvas, stride, canvasW, canvasH, left, top, w, h);
            return;
        }

        if (disposal == 3 && backup is not null && backup.Length == canvas.Length)
            Buffer.BlockCopy(backup, 0, canvas, 0, canvas.Length);
    }

    public static void Blit(
        byte[] dest, int dw, int dh,
        byte[] src, int sw, int sh, int dx, int dy)
    {
        var srcStride = sw * 4;
        var dstStride = dw * 4;
        for (var y = 0; y < sh; y++)
        {
            var gy = dy + y;
            if ((uint)gy >= (uint)dh)
                continue;
            for (var x = 0; x < sw; x++)
            {
                var gx = dx + x;
                if ((uint)gx >= (uint)dw)
                    continue;
                var si = y * srcStride + x * 4;
                var a = src[si + 3];
                if (a == 0)
                    continue;
                var di = gy * dstStride + gx * 4;
                if (a == 255)
                {
                    dest[di] = src[si];
                    dest[di + 1] = src[si + 1];
                    dest[di + 2] = src[si + 2];
                    dest[di + 3] = 255;
                    continue;
                }

                var sa = a / 255.0;
                var da = dest[di + 3] / 255.0;
                var outA = sa + da * (1 - sa);
                if (outA <= 0)
                    continue;
                dest[di] = (byte)Math.Round((src[si] * sa + dest[di] * da * (1 - sa)) / outA);
                dest[di + 1] = (byte)Math.Round((src[si + 1] * sa + dest[di + 1] * da * (1 - sa)) / outA);
                dest[di + 2] = (byte)Math.Round((src[si + 2] * sa + dest[di + 2] * da * (1 - sa)) / outA);
                dest[di + 3] = (byte)Math.Round(outA * 255);
            }
        }
    }

    public static void ClearRect(byte[] dest, int stride, int dw, int dh, int x, int y, int w, int h)
    {
        var x0 = Math.Max(0, x);
        var y0 = Math.Max(0, y);
        var x1 = Math.Min(dw, x + w);
        var y1 = Math.Min(dh, y + h);
        if (x0 >= x1 || y0 >= y1)
            return;
        var bytes = (x1 - x0) * 4;
        for (var row = y0; row < y1; row++)
            Array.Clear(dest, row * stride + x0 * 4, bytes);
    }
}
