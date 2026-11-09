using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Settings;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>Conversões de unidades e geometria entre o Revit (pés) e o núcleo (metros).</summary>
public static class RvtUnits
{
    public const double FtToM = 0.3048;
    public const double SqFtToSqM = 0.09290304;

    public static Vec2 ToVec(XYZ p) => new(p.X * FtToM, p.Y * FtToM);

    public static XYZ ToXyz(Vec2 v, double zFeet) => new(v.X / FtToM, v.Y / FtToM, zFeet);

    public static double MToFt(double m) => m / FtToM;

    /// <summary>Converte curva do núcleo em curva do Revit no plano Z indicado.</summary>
    public static Curve ToRevitCurve(Curve2 c, double zFeet)
    {
        if (c is ArcCurve arc)
            return Arc.Create(ToXyz(arc.Start, zFeet), ToXyz(arc.End, zFeet), ToXyz(arc.Mid, zFeet));
        return Line.CreateBound(ToXyz(c.Start, zFeet), ToXyz(c.End, zFeet));
    }

    public static CurveLoop ToCurveLoop(Ring ring, double zFeet)
    {
        var loop = new CurveLoop();
        foreach (var e in ring.Edges)
        {
            if (e.Length < 1e-4) continue;
            loop.Append(ToRevitCurve(e, zFeet));
        }
        return loop;
    }
}

/// <summary>Registro simples em %AppData%\GeoPoint\logs.</summary>
public static class Logger
{
    private static readonly object Gate = new();

    public static string Folder => Path.Combine(SettingsStore.DefaultFolder, "logs");

    public static void Info(string msg) => Write("INFO", msg);
    public static void Error(string msg, Exception? ex = null) => Write("ERRO", ex is null ? msg : msg + Environment.NewLine + ex);

    private static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                var file = Path.Combine(Folder, $"geopoint-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss} [{level}] {msg}{Environment.NewLine}");
            }
        }
        catch { /* registro nunca deve interromper o uso */ }
    }
}

public static class ElementUtil
{
    public static Element? ByUniqueId(Document doc, string? uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) return null;
        try { return doc.GetElement(uid); } catch { return null; }
    }

    public static List<ElementId> ExistingIds(Document doc, IEnumerable<string> uids) =>
        uids.Select(u => ByUniqueId(doc, u)).Where(e => e is not null).Select(e => e!.Id).Distinct().ToList();

    /// <summary>Vista adequada para elementos de detalhe (planta, desenho, área, forro).</summary>
    public static bool SupportsDetails(View v) =>
        !v.IsTemplate && v.ViewType is ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.AreaPlan or ViewType.EngineeringPlan
            or ViewType.DraftingView or ViewType.Detail or ViewType.Section or ViewType.Elevation or ViewType.Legend;

    public static bool IsPlanLike(View v) =>
        !v.IsTemplate && v.ViewType is ViewType.FloorPlan or ViewType.CeilingPlan or ViewType.AreaPlan or ViewType.EngineeringPlan or ViewType.DraftingView;

    /// <summary>Elevação (pés) do plano de desenho de uma vista em planta.</summary>
    public static double ViewPlaneZ(View v)
    {
        try
        {
            var sp = v.SketchPlane;
            if (sp is not null) return sp.GetPlane().Origin.Z;
        }
        catch { }
        if (v is ViewPlan vp && vp.GenLevel is Level lv) return lv.Elevation;
        return 0;
    }
}
