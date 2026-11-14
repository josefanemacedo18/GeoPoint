using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Export;
using GeoPoint.Core.Model;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>Google Earth: cor de cada lote igual à da hachura do projeto e um arquivo por lote.</summary>
public static class EarthExport
{
    /// <summary>
    /// Cor da hachura de cada lote no projeto (lida dos elementos de hachura criados pelo GeoPoint, a mais recente). Lote sem hachura:
    /// a cor que o comando Hachuras daria a ele (mesma paleta, na ordem dos lotes da fase).
    /// </summary>
    public static Dictionary<string, (byte R, byte G, byte B)> HatchColors(GpSession s, IEnumerable<LotRecord> lots)
    {
        var res = new Dictionary<string, (byte, byte, byte)>();
        var order = ProjectContext.SortLots(s.Project.PhaseLots).ToList();
        foreach (var lot in lots)
        {
            (byte, byte, byte)? c = null;
            for (int i = lot.Hatches.Count - 1; i >= 0 && c is null; i--)
                foreach (var uid in lot.Hatches[i].ElementIds)
                {
                    if (ElementUtil.ByUniqueId(s.Doc, uid) is not FilledRegion fr) continue;
                    try
                    {
                        if (s.Doc.GetElement(fr.GetTypeId()) is FilledRegionType t && t.ForegroundPatternColor is { IsValid: true } col)
                        {
                            c = (col.Red, col.Green, col.Blue);
                            break;
                        }
                    }
                    catch { }
                }
            int idx = order.FindIndex(l => l.Id == lot.Id);
            res[lot.Id] = c ?? LotPalette.Of(idx < 0 ? res.Count : idx);
        }
        return res;
    }

    /// <summary>Grava um KMZ (ou KML) para cada lote na pasta; devolve os arquivos gravados.</summary>
    public static List<string> WritePerLot(GpSession s, ProjectContext ctx, IReadOnlyList<LotRecord> lots, string folder, string prefix, bool kmz)
    {
        Directory.CreateDirectory(folder);
        var colors = HatchColors(s, lots);
        var files = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lot in ProjectContext.SortLots(lots))
        {
            string kml = GisExport.BuildKml(ctx, new[] { lot }, $"{prefix} – {lot.DisplayName}", colors);
            string name = GisExport.SafeFileName($"{prefix} - {lot.DisplayName}");
            string path = Path.Combine(folder, name + (kmz ? ".kmz" : ".kml"));
            for (int k = 2; !used.Add(path); k++) path = Path.Combine(folder, $"{name} ({k}){(kmz ? ".kmz" : ".kml")}");
            byte[] data = kmz ? GisExport.BuildKmz(kml) : new System.Text.UTF8Encoding(false).GetBytes(kml);
            if (Ui.TryWrite(path, data)) files.Add(path);
        }
        return files;
    }

    /// <summary>
    /// Abre os arquivos no Google Earth, um por vez: o primeiro abre o programa; os outros esperam ele terminar de abrir (senão o
    /// Windows pode abrir uma segunda janela). Roda em segundo plano, sem prender o Revit.
    /// </summary>
    public static void OpenInEarth(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return;
        var list = files.ToList();
        Task.Run(() =>
        {
            try
            {
                bool running = EarthRunning();
                Ui.OpenFile(list[0]);
                if (list.Count == 1) return;
                if (!running)
                    for (int i = 0; i < 60 && !EarthRunning(); i++) Thread.Sleep(500);
                Thread.Sleep(running ? 500 : 2500);
                foreach (var f in list.Skip(1))
                {
                    Ui.OpenFile(f);
                    Thread.Sleep(700);
                }
            }
            catch (Exception ex) { Logger.Error("Abrir no Google Earth", ex); }
        });
    }

    private static bool EarthRunning()
    {
        try
        {
            return Process.GetProcessesByName("googleearth").Concat(Process.GetProcessesByName("GoogleEarthPro"))
                .Any(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch { return false; } });
        }
        catch { return false; }
    }
}
