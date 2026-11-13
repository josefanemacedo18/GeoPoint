using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Settings;

namespace GeoPoint.Revit.Infrastructure;

/// <summary>Tabela de coordenadas desenhada numa vista de desenho (escala 1:1).</summary>
public static class CoordinateTableBuilder
{
    public static ViewDrafting Build(Document doc, LotRecord lot, LotReport rep, ProjectContext ctx, double textMm)
    {
        var fmt = new ValueFormatter(ctx.Settings.Format);
        var fase = ctx.Project.Fases.Count > 1 ? ctx.Project.FindFase(lot.FaseId) : null;
        string viewName = Ui.SafeFileName($"GeoPoint - Coordenadas - {(fase is null ? "" : $"F{fase.Numero} - ")}{lot.DisplayName}").Replace("{", "(").Replace("}", ")");
        var view = new FilteredElementCollector(doc).OfClass(typeof(ViewDrafting)).Cast<ViewDrafting>().FirstOrDefault(v => v.Name == viewName);
        if (view is null)
        {
            var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(t => t.ViewFamily == ViewFamily.Drafting)
                      ?? throw new InvalidOperationException("Tipo de vista de desenho não encontrado no modelo.");
            view = ViewDrafting.Create(doc, vft.Id);
            try { view.Name = viewName; } catch { }
        }
        else
        {
            var old = new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType()
                .Where(e => e is TextNote || e is DetailCurve).Select(e => e.Id).ToList();
            if (old.Count > 0) doc.Delete(old);
        }
        view.Scale = 1;

        var defs = ctx.Coords.HasGeoreference
            ? ctx.Settings.Memorial.CoordinateSystems.Take(2).ToList()
            : new List<Core.Geodesy.CrsDef> { Core.Geodesy.CrsDef.ProjectLocalDef() };
        if (defs.Count == 0) defs.Add(Core.Geodesy.CrsDef.Utm());

        var header = new List<string> { "Vértice" };
        foreach (var d in defs) { var (a, b) = fmt.ColumnTitles(d); header.Add(a); header.Add(b); }
        header.AddRange(new[] { "Segmento", ctx.Settings.Format.AngleStyle == AngleStyle.Rumo ? "Rumo" : "Azimute", "Distância (m)", "Confrontante" });

        var rows = new List<List<string>>();
        for (int i = 0; i < rep.Vertices.Count; i++)
        {
            var v = rep.Vertices[i];
            var s = rep.Segments[i];
            var row = new List<string> { v.Name };
            foreach (var d in defs)
            {
                var c = ctx.Coords.ToCrs(v.Internal, d);
                if (c is null) { row.Add("–"); row.Add("–"); continue; }
                var (a, b) = fmt.Columns(c.Value, d);
                row.Add(a); row.Add(b);
            }
            row.Add($"{s.From.Name} – {s.To.Name}");
            row.Add(fmt.Angle(s.AzimuthTrue));
            row.Add(s.IsArc ? $"D={fmt.DistanceValue(s.LengthHorizontal)} R={fmt.DistanceValue(s.Radius ?? 0)}" : fmt.DistanceValue(s.LengthHorizontal));
            row.Add(string.IsNullOrWhiteSpace(s.Confrontante) ? "–" : s.Confrontante);
            rows.Add(row);
        }

        // Larguras pelas quantidades de caracteres (aprox. 0,62 × altura por caractere).
        double h = textMm / 304.8; // pés (escala 1:1)
        double charW = h * 0.62, pad = h * 0.8, rowH = h * 2.0;
        int cols = header.Count;
        var widths = new double[cols];
        for (int c = 0; c < cols; c++)
        {
            int len = Math.Max(header[c].Length, rows.Count == 0 ? 0 : rows.Max(r => r[c].Length));
            widths[c] = len * charW + 2 * pad;
        }
        var typeId = RevitStyles.TextType(doc, textMm, ctx.Settings.Annotation.Font);
        var style = RevitStyles.LineStyle(doc, RevitStyles.LineTabela, RevitStyles.Rgb(0, 0, 0), 1);
        double totalW = widths.Sum();
        int nRows = rows.Count + 2;
        double totalH = nRows * rowH;

        void HLine(double y) => DrawLine(doc, view, new XYZ(0, y, 0), new XYZ(totalW, y, 0), style);
        void VLine(double x, double y0, double y1) => DrawLine(doc, view, new XYZ(x, y0, 0), new XYZ(x, y1, 0), style);

        // Título (linha 0), cabeçalho (linha 1), dados.
        for (int r = 0; r <= nRows; r++) HLine(-r * rowH);
        VLine(0, 0, -totalH);
        VLine(totalW, 0, -totalH);
        double x = 0;
        for (int c = 0; c < cols - 1; c++) { x += widths[c]; VLine(x, -rowH, -totalH); }

        void Text(double cx, double cy, string t, HorizontalTextAlignment al)
        {
            // Ponto de inserção = borda superior da caixa: centraliza na linha da tabela.
            var o = new TextNoteOptions(typeId) { HorizontalAlignment = al };
            TextNote.Create(doc, view.Id, new XYZ(cx, cy + RevitStyles.HalfHeightMm(textMm) / 304.8, 0), t, o);
        }
        string title = $"QUADRO DE COORDENADAS – {lot.DisplayName.ToUpperInvariant()}  (Área: {fmt.Area(rep.AreaLocal)} – Perímetro: {fmt.Distance(rep.PerimeterLocal)})";
        Text(totalW / 2, -rowH / 2, title, HorizontalTextAlignment.Center);
        x = 0;
        for (int c = 0; c < cols; c++)
        {
            Text(x + widths[c] / 2, -1.5 * rowH, header[c], HorizontalTextAlignment.Center);
            for (int r = 0; r < rows.Count; r++)
            {
                bool left = c == cols - 1;
                Text(left ? x + pad : x + widths[c] / 2, -(r + 2.5) * rowH, rows[r][c], left ? HorizontalTextAlignment.Left : HorizontalTextAlignment.Center);
            }
            x += widths[c];
        }
        return view;
    }

    private static void DrawLine(Document doc, View view, XYZ a, XYZ b, GraphicsStyle? style)
    {
        if (a.DistanceTo(b) < 1e-6) return;
        var dc = doc.Create.NewDetailCurve(view, Line.CreateBound(a, b));
        if (style is not null) { try { dc.LineStyle = style; } catch { } }
    }
}

/// <summary>Elementos nativos do Revit (Property Line, Região preenchida) e parâmetros compartilhados.</summary>
public static class NativeElements
{
    public sealed record ParamDef(string Name, Guid Guid, ForgeTypeId Spec, string Description);

    public static readonly ParamDef[] Params =
    {
        new("GP_Lote", new Guid("3F2B6E1A-7C44-4D3B-9E21-0A1B2C3D4E01"), SpecTypeId.String.Text, "Nome do lote (GeoPoint)"),
        new("GP_Quadra", new Guid("3F2B6E1A-7C44-4D3B-9E21-0A1B2C3D4E02"), SpecTypeId.String.Text, "Quadra (GeoPoint)"),
        new("GP_Uso", new Guid("3F2B6E1A-7C44-4D3B-9E21-0A1B2C3D4E03"), SpecTypeId.String.Text, "Uso da área (GeoPoint)"),
        new("GP_Matricula", new Guid("3F2B6E1A-7C44-4D3B-9E21-0A1B2C3D4E04"), SpecTypeId.String.Text, "Matrícula (GeoPoint)"),
        new("GP_Proprietario", new Guid("3F2B6E1A-7C44-4D3B-9E21-0A1B2C3D4E05"), SpecTypeId.String.Text, "Proprietário (GeoPoint)"),
        new("GP_Area", new Guid("3F2B6E1A-7C44-4D3B-9E21-0A1B2C3D4E06"), SpecTypeId.Area, "Área calculada pelo GeoPoint"),
        new("GP_Perimetro", new Guid("3F2B6E1A-7C44-4D3B-9E21-0A1B2C3D4E07"), SpecTypeId.Length, "Perímetro calculado pelo GeoPoint"),
        new("GP_Testada", new Guid("3F2B6E1A-7C44-4D3B-9E21-0A1B2C3D4E08"), SpecTypeId.Length, "Testada calculada pelo GeoPoint"),
        new("GP_AreaLiquida", new Guid("3F2B6E1A-7C44-4D3B-9E21-0A1B2C3D4E09"), SpecTypeId.Area, "Área líquida (GeoPoint)"),
        new("GP_Id", new Guid("3F2B6E1A-7C44-4D3B-9E21-0A1B2C3D4E0A"), SpecTypeId.String.Text, "Identificador interno do GeoPoint"),
    };

    /// <summary>Garante os parâmetros compartilhados nas categorias indicadas. Dentro de transação.</summary>
    public static List<string> EnsureParameters(Document doc, IEnumerable<BuiltInCategory> categories)
    {
        var warnings = new List<string>();
        var app = doc.Application;
        var cats = app.Create.NewCategorySet();
        foreach (var bic in categories)
        {
            var c = Category.GetCategory(doc, bic);
            if (c is not null && c.AllowsBoundParameters) cats.Insert(c);
            else warnings.Add($"A categoria {bic} não aceita parâmetros de projeto.");
        }
        if (cats.IsEmpty) return warnings;

        string original = app.SharedParametersFilename;
        string path = Path.Combine(SettingsStore.DefaultFolder, "GeoPoint_ParametrosCompartilhados.txt");
        try
        {
            Directory.CreateDirectory(SettingsStore.DefaultFolder);
            if (!File.Exists(path)) File.WriteAllText(path, "");
            app.SharedParametersFilename = path;
            var file = app.OpenSharedParameterFile() ?? throw new InvalidOperationException("Não foi possível abrir o arquivo de parâmetros compartilhados do GeoPoint.");
            var group = file.Groups.get_Item("GeoPoint") ?? file.Groups.Create("GeoPoint");
            foreach (var p in Params)
            {
                var def = group.Definitions.get_Item(p.Name) as ExternalDefinition;
                if (def is null)
                {
                    var opt = new ExternalDefinitionCreationOptions(p.Name, p.Spec) { GUID = p.Guid, Description = p.Description, UserModifiable = false, Visible = true };
                    def = (ExternalDefinition)group.Definitions.Create(opt);
                }
                var map = doc.ParameterBindings;
                var binding = app.Create.NewInstanceBinding(cats);
                if (map.Contains(def)) map.ReInsert(def, binding, GroupTypeId.Data);
                else if (!map.Insert(def, binding, GroupTypeId.Data))
                    warnings.Add($"Não foi possível vincular o parâmetro {p.Name}.");
            }
        }
        finally
        {
            try { app.SharedParametersFilename = original; } catch { }
        }
        return warnings;
    }

    public static void SetParameters(Element e, LotRecord lot, LotReport rep)
    {
        void S(string name, string value) { try { var p = e.get_Parameter(Params.First(x => x.Name == name).Guid); if (p is not null && !p.IsReadOnly) p.Set(value ?? ""); } catch { } }
        void D(string name, double internalValue) { try { var p = e.get_Parameter(Params.First(x => x.Name == name).Guid); if (p is not null && !p.IsReadOnly) p.Set(internalValue); } catch { } }
        S("GP_Lote", lot.Nome);
        S("GP_Quadra", lot.Quadra);
        S("GP_Uso", LotUseInfo.Label(lot.Uso));
        S("GP_Matricula", lot.Matricula);
        S("GP_Proprietario", lot.Proprietario);
        S("GP_Id", lot.Id);
        D("GP_Area", rep.AreaLocal / RvtUnits.SqFtToSqM);
        D("GP_Perimetro", rep.PerimeterLocal / RvtUnits.FtToM);
        D("GP_Testada", rep.Testada / RvtUnits.FtToM);
        D("GP_AreaLiquida", rep.AreaLiquida / RvtUnits.SqFtToSqM);
    }

    /// <summary>Cria/atualiza a linha de propriedade nativa do lote. Devolve a área calculada pelo Revit (m²) ou null.</summary>
    public static double? UpsertPropertyLine(Document doc, LotRecord lot, Region region, out Element? element)
    {
        var loops = new List<CurveLoop> { RvtUnits.ToCurveLoop(region.Outer, lot.ZFeet) };
        foreach (var h in region.Holes) loops.Add(RvtUnits.ToCurveLoop(h, lot.ZFeet));
        PropertyLine? pl = ElementUtil.ByUniqueId(doc, lot.PropertyLineId) as PropertyLine;
        if (pl is not null && pl.IsSketchBased())
        {
            pl.SetBoundary(loops);
        }
        else
        {
            if (pl is not null) doc.Delete(pl.Id);
            pl = PropertyLine.Create(doc, loops);
        }
        element = pl;
        lot.PropertyLineId = pl.UniqueId;
        doc.Regenerate();
        double a = pl.Area;
        return a > 0 ? a * RvtUnits.SqFtToSqM : null;
    }

    /// <summary>Cria região preenchida (cor por uso) na vista. Devolve a área calculada pelo Revit (m²) ou null.</summary>
    public static double? CreateFilledRegion(Document doc, View view, LotRecord lot, Region region, out Element element)
    {
        // Remove regiões anteriores do lote nesta vista.
        var old = lot.FilledRegionIds.Select(u => ElementUtil.ByUniqueId(doc, u)).OfType<FilledRegion>().Where(f => f.OwnerViewId == view.Id).ToList();
        foreach (var f in old) { lot.FilledRegionIds.Remove(f.UniqueId); doc.Delete(f.Id); }
        double z = ElementUtil.ViewPlaneZ(view);
        var loops = new List<CurveLoop> { RvtUnits.ToCurveLoop(region.Outer, z) };
        foreach (var h in region.Holes) loops.Add(RvtUnits.ToCurveLoop(h, z));
        var typeId = RevitStyles.FillType(doc, lot.Uso);
        var fr = FilledRegion.Create(doc, typeId, view.Id, loops);
        element = fr;
        lot.FilledRegionIds.Add(fr.UniqueId);
        doc.Regenerate();
        var p = fr.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
        return p is not null && p.HasValue ? p.AsDouble() * RvtUnits.SqFtToSqM : null;
    }
}

