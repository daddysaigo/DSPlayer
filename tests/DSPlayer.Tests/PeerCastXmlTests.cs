using DSPlayer.Services.PeerCast;
using Xunit;

namespace DSPlayer.Tests;

public class PeerCastXmlTests
{
    private const string SampleXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <peercast session="ABC">
          <channels_relayed total="1">
            <channel id="B7A89A7DF32FD5230A8E7BBE1E66829C" name="TestCh" bitrate="2128"
                     comment="hello" desc="desc1" genre="Game" type="FLV"
                     url="https://example.com/bbs/" uptime="100">
              <hits hosts="3" listeners="15" relays="12" />
              <relay listeners="1" relays="1" status="Relay" />
              <track title="" album="" genre="" artist="" contact="" />
            </channel>
          </channels_relayed>
        </peercast>
        """;

    [Fact]
    public void ParseChannel_ReadsFields()
    {
        var info = PeerCastXmlClient.ParseChannel(SampleXml, "B7A89A7DF32FD5230A8E7BBE1E66829C");
        Assert.NotNull(info);
        Assert.Equal("TestCh", info!.Name);
        Assert.Equal("FLV", info.Type);
        Assert.Equal("Game", info.Genre);
        Assert.Equal("desc1", info.Desc);
        Assert.Equal("hello", info.Comment);
        Assert.Equal(2128, info.BitrateKbps);
        Assert.Equal(15, info.Listeners);
        Assert.Equal("https://example.com/bbs/", info.ContactUrl);
    }

    [Fact]
    public void ParseChannel_UnknownId_ReturnsNull()
    {
        var info = PeerCastXmlClient.ParseChannel(SampleXml, "00000000000000000000000000000000");
        Assert.Null(info);
    }
}
