using System.Net.Http;
using System.Text.Json;

namespace DSPlayer.Services.PeerCast;

/// <summary>
/// Minimal PeerCastStation HTTP API helper.
/// Contact URL is usually already passed by PeCaRecorder; this is a fallback.
/// </summary>
public sealed class PeerCastApiClient
{
    private readonly HttpClient _http;

    public PeerCastApiClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    /// <summary>
    /// Tries common local PeerCastStation endpoints to resolve a channel's contact URL.
    /// </summary>
    public async Task<string?> TryGetContactUrlAsync(string channelId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            return null;

        // PeerCastStation HTML channel page often exposes info; JSON APIs vary by version.
        // Try channel info page scrape as last resort is heavy — prefer args from PeCaRecorder.
        var bases = new[]
        {
            "http://127.0.0.1:7144",
            "http://localhost:7144",
        };

        foreach (var b in bases)
        {
            try
            {
                // Some builds: /api/channels/{id}
                var url = $"{b}/api/channels/{channelId}";
                using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    continue;

                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
                if (TryReadContact(doc.RootElement, out var contact))
                    return contact;
            }
            catch
            {
                // try next
            }
        }

        return null;
    }

    private static bool TryReadContact(JsonElement root, out string? contact)
    {
        contact = null;
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var name in new[] { "contactUrl", "ContactUrl", "url", "info" })
        {
            if (!root.TryGetProperty(name, out var p))
                continue;

            if (p.ValueKind == JsonValueKind.String)
            {
                contact = p.GetString();
                if (!string.IsNullOrWhiteSpace(contact))
                    return true;
            }

            if (p.ValueKind == JsonValueKind.Object)
            {
                foreach (var nested in new[] { "url", "contactUrl", "ContactUrl" })
                {
                    if (p.TryGetProperty(nested, out var n) && n.ValueKind == JsonValueKind.String)
                    {
                        contact = n.GetString();
                        if (!string.IsNullOrWhiteSpace(contact))
                            return true;
                    }
                }
            }
        }

        return false;
    }
}
