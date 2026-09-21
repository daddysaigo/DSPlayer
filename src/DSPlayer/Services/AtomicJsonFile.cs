using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DSPlayer.Services;

/// <summary>Small configuration files: serialize writers across processes and replace atomically.</summary>
internal static class AtomicJsonFile
{
    public static JsonObject Read(string path) => TryRead(path) ?? TryRead(path + ".bak") ?? new JsonObject();

    private static JsonObject? TryRead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return JsonNode.Parse(stream) as JsonObject;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static void Update(string path, Action<JsonObject> update)
    {
        path = Path.GetFullPath(path);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
        using var mutex = new Mutex(false, "Local\\DSPlayer.Config." + hash);
        var acquired = false;
        string? temp = null;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("設定ファイルの保存待ちがタイムアウトしました。");

            var document = Read(path);
            var previous = document.DeepClone();
            update(document);
            if (JsonNode.DeepEquals(document, previous) && TryRead(path) is not null) return;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, document, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path))
                File.Replace(temp, path, TryRead(path) is null ? null : path + ".bak");
            else
                File.Move(temp, path);
            temp = null;
        }
        finally
        {
            if (temp is not null) File.Delete(temp);
            if (acquired) mutex.ReleaseMutex();
        }
    }
}
