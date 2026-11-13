using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using GeoPoint.Core.Model;
using GeoPoint.Core.Sheets;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>Registro das pranchas geradas, com impressão digital do conteúdo para saber se o usuário as editou.</summary>
public static class SheetRegistry
{
    /// <summary>Prancha gerada antes para a mesma chave (a mais recente que ainda existe).</summary>
    public static GeneratedSheet? Find(GpSession s, string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        s.Project.Sheets.RemoveAll(g => ElementUtil.ByUniqueId(s.Doc, g.SheetId) is not ViewSheet);
        return s.Project.Sheets.Where(g => g.Key == key).OrderByDescending(g => g.Created).FirstOrDefault();
    }

    /// <summary>A prancha foi alterada depois de gerada (textos movidos, linhas, cotas, escala, recorte...)?</summary>
    public static bool IsEdited(GpSession s, GeneratedSheet g)
    {
        try { return Fingerprint(s.Doc, g) != g.Fingerprint; }
        catch { return true; }
    }

    public static GeneratedSheet Register(GpSession s, SheetSpec spec, ViewSheet sheet, View view)
    {
        var g = new GeneratedSheet
        {
            Key = spec.Key, SheetId = sheet.UniqueId, ViewIds = { view.UniqueId }, Title = spec.Title, FaseId = s.Project.Fase.Id,
        };
        g.Fingerprint = Fingerprint(s.Doc, g);
        s.Project.Sheets.Add(g);
        return g;
    }

    /// <summary>Apaga a prancha e as vistas copiadas dela. Dentro de transação.</summary>
    public static void Delete(GpSession s, GeneratedSheet g)
    {
        foreach (var uid in g.ViewIds.Append(g.SheetId))
        {
            var e = ElementUtil.ByUniqueId(s.Doc, uid);
            if (e is null) continue;
            try { s.Doc.Delete(e.Id); } catch (Exception ex) { Logger.Error("Apagar prancha anterior", ex); }
        }
        s.Project.Sheets.Remove(g);
    }

    private static string R(XYZ p) => $"{Math.Round(p.X, 4)};{Math.Round(p.Y, 4)}";

    private static string Fingerprint(Document doc, GeneratedSheet g)
    {
        var parts = new List<string>();
        foreach (var uid in g.ViewIds.Prepend(g.SheetId))
        {
            if (ElementUtil.ByUniqueId(doc, uid) is not View v) { parts.Add("x" + uid); continue; }
            parts.Add($"V|{v.UniqueId}|{v.Scale}");
            try { var cb = v.CropBox; parts.Add("C|" + R(cb.Min) + "|" + R(cb.Max)); } catch { }
            foreach (var e in new FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType())
            {
                if (e.OwnerViewId != v.Id) continue;
                string d;
                try
                {
                    d = e switch
                    {
                        TextNote t => $"T|{t.Text}|{R(t.Coord)}|{t.GetTypeId().Value}",
                        CurveElement c => $"L|{R(c.GeometryCurve.GetEndPoint(0))}|{R(c.GeometryCurve.GetEndPoint(1))}|{c.LineStyle?.Id.Value}",
                        Viewport vp => $"P|{R(vp.GetBoxCenter())}|{vp.ViewId.Value}|{vp.GetTypeId().Value}",
                        _ => e.get_BoundingBox(v) is { } bb ? $"B|{e.Category?.Id.Value}|{R(bb.Min)}|{R(bb.Max)}" : $"E|{e.Category?.Id.Value}",
                    };
                }
                catch { d = "?"; }
                parts.Add(e.UniqueId + "|" + d);
            }
        }
        parts.Sort(StringComparer.Ordinal);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", parts)));
        return Convert.ToHexString(hash);
    }
}
