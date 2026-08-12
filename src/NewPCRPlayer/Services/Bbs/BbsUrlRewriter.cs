using System.Text.RegularExpressions;

namespace NewPCRPlayer.Services.Bbs;

/// <summary>
/// PCRPlayer.xml &lt;board&gt;&lt;url&gt; rewrite rules (enabled defaults).
/// </summary>
public static class BbsUrlRewriter
{
    private static readonly (Regex Pattern, string Replacement)[] Rules =
    {
        // したらば旧ドメイン
        (new Regex(@"jbbs\.livedoor\.jp/", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "jbbs.shitaraba.net/"),
        // Lite 版アドレス
        (new Regex(@"jbbs\.shitaraba\.net/bbs/lite/", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "jbbs.shitaraba.net/bbs/"),
        // subject.cgi → 板トップ
        (new Regex(@"jbbs\.shitaraba\.net/bbs/subject\.cgi/", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "jbbs.shitaraba.net/"),
    };

    public static string Rewrite(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url ?? "";

        var s = url.Trim();
        foreach (var (pattern, replacement) in Rules)
            s = pattern.Replace(s, replacement);
        return s;
    }
}
