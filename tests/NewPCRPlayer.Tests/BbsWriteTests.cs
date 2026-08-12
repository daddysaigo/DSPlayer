using System.Text;
using NewPCRPlayer.Services.Bbs;
using Xunit;

namespace NewPCRPlayer.Tests;

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
}
