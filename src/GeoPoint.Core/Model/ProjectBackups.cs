using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GeoPoint.Core.Settings;

namespace GeoPoint.Core.Model;

/// <summary>Cópia de segurança dos dados do GeoPoint (arquivo, data, motivo e quantidade de lotes).</summary>
public sealed record BackupEntry(string Path, DateTime When, string Reason, int Lots);

/// <summary>
/// Cópias de segurança dos dados do GeoPoint numa pasta: compactadas (.json.gz), as mais recentes ficam (<see cref="Keep"/>)
/// e uma cópia igual à última não é gravada de novo. Lê também as cópias antigas (.json) das versões até 1.5.2.
/// </summary>
public static class ProjectBackups
{
    public const int Keep = 40;

    /// <summary>Grava a cópia; devolve o arquivo ou null quando os dados são iguais aos da última cópia.</summary>
    public static string? Save(string dir, ParcelProject p, string reason, DateTime? now = null, int keep = Keep)
    {
        Directory.CreateDirectory(dir);
        string json = JsonSerializer.Serialize(p, JsonOptions.Default);
        var last = Files(dir).FirstOrDefault();
        if (last is not null)
        {
            try { if (ReadText(last) == json) return null; } catch { /* cópia ilegível: grava uma nova */ }
        }
        string safe = new string((reason ?? "").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
        string stamp = $"{now ?? DateTime.Now:yyyyMMdd-HHmmss}";
        string file = Path.Combine(dir, $"{stamp} - {safe}.json.gz");
        for (int i = 2; File.Exists(file); i++) file = Path.Combine(dir, $"{stamp}-{i} - {safe}.json.gz");
        using (var fs = File.Create(file))
        using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
        {
            var bytes = new UTF8Encoding(false).GetBytes(json);
            gz.Write(bytes, 0, bytes.Length);
        }
        foreach (var old in Files(dir).Skip(Math.Max(1, keep))) { try { File.Delete(old); } catch { } }
        return file;
    }

    /// <summary>Cópias da pasta, da mais recente para a mais antiga.</summary>
    public static List<BackupEntry> List(string dir)
    {
        var res = new List<BackupEntry>();
        foreach (var f in Files(dir))
        {
            string name = Path.GetFileName(f);
            name = name.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase) ? name[..^8] : Path.GetFileNameWithoutExtension(name);
            var parts = name.Split(" - ", 2);
            DateTime when = File.GetLastWriteTime(f);
            string stamp = parts[0].Length >= 15 ? parts[0][..15] : parts[0];
            if (DateTime.TryParseExact(stamp, "yyyyMMdd-HHmmss", null, System.Globalization.DateTimeStyles.None, out var d)) when = d;
            int lots = 0;
            try { lots = Load(f).Lots.Count; } catch { }
            res.Add(new BackupEntry(f, when, parts.Length > 1 ? parts[1] : "", lots));
        }
        return res;
    }

    public static ParcelProject Load(string path) =>
        JsonSerializer.Deserialize<ParcelProject>(ReadText(path), JsonOptions.Default) ?? throw new InvalidOperationException("Cópia inválida.");

    private static string ReadText(string path)
    {
        if (!path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) return File.ReadAllText(path);
        using var fs = File.OpenRead(path);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var r = new StreamReader(gz, Encoding.UTF8);
        return r.ReadToEnd();
    }

    /// <summary>Arquivos de cópia (.json.gz e .json antigos), do mais recente para o mais antigo (o nome começa pela data).</summary>
    private static IEnumerable<string> Files(string dir) =>
        !Directory.Exists(dir) ? Enumerable.Empty<string>() :
        Directory.GetFiles(dir).Where(f => f.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal);
}
