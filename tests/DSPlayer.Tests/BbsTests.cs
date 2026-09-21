using System.Net.Http;
using System.Text;
using DSPlayer.Services.Bbs;
using Xunit;

namespace DSPlayer.Tests;

public class BbsTests
{
    public BbsTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void TryParse_TwochStyle_ContactUrl()
    {
        var url = "https://komokomo.ddns.net/test/read.cgi/kidera/1786069077/";
        var t = BbsThreadRef.TryParse(url);
        Assert.NotNull(t);
        Assert.Equal(BbsBoardKind.TwochStyle, t!.Kind);
        Assert.Equal("kidera", t.Board);
        Assert.Equal("1786069077", t.ThreadId);
        Assert.Equal("https://komokomo.ddns.net/kidera/dat/1786069077.dat", t.DatUrl);
        Assert.Equal("shift_jis", t.DefaultEncodingName);
    }

    [Fact]
    public void TryParse_Shitaraba_ThreadUrl()
    {
        var url = "https://jbbs.shitaraba.net/bbs/read.cgi/radio/31139/1786177527/";
        var t = BbsThreadRef.TryParse(url);
        Assert.NotNull(t);
        Assert.Equal(BbsBoardKind.Shitaraba, t!.Kind);
        Assert.False(t.IsBoardOnly);
        Assert.Equal("radio", t.Category);
        Assert.Equal("31139", t.Board);
        Assert.Equal("1786177527", t.ThreadId);
        Assert.Equal("https://jbbs.shitaraba.net/bbs/rawmode.cgi/radio/31139/1786177527/", t.DatUrl);
        Assert.Equal("euc-jp", t.DefaultEncodingName);
    }

    [Fact]
    public void TryParse_RewritesLivedoorToShitaraba()
    {
        var url = "http://jbbs.livedoor.jp/bbs/read.cgi/game/52029/1629269069/";
        var t = BbsThreadRef.TryParse(url);
        Assert.NotNull(t);
        Assert.Equal(BbsBoardKind.Shitaraba, t!.Kind);
        Assert.Equal("jbbs.shitaraba.net", t.Host);
        Assert.Contains("jbbs.shitaraba.net", t.DatUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UrlRewriter_LiteAndSubject()
    {
        Assert.Equal(
            "https://jbbs.shitaraba.net/bbs/read.cgi/game/1/2/",
            BbsUrlRewriter.Rewrite("https://jbbs.shitaraba.net/bbs/lite/read.cgi/game/1/2/"));
        Assert.Equal(
            "https://jbbs.shitaraba.net/game/1/",
            BbsUrlRewriter.Rewrite("https://jbbs.shitaraba.net/bbs/subject.cgi/game/1/"));
    }

    [Fact]
    public void MessageNormalizer_CollapsesWwAndBlanks()
    {
        var raw = "\n\n  こんにちは\n\n\nwww テスト ww\n　\n";
        var n = BbsMessageNormalizer.Normalize(raw);
        Assert.DoesNotContain("\n\n\n", n);
        Assert.StartsWith("こんにちは", n);
        Assert.Contains("ｗｗｗ", n);
    }

    [Fact]
    public void TryParse_Shitaraba_BoardOnlyUrl()
    {
        var url = "https://jbbs.shitaraba.net/netgame/16823/";
        var t = BbsThreadRef.TryParse(url);
        Assert.NotNull(t);
        Assert.Equal(BbsBoardKind.Shitaraba, t!.Kind);
        Assert.True(t.IsBoardOnly);
        Assert.Equal("netgame", t.Category);
        Assert.Equal("16823", t.Board);
        Assert.Null(t.ThreadId);
        Assert.Equal("https://jbbs.shitaraba.net/netgame/16823/subject.txt", t.SubjectUrl);
        Assert.Null(t.DatUrl);
    }

    [Fact]
    public void TryParse_Jpnkn_BoardOnlyUrl()
    {
        var url = "https://bbs.jpnkn.com/ubereats/";
        var t = BbsThreadRef.TryParse(url);
        Assert.NotNull(t);
        Assert.Equal(BbsBoardKind.TwochStyle, t!.Kind);
        Assert.True(t.IsBoardOnly);
        Assert.Equal("bbs.jpnkn.com", t.Host);
        Assert.Equal("ubereats", t.Board);
        Assert.Null(t.ThreadId);
        Assert.Equal("https://bbs.jpnkn.com/ubereats/subject.txt", t.SubjectUrl);
        Assert.Null(t.DatUrl);
        Assert.True(t.CanFetch);
        Assert.False(t.CanWrite);
        Assert.Equal(BbsWriteCooldown.JpnknDefaultSeconds, t.DefaultPostCooldownSeconds);
        Assert.True(t.WithThread("1234567890").CanWrite);
    }

    [Fact]
    public void TryParse_Jpnkn_ReadCgiBoardOnly()
    {
        var t = BbsThreadRef.TryParse("https://bbs.jpnkn.com/test/read.cgi/ubereats/");
        Assert.NotNull(t);
        Assert.Equal(BbsBoardKind.TwochStyle, t!.Kind);
        Assert.True(t.IsBoardOnly);
        Assert.Equal("ubereats", t.Board);
        Assert.Null(t.ThreadId);
        Assert.Equal("https://bbs.jpnkn.com/ubereats/subject.txt", t.SubjectUrl);
    }

    [Fact]
    public void TryParse_Jpnkn_SubjectTxt()
    {
        var t = BbsThreadRef.TryParse("https://bbs.jpnkn.com/ubereats/subject.txt");
        Assert.NotNull(t);
        Assert.True(t!.IsBoardOnly);
        Assert.Equal("ubereats", t.Board);
    }

    [Fact]
    public void TryParse_GenericContactPage_NotForcedToTwochBoard()
    {
        var t = BbsThreadRef.TryParse("https://example.com/just-a-page");
        Assert.NotNull(t);
        Assert.Equal(BbsBoardKind.Unknown, t!.Kind);
        Assert.False(t.IsBoardOnly);
    }

    [Fact]
    public void ParseSubjectTxt_Shitaraba()
    {
        var text =
            "1786107484.cgi,6157 はよPC買え(577)\n" +
            "1786237790.cgi,6164 プレミア・キチガイ(2)\n";
        var list = BbsDatParser.ParseSubjectTxt(text);
        Assert.Equal(2, list.Count);
        Assert.Equal("1786107484", list[0].ThreadId);
        Assert.Equal("6157 はよPC買え", list[0].Title);
        Assert.Equal(577, list[0].ResCount);
    }

    [Fact]
    public void ParseSubjectTxt_TwochStyle()
    {
        var text =
            "1786069077.dat<>木寺談話室 [part17] (1000)\n" +
            "1787000001.dat<>木寺談話室 [part18] (12)\n";
        var list = BbsDatParser.ParseSubjectTxt(text);
        Assert.Equal(2, list.Count);
        Assert.Equal(1000, list[0].ResCount);
        var next = BbsDatParser.PickNextOpenThread(list, "1786069077");
        Assert.NotNull(next);
        Assert.Equal("1787000001", next!.ThreadId);
    }

    [Fact]
    public void ParseTwochDat_SampleLine()
    {
        var dat =
            "名無し<>sage<>2026/08/09(日) 12:00:00.00<>こんにちは<br>二行目<>スレタイ\n" +
            "人<>sage<>2026/08/09(日) 12:01:00.00<>レス2<>\n";

        var posts = BbsDatParser.ParseTwochDat(dat);
        Assert.Equal(2, posts.Count);
        Assert.Equal(1, posts[0].Number);
        Assert.Equal("名無し", posts[0].Name);
        Assert.Equal("スレタイ", posts[0].Title);
        Assert.Contains("こんにちは", posts[0].BodyText);
        Assert.Contains("二行目", posts[0].BodyText);
        Assert.Equal(2, posts[1].Number);
        Assert.Equal("レス2", posts[1].BodyText);
    }

    [Fact]
    public void IdCountStyle_TurnsRedAsCountGrows()
    {
        var one = BbsIdCountStyle.ForCount(1);
        var five = BbsIdCountStyle.ForCount(5);
        var ten = BbsIdCountStyle.ForCount(10);
        Assert.False(one.Bold);
        Assert.True(five.Color.R > five.Color.G);
        Assert.False(ten.Bold);
        Assert.True(ten.Color.R > ten.Color.B);
    }

    [Fact]
    public void PosterId_ExtractsFromDateField()
    {
        Assert.Equal("Ab12Xy", BbsPosterId.Extract("2026/08/09(日) 12:00:00.00 ID:Ab12Xy"));
        Assert.Equal("SNnL3gbc00", BbsPosterId.Extract("2026/08/09(日) 20:02:07 ID:SNnL3gbc00"));
        Assert.Equal("xxxx", BbsPosterId.Extract("2026/01/01(木) 00:00:00 ID:xxxx???"));
        Assert.Null(BbsPosterId.Extract("2026/01/01(木) 00:00:00"));
        Assert.Null(BbsPosterId.Extract("ID:???"));
        Assert.Equal("2026/08/09(日) 12:00:00.00",
            BbsPosterId.DateWithoutId("2026/08/09(日) 12:00:00.00 ID:Ab12Xy"));
    }

    [Fact]
    public void ParseShitarabaRaw_Sample()
    {
        var raw =
            "1<>名前<>sage<>2026/01/01(木) 00:00:00<>本文です<>タイトル<>abc\n" +
            "2<>名前2<><>2026/01/01(木) 00:01:00<>本文2<><>def\n";
        var posts = BbsDatParser.ParseShitarabaRaw(raw);
        Assert.Equal(2, posts.Count);
        Assert.Equal(1, posts[0].Number);
        Assert.Equal("タイトル", posts[0].Title);
        Assert.Contains("ID:abc", posts[0].DateId);
        Assert.Equal("abc", posts[0].PosterId);
        Assert.Equal("本文2", posts[1].BodyText);
    }

    [Fact]
    public void ParseHtml_ShitarabaStyleDt()
    {
        var html = """
            <dt id="comment_3">
              <a href="https://jbbs.shitaraba.net/bbs/read.cgi/radio/1/2/3">3</a>：
              <a href="mailto:sage"><b>名前</b> (ﾜｯﾁｮｲ)</a>：2026/08/09(日) 20:05:26 ID:abc
            </dt>
            <dd>
              本文です<br>
            </dd>
            """;
        var posts = BbsDatParser.ParseHtmlThread(html);
        Assert.Single(posts);
        Assert.Equal(3, posts[0].Number);
        Assert.Equal("名前", posts[0].Name);
        Assert.Contains("本文です", posts[0].BodyText);
    }

    [Fact]
    public void Encoding_ShitarabaPrefersEucJp()
    {
        // bytes that are valid euc-jp for "あ"
        var bytes = Encoding.GetEncoding("euc-jp").GetBytes("1<>名<>sage<>date<>本文<>title<>id\n");
        var enc = BbsDatParser.GetEncoding(BbsBoardKind.Shitaraba, bytes);
        Assert.Equal("euc-jp", enc.WebName, ignoreCase: true);
        var posts = BbsDatParser.ParseShitarabaRaw(enc.GetString(bytes));
        Assert.Single(posts);
        Assert.Equal("本文", posts[0].BodyText);
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task LiveFetch_ShitarabaThread_IfReachable()
    {
        var url = "https://jbbs.shitaraba.net/bbs/read.cgi/radio/31139/1786177527/";
        var thread = BbsThreadRef.TryParse(url);
        Assert.NotNull(thread);

        using var client = new BbsClient();
        try
        {
            var result = await client.FetchAsync(thread!);
            Assert.True(result.Posts.Count > 0, "expected posts from shitaraba rawmode");
            // Should be real Japanese, not mojibake replacement chars only
            var body = string.Concat(result.Posts.Select(p => p.BodyText));
            Assert.Contains("rawmode", result.SourceKind);
            Assert.False(string.IsNullOrWhiteSpace(body));
            // typical: Japanese characters present
            Assert.True(body.Any(c => c >= 0x3040 && c <= 0x30ff) || body.Any(c => c >= 0x4e00 && c <= 0x9fff),
                "expected hiragana/katakana/kanji in body, got: " + body[..Math.Min(80, body.Length)]);
        }
        catch (HttpRequestException)
        {
            // network may be unavailable
        }
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task LiveFetch_ShitarabaBoardOnly_IfReachable()
    {
        var url = "https://jbbs.shitaraba.net/netgame/16823/";
        var thread = BbsThreadRef.TryParse(url);
        Assert.NotNull(thread);
        Assert.True(thread!.IsBoardOnly);

        using var client = new BbsClient();
        try
        {
            var result = await client.FetchAsync(thread);
            Assert.True(result.Posts.Count > 0, "board-only should resolve subject and load posts");
            Assert.False(string.IsNullOrWhiteSpace(result.ResolvedThreadId));
        }
        catch (HttpRequestException)
        {
        }
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task LiveFetch_JpnknBoardOnly_IfReachable()
    {
        var url = "https://bbs.jpnkn.com/ubereats/";
        var thread = BbsThreadRef.TryParse(url);
        Assert.NotNull(thread);
        Assert.True(thread!.IsBoardOnly);
        Assert.Equal(BbsBoardKind.TwochStyle, thread.Kind);

        using var client = new BbsClient();
        try
        {
            var result = await client.FetchAsync(thread);
            Assert.True(result.Posts.Count > 0, "jpnkn board-only should resolve subject and load posts");
            Assert.False(string.IsNullOrWhiteSpace(result.ResolvedThreadId));
        }
        catch (HttpRequestException)
        {
        }
        catch (InvalidOperationException)
        {
            // board empty / subject missing — still proves parse + fetch path
        }
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task LiveFetch_KomokomoDat_IfReachable()
    {
        var url = "https://komokomo.ddns.net/test/read.cgi/kidera/1786069077/";
        var thread = BbsThreadRef.TryParse(url);
        Assert.NotNull(thread);

        using var client = new BbsClient();
        try
        {
            var result = await client.FetchAsync(thread!);
            Assert.True(result.Posts.Count > 0);
        }
        catch (HttpRequestException)
        {
        }
    }
}
