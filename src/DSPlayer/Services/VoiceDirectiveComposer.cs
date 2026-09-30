namespace DSPlayer.Services;

public static class VoiceDirectiveComposer
{
    public static string Compose(string body, string? canonicalVoice)
    {
        var text = (body ?? string.Empty).Trim();
        var voice = (canonicalVoice ?? string.Empty).Trim();
        if (string.Equals(voice, "通常音声", StringComparison.Ordinal))
            voice = string.Empty;
        // The editable selector accepts a canonical name, with or without the
        // marker. Normalize it here so pasted input can never produce ##name.
        while (voice.StartsWith("#", StringComparison.Ordinal) ||
               voice.StartsWith("＃", StringComparison.Ordinal))
            voice = voice[1..].TrimStart();
        if (string.IsNullOrEmpty(voice) || text.Length == 0 ||
            text.StartsWith("#", StringComparison.Ordinal) || text.StartsWith("＃", StringComparison.Ordinal))
            return text;
        return $"#{voice} {text}";
    }
}

public sealed record VoiceDirectiveOption(string Label, string Canonical);
