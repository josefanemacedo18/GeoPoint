using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Sheets;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>
/// Coloca a imagem de satélite no espaço de localização de uma prancha (recortada com o enquadramento escolhido, preenchendo o
/// espaço) e, se pedido, o contorno do lote por cima. Usado ao gerar a prancha e ao trocar a imagem depois.
/// </summary>
public static class SheetImagePlacer
{
    private const double Ft = 304.8;
    private static XYZ P(double xMm, double yMm) => new(xMm / Ft, yMm / Ft, 0);

    /// <summary>Coloca e devolve os UniqueIds criados (imagem e linhas do contorno). Dentro de transação.</summary>
    public static List<string> Place(Document doc, ViewSheet sheet, PaperRect r, string imageId, byte[] jpeg, SheetImageChoice choice,
        IReadOnlyList<List<Vec2>>? outline, List<string> warnings)
    {
        var ids = new List<string>();
        try
        {
            string path = ImageCodec.CroppedFile(jpeg, imageId, r.Width / r.Height, choice);
            if (PlaceImage(doc, sheet, r, path) is { } inst) ids.Add(inst.UniqueId);
        }
        catch (Exception ex)
        {
            Logger.Error("Imagem de satélite da prancha", ex);
            warnings.Add("Não foi possível colocar a imagem de satélite: " + ex.Message);
        }
        if (choice.ShowOutline && outline is { Count: > 0 })
        {
            var style = RevitStyles.LineStyle(doc, "GeoPoint - Contorno na imagem", new Color(230, 30, 30), 4);
            foreach (var ring in LocationImages.OutlineInRect(outline, r, choice))
                for (int i = 0; i < ring.Count; i++)
                {
                    var a = ring[i]; var b = ring[(i + 1) % ring.Count];
                    if (a.DistanceTo(b) < 0.3) continue;
                    try
                    {
                        var dc = doc.Create.NewDetailCurve(sheet, Line.CreateBound(P(a.X, a.Y), P(b.X, b.Y)));
                        if (style is not null) dc.LineStyle = style;
                        ids.Add(dc.UniqueId);
                    }
                    catch (Exception ex) { Logger.Error("Contorno na imagem", ex); }
                }
        }
        return ids;
    }

    /// <summary>Apaga a imagem e o contorno colocados antes (pelos UniqueIds). Dentro de transação.</summary>
    public static void Clear(Document doc, IEnumerable<string> uniqueIds)
    {
        var del = uniqueIds.Select(u => ElementUtil.ByUniqueId(doc, u)).Where(e => e is not null).Select(e => e!.Id).ToList();
        if (del.Count == 0) return;
        try { doc.Delete(del); } catch (Exception ex) { Logger.Error("Apagar imagem anterior", ex); }
    }

    /// <summary>Insere a imagem (arquivo) ajustada ao retângulo (mm), centrada. Dentro de transação.</summary>
    public static ImageInstance? PlaceImage(Document doc, ViewSheet sheet, PaperRect r, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        var type = new FilteredElementCollector(doc).OfClass(typeof(ImageType)).Cast<ImageType>()
            .FirstOrDefault(t => { try { return string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase); } catch { return false; } })
            ?? ImageType.Create(doc, new ImageTypeOptions(path, false, ImageTypeSource.Import));
        var inst = ImageInstance.Create(doc, sheet, type.Id, new ImagePlacementOptions(P(r.Center.X, r.Center.Y), BoxPlacement.Center));
        double w = inst.Width, h = inst.Height;
        if (w <= 0 || h <= 0) return inst;
        double k = Math.Min(r.Width / Ft / w, r.Height / Ft / h);
        inst.Width = w * k;
        if (inst.Height > r.Height / Ft * 1.001) inst.Height = r.Height / Ft;
        // Recentraliza após redimensionar.
        var bb = inst.get_BoundingBox(sheet);
        if (bb is not null)
        {
            var cur = (bb.Min + bb.Max) / 2;
            ElementTransformUtils.MoveElement(doc, inst.Id, new XYZ(r.Center.X / Ft - cur.X, r.Center.Y / Ft - cur.Y, 0));
        }
        return inst;
    }
}
