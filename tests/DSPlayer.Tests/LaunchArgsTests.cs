using DSPlayer.Models;
using Xunit;

namespace DSPlayer.Tests;

public class LaunchArgsTests
{
    [Fact]
    public void Parse_StreamUrlAndChannelName()
    {
        var id = "0123456789abcdef0123456789abcdef";
        var url = $"http://localhost:7144/stream/{id}";
        var args = LaunchArgs.Parse(new[] { url, "木寺 (FLV)" });

        Assert.Equal(url, args.StreamUrl);
        Assert.Equal(url, args.PlaybackUrl);
        Assert.Equal("木寺 (FLV)", args.ChannelName);
        Assert.Equal(id, args.ChannelId);
        Assert.True(args.HasStream);
        Assert.False(args.HasContact);
    }

    [Fact]
    public void Parse_PeCaRecorder_Canonical_X0_3()
    {
        // Stock PeCaRecorder: arg = "$x" "$0" "$3"
        var id = "81CF10184D84B31B48FE8F08DF69B0DF";
        var pls = $"http://127.0.0.1:7144/pls/{id}?tip=118.109.131.163:7144";
        var contact = "https://komokomo.ddns.net/test/read.cgi/kidera/1786069077/";
        var args = LaunchArgs.Parse(new[] { pls, "木寺", contact });

        Assert.Equal(pls, args.StreamUrl);
        Assert.Equal(id, args.ChannelId);
        Assert.Equal("木寺", args.ChannelName);
        Assert.Equal(contact, args.ContactUrl);
        Assert.Contains("/stream/" + id, args.PlaybackUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tip=118.109.131.163:7144", args.PlaybackUrl);
        Assert.True(args.HasStream);
        Assert.True(args.HasContact);
    }

    [Fact]
    public void Parse_PeCaRecorder_ContactWithoutBbsKeyword_StillAcceptedAsPositional3()
    {
        // $3 may be a plain contact page (no "bbs" / "read.cgi" in path)
        var id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var stream = $"http://127.0.0.1:7144/stream/{id}";
        var contact = "https://example.com/mychannel/";
        var args = LaunchArgs.Parse(new[] { stream, "TestCh", contact });

        Assert.Equal(stream, args.StreamUrl);
        Assert.Equal("TestCh", args.ChannelName);
        Assert.Equal(contact, args.ContactUrl);
        Assert.True(args.HasContact);
    }

    [Fact]
    public void Parse_MediaAndContact_NoChannelName()
    {
        var id = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var stream = $"http://localhost:7144/stream/{id}";
        var contact = "https://bbs.jpnkn.com/test/read.cgi/foo/123/";
        var args = LaunchArgs.Parse(new[] { stream, contact });

        Assert.Equal(stream, args.StreamUrl);
        Assert.Equal(contact, args.ContactUrl);
        Assert.Null(args.ChannelName);
    }

    [Fact]
    public void Parse_BoardOnlyContact_Jpnkn()
    {
        var id = "cccccccccccccccccccccccccccccccc";
        var pls = $"http://127.0.0.1:7144/pls/{id}?tip=1.2.3.4:7144";
        var board = "https://bbs.jpnkn.com/ubereats/";
        var args = LaunchArgs.Parse(new[] { pls, "UberEats", board });

        Assert.Equal(board, args.ContactUrl);
        Assert.True(LaunchArgs.IsContactUrl(board));
    }

    [Fact]
    public void ToPlaybackUrl_ConvertsPlsToStream_KeepsQuery()
    {
        var id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var pls = $"http://127.0.0.1:7144/pls/{id}?tip=1.2.3.4:7144";
        var play = LaunchArgs.ToPlaybackUrl(pls);
        Assert.Equal($"http://127.0.0.1:7144/stream/{id}?tip=1.2.3.4:7144", play);
    }

    [Fact]
    public void ToPlaybackUrl_StreamUnchanged()
    {
        var id = "dddddddddddddddddddddddddddddddd";
        var stream = $"http://127.0.0.1:7144/stream/{id}?tip=9.9.9.9:7144";
        Assert.Equal(stream, LaunchArgs.ToPlaybackUrl(stream));
    }

    [Fact]
    public void Parse_QuotedStyleArgs()
    {
        var id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var url = $"http://127.0.0.1:7144/stream/{id}";
        var args = LaunchArgs.Parse(new[] { $"\"{url}\"", "\"Test Ch\"" });

        Assert.Equal(url, args.StreamUrl);
        Assert.Equal("Test Ch", args.ChannelName);
    }

    [Fact]
    public void Parse_Empty_NoStream()
    {
        var args = LaunchArgs.Parse(Array.Empty<string>());
        Assert.False(args.HasStream);
        Assert.Null(args.ChannelId);
        Assert.False(args.HasContact);
    }

    [Fact]
    public void LooksLikeStreamUrl_AcceptsPlsAndStream()
    {
        Assert.True(LaunchArgs.LooksLikeStreamUrl(
            "http://localhost:7144/stream/0123456789abcdef0123456789abcdef"));
        Assert.True(LaunchArgs.LooksLikeStreamUrl(
            "http://127.0.0.1:7144/pls/0123456789abcdef0123456789abcdef?tip=1.2.3.4:7144"));
        Assert.False(LaunchArgs.LooksLikeStreamUrl("not-a-url"));
        Assert.False(LaunchArgs.IsContactUrl(
            "http://127.0.0.1:7144/pls/0123456789abcdef0123456789abcdef"));
        Assert.True(LaunchArgs.IsContactUrl(
            "https://komokomo.ddns.net/test/read.cgi/kidera/1/"));
    }

    [Fact]
    public void IsContactUrl_JpnknAndShitaraba()
    {
        Assert.True(LaunchArgs.IsContactUrl("https://bbs.jpnkn.com/ubereats/"));
        Assert.True(LaunchArgs.IsContactUrl(
            "https://jbbs.shitaraba.net/bbs/read.cgi/internet/25178/1/"));
        Assert.False(LaunchArgs.IsContactUrl("https://example.com/just-a-page"));
    }
}
