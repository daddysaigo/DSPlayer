namespace DSPlayer.Services;

public static class VoiceDirectiveComposer
{
    public static string Compose(string body, string? canonicalVoice)
    {
        var text = (body ?? string.Empty).Trim();
        var voice = (canonicalVoice ?? string.Empty).Trim();
        if (string.Equals(voice, "通常音声", StringComparison.Ordinal))
            voice = string.Empty;
        if (string.IsNullOrEmpty(voice) || text.Length == 0 ||
            text.StartsWith("#", StringComparison.Ordinal) || text.StartsWith("＃", StringComparison.Ordinal) ||
            System.Text.RegularExpressions.Regex.IsMatch(text, @"(?:^|\s)[#＃][^\s#＃]+$"))
            return text;
        return $"{text} #{voice}";
    }
}

public sealed record VoiceDirectiveOption(string Label, string Canonical);
