using DSPlayer.Services;
using Xunit;

namespace DSPlayer.Tests;

public sealed class VoiceDirectiveComposerTests
{
    [Fact]
    public void AddsCanonicalDirectiveOnlyWhenVoiceSelected()
    {
        Assert.Equal("こんにちは #マオマオ", VoiceDirectiveComposer.Compose("こんにちは", "マオマオ"));
        Assert.Equal("こんにちは", VoiceDirectiveComposer.Compose("こんにちは", ""));
        Assert.Equal("こんにちは", VoiceDirectiveComposer.Compose("こんにちは", "通常音声"));
        Assert.Equal("こんにちは #リヴァイ兵長", VoiceDirectiveComposer.Compose("こんにちは", "リヴァイ兵長"));
        Assert.Equal("", VoiceDirectiveComposer.Compose("  ", "マオマオ"));
        Assert.Equal("#マオマオ こんにちは", VoiceDirectiveComposer.Compose("#マオマオ こんにちは", "マオマオ"));
        Assert.Equal("こんにちは #マオマオ", VoiceDirectiveComposer.Compose("こんにちは #マオマオ", "マオマオ"));
    }
}
