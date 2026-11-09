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
    public static GraphicsStyle? LineStyle(Document doc, string name, Color color, int weight, ElementId? pattern = null)
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
                if (pattern is not null && pattern != ElementId.InvalidElementId) sub.SetLinePatternId(pattern, GraphicsStyleType.Projection);
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
    /// <summary>Margem (mm no papel) entre o texto e a borda da caixa nos tipos de texto do GeoPoint.</summary>
    public const double TextPadMm = 0.4;

    /// <summary>Altura de uma linha de texto em relação ao tamanho do texto.</summary>
    public const double LineFactor = 1.5;

    /// <summary>Meia altura (mm no papel) da caixa de um texto do GeoPoint.</summary>
    public static double HalfHeightMm(double sizeMm, int lines = 1) => (sizeMm * LineFactor * lines) / 2 + TextPadMm;

    /// <summary>Tipo de texto com tamanho (mm) e fonte. Deve ser chamado dentro de transação.</summary>
    public static ElementId TextType(Document doc, double sizeMm, string font, bool opaque = false, bool bold = false, Color? color = null)
    {
        string name = $"GeoPoint {sizeMm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} mm {font}"
                      + (opaque ? " opaco" : "") + (bold ? " negrito" : "") + (color is null ? "" : $" #{color.Red:X2}{color.Green:X2}{color.Blue:X2}");
        var nt = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault(t => t.Name == name);
        if (nt is null)
        {
            var defId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            var baseType = (defId != ElementId.InvalidElementId ? doc.GetElement(defId) as TextNoteType : null)
                           ?? new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().First();
            nt = (TextNoteType)baseType.Duplicate(name);
        }
        // Os parâmetros são reaplicados sempre (tipos criados por versões anteriores ficam iguais).
        void SetD(BuiltInParameter bip, double v) { try { var p = nt.get_Parameter(bip); if (p is not null && !p.IsReadOnly && Math.Abs(p.AsDouble() - v) > 1e-9) p.Set(v); } catch { } }
        void SetI(BuiltInParameter bip, int v) { try { var p = nt.get_Parameter(bip); if (p is not null && !p.IsReadOnly && p.AsInteger() != v) p.Set(v); } catch { } }
        SetD(BuiltInParameter.TEXT_SIZE, sizeMm / 304.8);
        try { var pf = nt.get_Parameter(BuiltInParameter.TEXT_FONT); if (pf is not null && pf.AsString() != font) pf.Set(font); } catch { }
        SetI(BuiltInParameter.TEXT_BACKGROUND, opaque ? 0 : 1); // 0 = opaco, 1 = transparente
        SetI(BuiltInParameter.TEXT_STYLE_BOLD, bold ? 1 : 0);
        SetI(BuiltInParameter.TEXT_STYLE_ITALIC, 0);
        SetI(BuiltInParameter.TEXT_STYLE_UNDERLINE, 0);
        SetI(BuiltInParameter.TEXT_BOX_VISIBILITY, 0);
        SetD(BuiltInParameter.TEXT_WIDTH_SCALE, 1.0);
        SetD(BuiltInParameter.LEADER_OFFSET_SHEET, TextPadMm / 304.8);
        SetI(BuiltInParameter.LINE_COLOR, color is null ? 0 : color.Red + color.Green * 256 + color.Blue * 65536);
        return nt.Id;
    }

    /// <summary>Padrão de linha tracejado (comprimentos no papel, mm). Dentro de transação.</summary>
    public static ElementId DashPattern(Document doc, string name, double dashMm, double gapMm)
    {
        var ex = new FilteredElementCollector(doc).OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>().FirstOrDefault(e => e.Name == name);
        if (ex is not null) return ex.Id;
        try
        {
            var lp = new LinePattern(name);
            lp.SetSegments(new List<LinePatternSegment>
            {
                new(LinePatternSegmentType.Dash, dashMm / 304.8),
                new(LinePatternSegmentType.Space, gapMm / 304.8),
            });
            return LinePatternElement.Create(doc, lp).Id;
        }
        catch (Exception e)
        {
            Logger.Error("Falha ao criar padrão de linha " + name, e);
            return ElementId.InvalidElementId;
        }
    }

    /// <summary>Estilo de linha "&lt;Linhas invisíveis&gt;" (independe do idioma).</summary>
    public static ElementId InvisibleLines(Document doc)
    {
        try
        {
            var lines = Category.GetCategory(doc, BuiltInCategory.OST_Lines);
            foreach (Category c in lines.SubCategories)
                if (c.Id.Value == (long)BuiltInCategory.OST_InvisibleLines) return c.GetGraphicsStyle(GraphicsStyleType.Projection)?.Id ?? ElementId.InvalidElementId;
        }
        catch { }
        return ElementId.InvalidElementId;
    }

    /// <summary>Padrão de hachura de desenho (espaçamento no papel, mm). Dentro de transação.</summary>
    public static ElementId HatchPattern(Document doc, string name, double angleDeg, double spacingMm, bool cross)
    {
        var ex = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>().FirstOrDefault(e => e.Name == name);
        if (ex is not null) return ex.Id;
        var fp = cross
            ? new FillPattern(name, FillPatternTarget.Drafting, FillPatternHostOrientation.ToView, angleDeg * Math.PI / 180, spacingMm / 304.8, spacingMm / 304.8)
            : new FillPattern(name, FillPatternTarget.Drafting, FillPatternHostOrientation.ToView, angleDeg * Math.PI / 180, spacingMm / 304.8);
        return FillPatternElement.Create(doc, fp).Id;
    }

    /// <summary>Tipo de região com hachura colorida e fundo claro. Dentro de transação.</summary>
    public static ElementId HatchType(Document doc, string name, ElementId hatch, Color color, bool lightBackground)
    {
        var all = new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().ToList();
        var ex = all.FirstOrDefault(t => t.Name == name);
        if (ex is not null) return ex.Id;
        var baseType = all.FirstOrDefault() ?? throw new InvalidOperationException("O modelo não possui tipos de região preenchida.");
        var nt = (FilledRegionType)baseType.Duplicate(name);
        nt.ForegroundPatternId = hatch;
        nt.ForegroundPatternColor = color;
        var solid = SolidPattern(doc);
        if (lightBackground && solid != ElementId.InvalidElementId)
        {
            nt.BackgroundPatternId = solid;
            nt.BackgroundPatternColor = new Color((byte)(255 - (255 - color.Red) * 0.12), (byte)(255 - (255 - color.Green) * 0.12), (byte)(255 - (255 - color.Blue) * 0.12));
        }
        else nt.BackgroundPatternId = ElementId.InvalidElementId;
        try { nt.IsMasking = false; } catch { }
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
