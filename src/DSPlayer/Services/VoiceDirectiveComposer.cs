namespace DSPlayer.Services;

public static class VoiceDirectiveComposer
{
    public static string Compose(string body, string? canonicalVoice)
    {
        var text = (body ?? string.Empty).Trim();
        var voice = (canonicalVoice ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(voice) || text.Length == 0 ||
            text.StartsWith("#", StringComparison.Ordinal) || text.StartsWith("＃", StringComparison.Ordinal))
            return text;
        return $"#{voice} {text}";
    }
}

public sealed record VoiceDirectiveOption(string Label, string Canonical);
