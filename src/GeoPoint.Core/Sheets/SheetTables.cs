using GeoPoint.Core.Calc;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Settings;

namespace GeoPoint.Core.Sheets;

/// <summary>Tabela desenhada na prancha (coordenadas dos vértices ou quadro de lotes), com medidas no papel (mm).</summary>
public sealed class SheetTable
{
    public List<string> TitleLines { get; } = new();
    public string[] Header { get; init; } = Array.Empty<string>();
    public List<string[]> Rows { get; } = new();
    public HashSet<int> BoldRows { get; } = new();
    public double TextMm { get; set; }
    public double TitleMm { get; set; }
    public double[] ColWidths { get; set; } = Array.Empty<double>();
    public double RowH => TextMm * 2.3;
    public double TitleLineH => TitleMm * 1.7;
    public double Width => ColWidths.Sum();
    public double Height => TitleLines.Count * TitleLineH + 1.2 + RowH * (Rows.Count + 1);

    /// <summary>Calcula as larguras das colunas pelo texto mais largo de cada uma.</summary>
    public SheetTable Measure()
    {
        int n = Header.Length;
        var w = new double[n];
        for (int c = 0; c < n; c++)
        {
            double m = AnnotationPlanner.TextWidth(Header[c], TextMm, true);
            foreach (var r in Rows) if (c < r.Length) m = Math.Max(m, AnnotationPlanner.TextWidth(r[c], TextMm));
            w[c] = m + 2.4;
        }
        double titleW = TitleLines.Select((t, i) => AnnotationPlanner.TextWidth(t, TitleMm, i == 0)).DefaultIfEmpty(0).Max() + 3;
        double sum = w.Sum();
        if (titleW > sum) for (int c = 0; c < n; c++) w[c] += (titleW - sum) / n;
        ColWidths = w;
        return this;
    }

    /// <summary>Reduz o texto (até <paramref name="minMm"/>) para caber no espaço; devolve false se não couber.</summary>
    public bool FitInto(double maxW, double maxH, double minMm = 1.3)
    {
        Measure();
        while ((Width > maxW || Height > maxH) && TextMm > minMm + 1e-9)
        {
            TextMm = Math.Round(TextMm - 0.1, 2);
            TitleMm = Math.Max(minMm, Math.Round(TitleMm - 0.1, 2));
            Measure();
        }
        return Width <= maxW + 1e-9 && Height <= maxH + 1e-9;
    }

    /// <summary>Tabela de coordenadas dos vértices (modelo da folha: vértice, azimute, distância e as duas coordenadas).</summary>
    public static SheetTable Coordinates(LotReport rep, ProjectContext ctx, ValueFormatter fmt, CrsDef def, SheetTextStyles st)
    {
        var (ta, tb) = fmt.ColumnTitles(def);
        bool geo = def.Kind == CrsKind.Geographic;
        var t = new SheetTable
        {
            Header = new[] { "Vértice", fmt.F.AngleStyle == AngleStyle.Rumo ? "Rumo" : "Azimute", "Dist. (m)", geo ? "Latitude" : ta, geo ? "Longitude" : tb },
            TextMm = st.TabelaMm, TitleMm = st.TabelaMm,
        };
        t.TitleLines.Add("TABELA DE COORDENADAS DOS VÉRTICES" + (rep.Lot.DisplayName.Length > 0 ? " – " + rep.Lot.DisplayName.ToUpperInvariant() : ""));
        if (ctx.Coords.HasGeoreference)
        {
            t.TitleLines.Add("SISTEMA GEODÉSICO DE REFERÊNCIA: " + Datum.Get(def.Datum).Name);
            var z = ctx.Coords.ResolveZone(def);
            string proj = def.Kind switch
            {
                CrsKind.Utm => $"PROJEÇÃO UTM – FUSO {z.Number}{(z.South ? "S" : "N")}",
                CrsKind.Geographic => "COORDENADAS GEOGRÁFICAS",
                CrsKind.LocalTopographic => "PLANO TOPOGRÁFICO LOCAL",
                CrsKind.CustomTm => "PROJEÇÃO " + def.CustomName,
                _ => "",
            };
            string epsg = SheetTexts.Epsg(def, z);
            if (proj.Length > 0) t.TitleLines.Add(proj + (epsg.Length > 0 ? " – " + epsg : ""));
        }
        else t.TitleLines.Add("COORDENADAS LOCAIS DO PROJETO (SEM GEORREFERÊNCIA)");
        for (int i = 0; i < rep.Vertices.Count; i++)
        {
            var v = rep.Vertices[i];
            var s = rep.Segments[i];
            var c = ctx.Coords.ToCrs(v.Internal, ctx.Coords.HasGeoreference ? def : CrsDef.ProjectLocalDef());
            var (a, b) = c is { } cc ? fmt.Columns(cc, ctx.Coords.HasGeoreference ? def : CrsDef.ProjectLocalDef()) : ("–", "–");
            string dist = s.IsArc ? $"{fmt.DistanceValue(s.LengthHorizontal)} (R={fmt.DistanceValue(s.Radius ?? 0)})" : fmt.DistanceValue(s.LengthHorizontal);
            t.Rows.Add(new[] { v.Name, fmt.Angle(s.AzimuthTrue), dist, a, b });
        }
        return t.Measure();
    }

    /// <summary>Quadro de lotes (pranchas com vários lotes).</summary>
    public static SheetTable Lots(IReadOnlyList<LotRecord> lots, ProjectContext ctx, ValueFormatter fmt, SheetTextStyles st)
    {
        var t = new SheetTable { Header = new[] { "Lote", "Área (m²)", "Perímetro (m)", "Proprietário" }, TextMm = st.TabelaMm, TitleMm = st.TabelaMm };
        t.TitleLines.Add("QUADRO DE ÁREAS");
        double total = 0;
        foreach (var l in ProjectContext.SortLots(lots))
        {
            LotReport? r = null;
            try { r = ctx.Report(l); } catch { }
            total += r?.AreaLocal ?? 0;
            string owner = string.IsNullOrWhiteSpace(l.Proprietario) ? ctx.Project.Info.Proprietario : l.Proprietario;
            if (owner.Length > 40) owner = owner[..39] + "…";
            if (l.IsRemanescente) t.BoldRows.Add(t.Rows.Count);
            t.Rows.Add(new[] { l.DisplayName, r is null ? "–" : fmt.Number(r.AreaLocal, fmt.F.AreaDecimals), r is null ? "–" : fmt.DistanceValue(r.PerimeterLocal), owner });
        }
        t.BoldRows.Add(t.Rows.Count);
        t.Rows.Add(new[] { "TOTAL", fmt.Number(total, fmt.F.AreaDecimals), "", "" });
        return t.Measure();
    }
}
