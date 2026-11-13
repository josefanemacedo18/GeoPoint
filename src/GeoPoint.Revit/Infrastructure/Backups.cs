using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;
using GeoPoint.Core.Model;
using GeoPoint.Core.Settings;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>Cópias de segurança dos dados do GeoPoint (lotes, fases, operações) antes das operações que alteram o projeto.</summary>
public static class Backups
{
    public const int Keep = 40;

    public sealed record Entry(string Path, DateTime When, string Reason, int Lots);

    public static string Folder(Document doc)
    {
        string name = string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : System.IO.Path.GetFileNameWithoutExtension(doc.PathName);
        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return System.IO.Path.Combine(SettingsStore.DefaultFolder, "copias", name.Length == 0 ? "projeto" : name);
    }

    /// <summary>Grava a cópia (silencioso: uma falha aqui nunca impede o comando).</summary>
    public static void Save(GpSession s, string reason)
    {
        try
        {
            if (s.Project.Lots.Count == 0 && s.Project.Fases.Count <= 1) return;
            var dir = Folder(s.Doc);
            Directory.CreateDirectory(dir);
            string safe = new string(reason.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            string file = System.IO.Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss} - {safe}.json");
            File.WriteAllText(file, JsonSerializer.Serialize(s.Project, JsonOptions.Default));
            foreach (var old in Directory.GetFiles(dir, "*.json").OrderByDescending(f => f).Skip(Keep)) File.Delete(old);
        }
        catch (Exception ex) { Logger.Error("Cópia de segurança", ex); }
    }

    public static List<Entry> List(Document doc)
    {
        var res = new List<Entry>();
        var dir = Folder(doc);
        if (!Directory.Exists(dir)) return res;
        foreach (var f in Directory.GetFiles(dir, "*.json").OrderByDescending(x => x))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(f);
            var parts = name.Split(" - ", 2);
            DateTime when = File.GetLastWriteTime(f);
            if (parts.Length > 0 && DateTime.TryParseExact(parts[0], "yyyyMMdd-HHmmss", null, System.Globalization.DateTimeStyles.None, out var d)) when = d;
            int lots = 0;
            try { lots = Load(f).Lots.Count; } catch { }
            res.Add(new Entry(f, when, parts.Length > 1 ? parts[1] : "", lots));
        }
        return res;
    }

    public static ParcelProject Load(string path) =>
        JsonSerializer.Deserialize<ParcelProject>(File.ReadAllText(path), JsonOptions.Default) ?? throw new InvalidOperationException("Cópia inválida.");

    /// <summary>Arquivo .zip com registros, configurações (sem chaves) e dados do projeto, para enviar ao suporte.</summary>
    public static string Diagnostic(GpSession s)
    {
        string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desk) || !Directory.Exists(desk)) desk = SettingsStore.DefaultFolder;
        string zipPath = System.IO.Path.Combine(desk, $"GeoPoint-diagnostico-{DateTime.Now:yyyyMMdd-HHmm}.zip");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        void Add(string name, string content)
        {
            var e = zip.CreateEntry(name);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(content);
        }
        var sb = new StringBuilder();
        sb.AppendLine($"GeoPoint {typeof(Backups).Assembly.GetName().Version}");
        sb.AppendLine($"Revit {s.UiApp.Application.VersionNumber} {s.UiApp.Application.VersionBuild} ({s.UiApp.Application.Language})");
        sb.AppendLine($"Windows {Environment.OSVersion}  .NET {Environment.Version}");
        sb.AppendLine($"Documento: {s.Doc.Title}  workshared: {s.Doc.IsWorkshared}");
        sb.AppendLine($"Vista ativa: {s.ActiveView.Name} ({s.ActiveView.ViewType}, 1:{s.ActiveView.Scale})");
        sb.AppendLine($"Fases: {s.Project.Fases.Count}  ativa: {s.Project.Fase.Label}");
        sb.AppendLine($"Lotes: {s.Project.Lots.Count} (ativos na fase: {s.Project.ActiveLots.Count()})  Operações: {s.Project.Operations.Count}  Pranchas: {s.Project.Sheets.Count}  Edificações: {s.Project.Buildings.Count}");
        sb.AppendLine($"Referência: {(s.Project.Reference.IsSet ? $"{s.Project.Reference.Lat:0.######}, {s.Project.Reference.Lon:0.######} ({s.Project.Reference.Mode})" : "não definida")}");
        Add("sistema.txt", sb.ToString());
        // Configurações sem a chave do Google.
        var st = s.Settings.Clone();
        st.Imagery.GoogleKey = st.Imagery.GoogleKey.Length > 0 ? "(omitida)" : "";
        Add("configuracoes.json", JsonSerializer.Serialize(st, JsonOptions.Default));
        Add("projeto-geopoint.json", JsonSerializer.Serialize(s.Project, JsonOptions.Default));
        try
        {
            if (Directory.Exists(Logger.Folder))
                foreach (var f in Directory.GetFiles(Logger.Folder).OrderByDescending(File.GetLastWriteTime).Take(5))
                    zip.CreateEntryFromFile(f, "registros/" + System.IO.Path.GetFileName(f));
        }
        catch (Exception ex) { Add("registros/erro.txt", ex.ToString()); }
        return zipPath;
    }
}
