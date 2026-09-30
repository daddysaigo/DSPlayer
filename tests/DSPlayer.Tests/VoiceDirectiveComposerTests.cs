using DSPlayer.Services;
using Xunit;

namespace DSPlayer.Tests;

public sealed class VoiceDirectiveComposerTests
{
    [Fact]
    public void AddsCanonicalDirectiveOnlyWhenVoiceSelected()
    {
        Assert.Equal("#マオマオ こんにちは", VoiceDirectiveComposer.Compose("こんにちは", "マオマオ"));
        Assert.Equal("こんにちは", VoiceDirectiveComposer.Compose("こんにちは", ""));
        Assert.Equal("こんにちは", VoiceDirectiveComposer.Compose("こんにちは", "通常音声"));
        Assert.Equal("#新キャラ こんにちは", VoiceDirectiveComposer.Compose("こんにちは", "新キャラ"));
        Assert.Equal("#新キャラ こんにちは", VoiceDirectiveComposer.Compose("こんにちは", "#新キャラ"));
        Assert.Equal("#新キャラ こんにちは", VoiceDirectiveComposer.Compose("こんにちは", "＃新キャラ"));
        Assert.Equal("", VoiceDirectiveComposer.Compose("  ", "マオマオ"));
        Assert.Equal("#マオマオ こんにちは", VoiceDirectiveComposer.Compose("#マオマオ こんにちは", "マオマオ"));
    }
}
