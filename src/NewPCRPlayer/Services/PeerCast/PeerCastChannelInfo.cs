namespace NewPCRPlayer.Services.PeerCast;

public sealed class PeerCastChannelInfo
{
    public string ChannelId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string Genre { get; init; } = "";
    public string Desc { get; init; } = "";
    public string Comment { get; init; } = "";
    public string ContactUrl { get; init; } = "";
    public int BitrateKbps { get; init; }
    public int Listeners { get; init; }
    public int Relays { get; init; }
    public int UptimeSeconds { get; init; }
    public string Status { get; init; } = "";
}
