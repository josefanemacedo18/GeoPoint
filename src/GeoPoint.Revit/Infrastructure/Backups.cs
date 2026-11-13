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
    public const int Keep = ProjectBackups.Keep;

    public static string Folder(Document doc)
    {
        string name = string.IsNullOrWhiteSpace(doc.PathName) ? doc.Title : System.IO.Path.GetFileNameWithoutExtension(doc.PathName);
        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return System.IO.Path.Combine(SettingsStore.DefaultFolder, "copias", name.Length == 0 ? "projeto" : name);
    }

    /// <summary>Grava a cópia compactada (silencioso: uma falha aqui nunca impede o comando; cópia igual à última não é repetida).</summary>
    public static void Save(GpSession s, string reason)
    {
        try
        {
            if (s.Project.Lots.Count == 0 && s.Project.Fases.Count <= 1) return;
            ProjectBackups.Save(Folder(s.Doc), s.Project, reason);
        }
        catch (Exception ex) { Logger.Error("Cópia de segurança", ex); }
    }

    public static List<BackupEntry> List(Document doc) => ProjectBackups.List(Folder(doc));

    public static ParcelProject Load(string path) => ProjectBackups.Load(path);

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
        // Configurações (a chave do Google fica no cofre do Windows e não entra no arquivo).
        Add("configuracoes.json", SettingsStore.Serialize(s.Settings));
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
