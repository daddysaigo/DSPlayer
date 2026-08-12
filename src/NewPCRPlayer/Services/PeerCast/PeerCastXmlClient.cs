using System.Globalization;
using System.Net.Http;
using System.Xml.Linq;

namespace NewPCRPlayer.Services.PeerCast;

/// <summary>
/// PeerCast classic admin XML API: GET /admin?cmd=viewxml
/// (PeerCastStation JSON-RPC /api/1 POST is restricted in some builds; viewxml works.)
/// </summary>
public sealed class PeerCastXmlClient
{
    private readonly HttpClient _http;

    public PeerCastXmlClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task<PeerCastChannelInfo?> GetChannelAsync(
        string channelId,
        string? baseUrl = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            return null;

        var bases = new List<string>();
        if (!string.IsNullOrWhiteSpace(baseUrl))
            bases.Add(baseUrl.TrimEnd('/'));
        bases.Add("http://127.0.0.1:7144");
        bases.Add("http://localhost:7144");

        foreach (var b in bases.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var xmlUrl = b + "/admin?cmd=viewxml";
                using var resp = await _http.GetAsync(xmlUrl, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    continue;

                var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var info = ParseChannel(text, channelId);
                if (info is not null)
                    return info;
            }
            catch
            {
                // try next base
            }
        }

        return null;
    }

    public static PeerCastChannelInfo? ParseChannel(string xml, string channelId)
    {
        if (string.IsNullOrWhiteSpace(xml) || string.IsNullOrWhiteSpace(channelId))
            return null;

        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { return null; }

        var idTarget = channelId.Trim();
        var channels = doc.Descendants("channel");
        foreach (var ch in channels)
        {
            var id = (string?)ch.Attribute("id") ?? "";
            if (!id.Equals(idTarget, StringComparison.OrdinalIgnoreCase))
                continue;

            var hits = ch.Element("hits");
            var relay = ch.Element("relay");

            var listeners = AttrInt(hits, "listeners");
            if (listeners == 0)
                listeners = AttrInt(relay, "listeners");

            return new PeerCastChannelInfo
            {
                ChannelId = id,
                Name = Attr(ch, "name"),
                Type = Attr(ch, "type"),
                Genre = Attr(ch, "genre"),
                Desc = Attr(ch, "desc"),
                Comment = Attr(ch, "comment"),
                ContactUrl = Attr(ch, "url"),
                BitrateKbps = AttrInt(ch, "bitrate"),
                Listeners = listeners,
                Relays = AttrInt(hits, "relays") is var r && r > 0 ? r : AttrInt(relay, "relays"),
                UptimeSeconds = AttrInt(ch, "uptime"),
                Status = Attr(relay, "status"),
            };
        }

        return null;
    }

    private static string Attr(XElement? el, string name) =>
        ((string?)el?.Attribute(name))?.Trim() ?? "";

    private static int AttrInt(XElement? el, string name)
    {
        var s = Attr(el, name);
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}
