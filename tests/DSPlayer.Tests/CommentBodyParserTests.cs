using DSPlayer.Services.Bbs;
using Xunit;

namespace DSPlayer.Tests;

public class CommentBodyParserTests
{
    [Fact]
    public void Parse_Empty_IsEmpty()
    {
        Assert.Empty(CommentBodyParser.Parse(null));
        Assert.Empty(CommentBodyParser.Parse(""));
    }

    [Fact]
    public void Parse_PlainText_IsOneTextSegment()
    {
        var segs = CommentBodyParser.Parse("ただの本文");
        Assert.Single(segs);
        Assert.Equal(CommentSegmentKind.Text, segs[0].Kind);
        Assert.Equal("ただの本文", segs[0].Text);
    }

    [Fact]
    public void Parse_AnchorAndUrl_InOrder()
    {
        var segs = CommentBodyParser.Parse(">>12 見て https://example.com/a だ");
        Assert.Equal(CommentSegmentKind.Anchor, segs[0].Kind);
        Assert.Equal(12, segs[0].ResNumber);
        Assert.Equal(CommentSegmentKind.Text, segs[1].Kind);
        Assert.Equal(CommentSegmentKind.Url, segs[2].Kind);
        Assert.Equal("https://example.com/a", segs[2].NavigateUrl);
        Assert.Equal(CommentSegmentKind.Text, segs[3].Kind);
    }

    [Theory]
    [InlineData("ttp://example.com/x", "http://example.com/x")]
    [InlineData("ttps://example.com/x", "https://example.com/x")]
    [InlineData("HTTPS://Example.COM/x", "HTTPS://Example.COM/x")]
    public void NormalizeHref_FillsMissingH(string raw, string expected)
    {
        Assert.Equal(expected, CommentBodyParser.NormalizeHref(raw));
    }

    [Fact]
    public void Parse_StripsTrailingPunctFromUrl()
    {
        var segs = CommentBodyParser.Parse("link https://example.com/a.");
        var url = segs.Single(s => s.Kind == CommentSegmentKind.Url);
        Assert.Equal("https://example.com/a", url.Text);
        Assert.Equal("https://example.com/a", url.NavigateUrl);
        Assert.Equal(".", segs[^1].Text);
    }

    [Theory]
    [InlineData("https://i.imgur.com/abc.jpg", true)]
    [InlineData("https://i.imgur.com/abc.PNG", true)]
    [InlineData("https://cdn.example.com/x.jpeg?size=1", true)]
    [InlineData("https://pbs.twimg.com/media/ABC?format=jpg&name=large", true)]
    [InlineData("https://pbs.twimg.com/media/HP-Auh_bcAA_Fxc?format=jpg&name=large", true)]
    [InlineData("https://pbs.twimg.com/media/HP-Auh_bcAA_Fxc", true)]
    [InlineData("https://pbs.twimg.com/media/Foo.jpg:large", true)]
    [InlineData("https://example.com/page", false)]
    [InlineData("https://example.com/x.webp", false)]
    [InlineData("https://imgur.com/gallery/abc", false)]
    [InlineData("https://video.twimg.com/tweet_video/Foo.mp4", false)]
    public void LooksLikeImage_ByExtensionOrFormatQuery(string href, bool expected)
    {
        Assert.Equal(expected, CommentBodyParser.LooksLikeImage(href));
    }

    [Fact]
    public void Parse_TwitterMediaUrl_IsImage()
    {
        const string url = "https://pbs.twimg.com/media/HP-Auh_bcAA_Fxc?format=jpg&name=large";
        var segs = CommentBodyParser.Parse(url);
        Assert.Equal(CommentSegmentKind.Image, segs[0].Kind);
        Assert.Equal(url, segs[0].NavigateUrl);
    }

    [Fact]
    public void ToFetchUrl_TwitterLarge_BecomesSmall()
    {
        var src = "https://pbs.twimg.com/media/HP-Auh_bcAA_Fxc?format=jpg&name=large";
        var fetch = CommentBodyParser.ToFetchUrl(src);
        Assert.Equal("https://pbs.twimg.com/media/HP-Auh_bcAA_Fxc?format=jpg&name=small", fetch);
    }

    [Fact]
    public void ToFetchUrl_TwitterBareMedia_AddsFormat()
    {
        var fetch = CommentBodyParser.ToFetchUrl("https://pbs.twimg.com/media/HP-Auh_bcAA_Fxc");
        Assert.Equal("https://pbs.twimg.com/media/HP-Auh_bcAA_Fxc?format=jpg&name=small", fetch);
    }

    [Fact]
    public void Parse_ImageUrl_IsImageKind()
    {
        var segs = CommentBodyParser.Parse("ttps://i.imgur.com/zz.png");
        Assert.Equal(CommentSegmentKind.Image, segs[0].Kind);
        Assert.Equal("https://i.imgur.com/zz.png", segs[0].NavigateUrl);
        Assert.Equal("ttps://i.imgur.com/zz.png", segs[0].Text);
    }

    [Fact]
    public void Parse_TtpWithoutImage_IsUrl()
    {
        var segs = CommentBodyParser.Parse("ttp://example.com/thread");
        Assert.Equal(CommentSegmentKind.Url, segs[0].Kind);
        Assert.Equal("http://example.com/thread", segs[0].NavigateUrl);
    }

    [Fact]
    public void IsLoopbackUrl_TrueForLocalhost()
    {
        Assert.True(CommentBodyParser.IsLoopbackUrl("http://127.0.0.1:7144/foo.jpg"));
        Assert.False(CommentBodyParser.IsLoopbackUrl("https://i.imgur.com/a.jpg"));
    }
}
