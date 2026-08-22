using DSPlayer.Services.Bbs;
using Xunit;

namespace DSPlayer.Tests;

public class ThreadMomentumTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 30)]
    [InlineData(4, 35)]
    [InlineData(5, 43)]
    [InlineData(6, 50)]
    [InlineData(7, 55)]
    [InlineData(10, 70)]
    [InlineData(11, 75)]
    [InlineData(15, 85)]
    [InlineData(16, 88)]
    [InlineData(25, 95)]
    [InlineData(35, 100)]
    [InlineData(80, 100)]
    public void ScoreFromCount_MatchesAnchors(int count, int expected)
    {
        Assert.Equal(expected, ThreadMomentum.ScoreFromCount(count));
    }

    [Fact]
    public void ScoreFromCount_OnePerMinuteSitsInTheForties()
    {
        var score = ThreadMomentum.ScoreFromCount(5);
        Assert.InRange(score, 40, 50);
    }

    [Fact]
    public void ScoreFromCount_InterpolatesBetweenAnchors()
    {
        var score = ThreadMomentum.ScoreFromCount(8);
        Assert.InRange(score, 56, 69);
        Assert.Equal(60, score); // 7→55, 10→70, t=1/3 → 60
    }

    [Theory]
    [InlineData(0, "過疎")]
    [InlineData(10, "過疎")]
    [InlineData(30, "過疎")]
    [InlineData(35, "落ち着いてる")]
    [InlineData(50, "落ち着いてる")]
    [InlineData(55, "普通")]
    [InlineData(70, "普通")]
    [InlineData(75, "活発")]
    [InlineData(85, "活発")]
    [InlineData(88, "熱い")]
    [InlineData(100, "熱い")]
    public void LabelFromScore_MatchesBands(int score, string expected)
    {
        Assert.Equal(expected, ThreadMomentum.LabelFromScore(score));
    }

    [Fact]
    public void FormatDisplay_AlwaysShowsScoreAndLabel()
    {
        Assert.Equal("勢い 0　過疎", ThreadMomentum.FormatDisplay(0));
        Assert.Equal("勢い 58　普通", ThreadMomentum.FormatDisplay(58));
    }

    [Theory]
    [InlineData(0, "過疎")]
    [InlineData(34, "過疎")]
    [InlineData(35, "ゆったり")]
    [InlineData(54, "ゆったり")]
    [InlineData(55, "平常運転")]
    [InlineData(74, "平常運転")]
    [InlineData(75, "にぎやか")]
    [InlineData(87, "にぎやか")]
    [InlineData(88, "大盛況")]
    [InlineData(100, "大盛況")]
    public void HeatLabelFromScore_MatchesBands(int score, string expected)
    {
        Assert.Equal(expected, ThreadMomentum.HeatLabelFromScore(score));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 1)]
    [InlineData(34, 1)]
    [InlineData(35, 2)]
    [InlineData(55, 3)]
    [InlineData(75, 4)]
    [InlineData(88, 5)]
    [InlineData(100, 5)]
    public void MeterFilledFromScore_FollowsBands(int score, int filled)
    {
        Assert.Equal(filled, ThreadMomentum.MeterFilledFromScore(score));
        ThreadMomentum.SplitMeter(score, out var on, out var off);
        Assert.Equal(filled, on.Length);
        Assert.Equal(5 - filled, off.Length);
    }

    [Fact]
    public void MeterBandRgb_SteelBlueGreenCoral()
    {
        Assert.Equal(5, ThreadMomentum.MeterBandRgb.Length);
        var quiet = ThreadMomentum.MeterColorFromScore(10);
        var cruise = ThreadMomentum.MeterColorFromScore(58);
        var hot = ThreadMomentum.MeterColorFromScore(95);
        Assert.True(quiet.B > quiet.R);          // スチール青
        Assert.True(cruise.G > cruise.R && cruise.G > cruise.B); // 平常運転は緑
        Assert.True(hot.R > hot.B + 40);         // コーラル橙
    }

    [Theory]
    [InlineData(null, "Heat")]
    [InlineData("Heat", "Heat")]
    [InlineData("Simple", "Simple")]
    [InlineData("classic", "Simple")]
    public void NormalizeStyle_DefaultsToHeat(string? id, string expected)
    {
        Assert.Equal(expected, ThreadMomentum.NormalizeStyle(id));
    }

    [Fact]
    public void TryParsePostedUtc_ReadsTwochAndShitarabaStampsAsJst()
    {
        var a = ThreadMomentum.TryParsePostedUtc("2026/08/16(日) 21:00:00.00 ID:Ab12Xy");
        var b = ThreadMomentum.TryParsePostedUtc("2026/08/16(日) 21:00:00 ID:SNnL3gbc00");
        Assert.NotNull(a);
        Assert.NotNull(b);
        // 21:00 JST = 12:00 UTC
        Assert.Equal(new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc), a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void UpdateFromPosts_ScoresFirstLoadFromPostClocks()
    {
        var now = new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
        var posts = new List<BbsPost>
        {
            PostAt(now.AddMinutes(-20)),
            PostAt(now.AddMinutes(-4)),
            PostAt(now.AddMinutes(-3)),
            PostAt(now.AddMinutes(-2)),
            PostAt(now.AddMinutes(-1)),
            PostAt(now.AddSeconds(-10)),
        };

        var m = new ThreadMomentum();
        m.UpdateFromPosts(posts, now);
        Assert.Equal(5, m.Count);
        Assert.Equal(43, m.Score);
    }

    [Fact]
    public void UpdateFromPosts_QuietThreadStaysZero()
    {
        var now = new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
        var posts = new[]
        {
            PostAt(now.AddMinutes(-30)),
            PostAt(now.AddMinutes(-10)),
        };

        var m = new ThreadMomentum();
        m.UpdateFromPosts(posts, now);
        Assert.Equal(0, m.Count);
        Assert.Equal(0, m.Score);
        Assert.Equal("勢い 0　過疎", ThreadMomentum.FormatDisplay(m.Score));
    }

    [Fact]
    public void UpdateFromPosts_DoesNotRampJustBecauseWeKeepWatching()
    {
        var now = new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
        var posts = new List<BbsPost>
        {
            PostAt(now.AddMinutes(-2)),
            PostAt(now.AddMinutes(-1)),
        };

        var m = new ThreadMomentum();
        m.UpdateFromPosts(posts, now);
        var first = m.Score;

        // Same two posts, 30s later — still two in the window
        m.UpdateFromPosts(posts, now.AddSeconds(30));
        Assert.Equal(first, m.Score);
        Assert.Equal(2, m.Count);
    }

    [Fact]
    public void Recalculate_DropsPostsAfterWindow()
    {
        var now = new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
        var m = new ThreadMomentum();
        m.UpdateFromPosts(new[] { PostAt(now.AddMinutes(-4)) }, now);
        Assert.Equal(1, m.Count);

        m.Recalculate(now + TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        Assert.Equal(0, m.Count);
        Assert.Equal(0, m.Score);
    }

    [Fact]
    public void Clear_ResetsScore()
    {
        var now = DateTime.UtcNow;
        var m = new ThreadMomentum();
        m.UpdateFromPosts(new[] { PostAt(now.AddSeconds(-5)) }, now);
        Assert.True(m.Score > 0);
        m.Clear();
        Assert.Equal(0, m.Count);
        Assert.Equal(0, m.Score);
    }

    private static BbsPost PostAt(DateTime utc)
    {
        var jst = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time"));
        var dateId = jst.ToString("yyyy/MM/dd") + "(日) " + jst.ToString("HH:mm:ss") + " ID:Ab12Xy";
        return new BbsPost { Number = 1, DateId = dateId };
    }
}
