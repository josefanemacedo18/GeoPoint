using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using GeoPoint.Core.Model;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>Estilos de linha, tipos de texto e tipos de região criados pelo GeoPoint (prefixo "GeoPoint").</summary>
public static class RevitStyles
{
    public const string LineDivisaProposta = "GeoPoint - Divisa proposta";
    public const string LineAreaEdificavel = "GeoPoint - Área edificável";
    public const string LineCaminhamento = "GeoPoint - Caminhamento";
    public const string LineTabela = "GeoPoint - Tabela";

    /// <summary>Estilo de linha (subcategoria de Linhas). Deve ser chamado dentro de transação.</summary>
    public static GraphicsStyle? LineStyle(Document doc, string name, Color color, int weight)
    {
        try
        {
            var lines = Category.GetCategory(doc, BuiltInCategory.OST_Lines);
            if (lines is null) return null;
            Category? sub = null;
            foreach (Category c in lines.SubCategories)
                if (c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { sub = c; break; }
            if (sub is null)
            {
                sub = doc.Settings.Categories.NewSubcategory(lines, name);
                sub.LineColor = color;
                sub.SetLineWeight(weight, GraphicsStyleType.Projection);
            }
            return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
        }
        catch (Exception ex)
        {
            Logger.Error("Falha ao criar estilo de linha " + name, ex);
            return null;
        }
    }

    /// <summary>Tipo de texto com tamanho (mm) e fonte. Deve ser chamado dentro de transação.</summary>
    public static ElementId TextType(Document doc, double sizeMm, string font)
    {
        string name = $"GeoPoint {sizeMm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} mm {font}";
        var existing = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault(t => t.Name == name);
        if (existing is not null) return existing.Id;
        var defId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
        var baseType = (defId != ElementId.InvalidElementId ? doc.GetElement(defId) as TextNoteType : null)
                       ?? new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().First();
        var nt = (TextNoteType)baseType.Duplicate(name);
        nt.get_Parameter(BuiltInParameter.TEXT_SIZE)?.Set(sizeMm / 304.8);
        try { nt.get_Parameter(BuiltInParameter.TEXT_FONT)?.Set(font); } catch { }
        try { nt.get_Parameter(BuiltInParameter.TEXT_BACKGROUND)?.Set(1); } catch { } // transparente
        return nt.Id;
    }

    private static ElementId SolidPattern(Document doc)
    {
        foreach (var fp in new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>())
        {
            try
            {
                var p = fp.GetFillPattern();
                if (p.IsSolidFill && p.Target == FillPatternTarget.Drafting) return fp.Id;
            }
            catch { }
        }
        return ElementId.InvalidElementId;
    }

    /// <summary>Tipo de região preenchida com cor sólida para o uso. Deve ser chamado dentro de transação.</summary>
    public static ElementId FillType(Document doc, LotUse uso, string? customName = null, (byte R, byte G, byte B)? color = null)
    {
        string name = customName ?? "GeoPoint - " + LotUseInfo.Label(uso);
        var all = new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().ToList();
        var ex = all.FirstOrDefault(t => t.Name == name);
        if (ex is not null) return ex.Id;
        var baseType = all.FirstOrDefault() ?? throw new InvalidOperationException("O modelo não possui tipos de região preenchida.");
        var nt = (FilledRegionType)baseType.Duplicate(name);
        var solid = SolidPattern(doc);
        var c = color ?? LotUseInfo.DefaultColor(uso);
        if (solid != ElementId.InvalidElementId)
        {
            nt.ForegroundPatternId = solid;
            nt.ForegroundPatternColor = new Color(c.R, c.G, c.B);
            nt.BackgroundPatternId = ElementId.InvalidElementId;
        }
        return nt.Id;
    }

    public static Color Rgb(byte r, byte g, byte b) => new(r, g, b);
}

/// <summary>Criação de linhas no mesmo plano/vista das linhas de origem de um lote.</summary>
public sealed class CurveTarget
{
    public View? DetailView { get; init; }
    public SketchPlane? Sketch { get; init; }
    public double ZFeet { get; init; }

    /// <summary>Define onde desenhar novas linhas de um lote (deve ser chamado dentro de transação se precisar criar plano).</summary>
    public static CurveTarget ForLot(Document doc, View active, LotRecord lot)
    {
        if (lot.SourceKind == "DetailLine" && ElementUtil.ByUniqueId(doc, lot.SourceViewId) is View v && ElementUtil.SupportsDetails(v))
            return new CurveTarget { DetailView = v, ZFeet = lot.ZFeet };
        if (lot.SourceKind == "Cad" && ElementUtil.IsPlanLike(active))
            return new CurveTarget { DetailView = active, ZFeet = ElementUtil.ViewPlaneZ(active) };
        if (ElementUtil.ByUniqueId(doc, lot.SketchPlaneId) is SketchPlane sp && IsHorizontal(sp))
            return new CurveTarget { Sketch = sp, ZFeet = sp.GetPlane().Origin.Z };
        return new CurveTarget { Sketch = HorizontalPlane(doc, lot.ZFeet), ZFeet = lot.ZFeet };
    }

    public static CurveTarget ForActiveView(Document doc, View active, bool detail, double? zFeet = null)
    {
        if (detail)
        {
            if (!ElementUtil.SupportsDetails(active)) throw new InvalidOperationException("A vista atual não aceita linhas de detalhe. Abra uma vista de planta.");
            return new CurveTarget { DetailView = active, ZFeet = zFeet ?? ElementUtil.ViewPlaneZ(active) };
        }
        double z = zFeet ?? ElementUtil.ViewPlaneZ(active);
        SketchPlane? sp = null;
        try { sp = active.SketchPlane; } catch { }
        if (sp is not null && IsHorizontal(sp) && Math.Abs(sp.GetPlane().Origin.Z - z) < 1e-6) return new CurveTarget { Sketch = sp, ZFeet = z };
        return new CurveTarget { Sketch = HorizontalPlane(doc, z), ZFeet = z };
    }

    private static bool IsHorizontal(SketchPlane sp)
    {
        try { return Math.Abs(Math.Abs(sp.GetPlane().Normal.Z) - 1) < 1e-9; } catch { return false; }
    }

    private static SketchPlane HorizontalPlane(Document doc, double z) =>
        SketchPlane.Create(doc, Autodesk.Revit.DB.Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, z)));

    /// <summary>Cria as linhas e devolve os elementos criados. Deve ser chamado dentro de transação.</summary>
    public List<CurveElement> Create(Document doc, IEnumerable<Core.Geometry.Curve2> curves, GraphicsStyle? style)
    {
        var res = new List<CurveElement>();
        foreach (var c in curves)
        {
            if (c.Length < 0.001) continue;
            var rc = RvtUnits.ToRevitCurve(c, ZFeet);
            CurveElement ce = DetailView is not null
                ? doc.Create.NewDetailCurve(DetailView, rc)
                : doc.Create.NewModelCurve(rc, Sketch!);
            if (style is not null)
            {
                try { ce.LineStyle = style; } catch { }
            }
            res.Add(ce);
        }
        return res;
    }
}
