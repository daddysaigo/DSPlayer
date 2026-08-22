using DSPlayer.Services;
using Xunit;

namespace DSPlayer.Tests;

public class CommentGifCompositorTests
{
    [Fact]
    public void Compose_TransparentPatch_KeepsPreviousPixels()
    {
        // 2x2 red, then a 1x1 blue at (1,1) with the rest of the frame transparent
        var red = Opaque(2, 2, 255, 0, 0);
        var patch = new byte[4]; // 1x1 blue
        patch[0] = 255;
        patch[1] = 0;
        patch[2] = 0;
        patch[3] = 255;

        var outFrames = CommentGifCompositor.Compose(2, 2, new[]
        {
            new CommentGifCompositor.FrameInput
            {
                Bgra = red, Width = 2, Height = 2, DelayMs = 100, Disposal = 1,
            },
            new CommentGifCompositor.FrameInput
            {
                Bgra = patch, Width = 1, Height = 1, Left = 1, Top = 1, DelayMs = 100, Disposal = 1,
            },
        });

        Assert.Equal(2, outFrames.Length);
        Assert.Equal(16, outFrames[1].Bgra.Length);
        // (0,0) still red
        AssertPixel(outFrames[1].Bgra, 2, 0, 0, r: 255, g: 0, b: 0, a: 255);
        // (1,1) now blue (bgra)
        AssertPixel(outFrames[1].Bgra, 2, 1, 1, r: 0, g: 0, b: 255, a: 255);
    }

    [Fact]
    public void Compose_DisposalRestoreBackground_ClearsPreviousRect()
    {
        var red = Opaque(2, 2, 255, 0, 0);
        var blue = new byte[] { 255, 0, 0, 255 };

        var outFrames = CommentGifCompositor.Compose(2, 2, new[]
        {
            new CommentGifCompositor.FrameInput
            {
                Bgra = red, Width = 2, Height = 2, DelayMs = 50, Disposal = 2,
            },
            new CommentGifCompositor.FrameInput
            {
                Bgra = blue, Width = 1, Height = 1, DelayMs = 50, Disposal = 1,
            },
        });

        // After frame0 (disposal 2) the canvas is cleared, then blue is drawn at 0,0
        AssertPixel(outFrames[1].Bgra, 2, 0, 0, r: 0, g: 0, b: 255, a: 255);
        AssertPixel(outFrames[1].Bgra, 2, 1, 1, r: 0, g: 0, b: 0, a: 0);
    }

    [Fact]
    public void Compose_EveryFrameSameSize()
    {
        var a = Opaque(3, 2, 10, 20, 30);
        var b = Opaque(1, 1, 1, 2, 3);
        var outFrames = CommentGifCompositor.Compose(3, 2, new[]
        {
            new CommentGifCompositor.FrameInput { Bgra = a, Width = 3, Height = 2, DelayMs = 10, Disposal = 1 },
            new CommentGifCompositor.FrameInput { Bgra = b, Width = 1, Height = 1, Left = 2, Top = 1, DelayMs = 10, Disposal = 1 },
        });
        Assert.Equal(outFrames[0].Bgra.Length, outFrames[1].Bgra.Length);
        Assert.Equal(3 * 2 * 4, outFrames[0].Bgra.Length);
    }

    private static byte[] Opaque(int w, int h, byte r, byte g, byte b)
    {
        var p = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            p[i * 4] = b;
            p[i * 4 + 1] = g;
            p[i * 4 + 2] = r;
            p[i * 4 + 3] = 255;
        }
        return p;
    }

    private static void AssertPixel(byte[] bgra, int width, int x, int y, byte r, byte g, byte b, byte a)
    {
        var i = (y * width + x) * 4;
        Assert.Equal(b, bgra[i]);
        Assert.Equal(g, bgra[i + 1]);
        Assert.Equal(r, bgra[i + 2]);
        Assert.Equal(a, bgra[i + 3]);
    }
}
