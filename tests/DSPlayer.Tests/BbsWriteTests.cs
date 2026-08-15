using System.Text;
using DSPlayer.Services.Bbs;
using Xunit;

namespace DSPlayer.Tests;

public class BbsWriteTests
{
    public BbsWriteTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void WriteUrl_ShitarabaThread()
    {
        var t = BbsThreadRef.TryParse("https://jbbs.shitaraba.net/bbs/read.cgi/radio/31139/1786177527/");
        Assert.NotNull(t);
        Assert.True(t!.CanWrite);
        Assert.Equal("https://jbbs.shitaraba.net/bbs/write.cgi/radio/31139/1786177527/", t.WriteUrl);
    }

    [Fact]
    public void WriteUrl_TwochStyle()
    {
        var t = BbsThreadRef.TryParse("https://komokomo.ddns.net/test/read.cgi/kidera/1786069077/");
        Assert.NotNull(t);
        Assert.True(t!.CanWrite);
        Assert.Contains("/test/bbs.cgi", t.WriteUrl);
    }

    [Fact]
    public void WriteUrl_BoardOnly_NotYetWritable()
    {
        var t = BbsThreadRef.TryParse("https://jbbs.shitaraba.net/netgame/16823/");
        Assert.NotNull(t);
        Assert.False(t!.CanWrite);
        Assert.True(t.WithThread("1786107484").CanWrite);
    }

    [Fact]
    public async Task PostAsync_EmptyMessage_Fails()
    {
        var t = BbsThreadRef.TryParse("https://jbbs.shitaraba.net/bbs/read.cgi/radio/31139/1786177527/");
        using var w = new BbsWriter();
        var r = await w.PostAsync(t!, "name", "sage", "  ");
        Assert.False(r.Success);
    }

    [Fact]
    public void InterpretResponse_Flood_ShitarabaInterval()
    {
        var r = BbsWriter.InterpretResponse(
            httpOk: true,
            text: "ERROR: 投稿間隔が短すぎます。5秒待ってください。",
            kind: "したらば");
        Assert.False(r.Success);
        Assert.True(r.IsFloodLimited);
        Assert.Equal(5, r.RetryAfterSeconds);
        Assert.Contains("連投", r.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InterpretResponse_Flood_WithoutSeconds_LeavesRetryNull()
    {
        var r = BbsWriter.InterpretResponse(
            httpOk: true,
            text: "連投規制中です。もう少し待ってから書き込んでください。",
            kind: "2ch系");
        Assert.False(r.Success);
        Assert.True(r.IsFloodLimited);
        Assert.Null(r.RetryAfterSeconds);
    }

    [Fact]
    public void Cooldown_PerBoard_UsesEngineDefaultAfterSuccess()
    {
        var clock = new BbsWriteCooldown();
        var t0 = DateTime.UtcNow;
        var shita = BbsThreadRef.TryParse("https://jbbs.shitaraba.net/bbs/read.cgi/radio/31139/1/")!;
        var jpnkn = BbsThreadRef.TryParse("https://bbs.jpnkn.com/test/read.cgi/ubereats/1/")!;

        clock.NoteSuccess(shita, t0);
        clock.NoteSuccess(jpnkn, t0);

        Assert.Equal(BbsWriteCooldown.ShitarabaDefaultSeconds, clock.RemainingSeconds(shita, t0));
        Assert.Equal(BbsWriteCooldown.JpnknDefaultSeconds, clock.RemainingSeconds(jpnkn, t0));
        Assert.Equal(10, clock.KnownIntervalSeconds(shita));
        Assert.Equal(5, clock.KnownIntervalSeconds(jpnkn));
    }

    [Fact]
    public void Cooldown_FloodRemaining_IsAuthoritativeAndLearnsInterval()
    {
        var clock = new BbsWriteCooldown();
        var t0 = DateTime.UtcNow;
        var shita = BbsThreadRef.TryParse("https://jbbs.shitaraba.net/bbs/read.cgi/radio/31139/1/")!;

        clock.NoteSuccess(shita, t0);
        // posted, then 3s later the board says wait 7 more → interval is 10
        clock.NoteFlood(shita, retryAfterSeconds: 7, utc: t0.AddSeconds(3));

        Assert.Equal(7, clock.RemainingSeconds(shita, t0.AddSeconds(3)));
        Assert.Equal(10, clock.KnownIntervalSeconds(shita));
    }

    [Fact]
    public void Cooldown_DifferentBoardsAreIndependent()
    {
        var clock = new BbsWriteCooldown();
        var t0 = DateTime.UtcNow;
        var a = BbsThreadRef.TryParse("https://jbbs.shitaraba.net/bbs/read.cgi/radio/31139/1/")!;
        var b = BbsThreadRef.TryParse("https://jbbs.shitaraba.net/bbs/read.cgi/netgame/16823/1/")!;

        clock.NoteSuccess(a, t0);
        Assert.True(clock.RemainingSeconds(a, t0) > 0);
        Assert.Equal(0, clock.RemainingSeconds(b, t0));
    }

    [Fact]
    public void InterpretResponse_Flood_SambaStyleSeconds()
    {
        var r = BbsWriter.InterpretResponse(
            httpOk: true,
            text: "ＥＲＲＯＲ – 593 7 sec たたないと書けません。(2回目、1 sec しかたってない)",
            kind: "2ch系");
        Assert.False(r.Success);
        Assert.True(r.IsFloodLimited);
        Assert.Equal(7, r.RetryAfterSeconds);
    }

    [Fact]
    public void InterpretResponse_CookieConfirm_NotFlood()
    {
        var r = BbsWriter.InterpretResponse(
            httpOk: true,
            text: "cookieを確認してください。変更せずもう一度書きこんで下さい。1秒待って…",
            kind: "したらば");
        Assert.False(r.Success);
        Assert.True(r.NeedsConfirmRetry);
        Assert.False(r.IsFloodLimited);
    }

    [Fact]
    public void InterpretResponse_Success_NotFlood()
    {
        var r = BbsWriter.InterpretResponse(
            httpOk: true,
            text: "書きこみました。",
            kind: "したらば");
        Assert.True(r.Success);
        Assert.False(r.IsFloodLimited);
    }

    [Fact]
    public void InterpretResponse_ShitarabaDonePage_IsSuccessEvenIfPleaseWait()
    {
        var r = BbsWriter.InterpretResponse(
            httpOk: true,
            text: "<title>書きこみました。</title>書きこみが終わりました。<br>しばらくお待ち下さい。1秒後に自動的にジャンプします。",
            kind: "したらば");
        Assert.True(r.Success);
        Assert.False(r.IsFloodLimited);
        Assert.False(r.NeedsConfirmRetry);
    }

    [Fact]
    public void WriteUrl_JpnknBoardOnly_NotYetWritable()
    {
        var t = BbsThreadRef.TryParse("https://bbs.jpnkn.com/ubereats/");
        Assert.NotNull(t);
        Assert.False(t!.CanWrite);
        var resolved = t.WithThread("1780000000");
        Assert.True(resolved.CanWrite);
        Assert.Contains("/test/bbs.cgi", resolved.WriteUrl);
    }
}
