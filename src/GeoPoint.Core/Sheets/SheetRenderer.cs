using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;

namespace GeoPoint.Core.Sheets;

/// <summary>Estilos de linha. <see cref="Grade"/> fica ATRÁS do desenho (no Revit vai para a vista, antes das hachuras).</summary>
public enum PLineStyle { Moldura, Bloco, Fina, Grade, Tracejada, Vermelha, MeioFio, Limite, Papel }
public enum PTextH { Left, Center, Right }
public enum PTextV { Top, Middle, Bottom }

/// <summary>Elemento gráfico da prancha, em mm no papel (origem no canto inferior esquerdo).</summary>
public abstract record SheetPrim;
public sealed record PLine(Vec2 A, Vec2 B, PLineStyle Style) : SheetPrim;
public sealed record PFill(IReadOnlyList<Vec2> Pts, byte R, byte G, byte B, bool Hatch = false, bool Cross = false) : SheetPrim;
public sealed record PText(Vec2 At, string Text, double SizeMm, bool Bold = false, PTextH H = PTextH.Left, PTextV V = PTextV.Top, double Rotation = 0, byte R = 0, byte G = 0, byte B = 0) : SheetPrim
{
    public int LineCount => Text.Split('\n').Length;
}
public sealed record PImage(PaperRect Rect, string Key) : SheetPrim;
public sealed record PCircle(Vec2 C, double Radius, bool Filled, byte R = 0, byte G = 0, byte B = 0) : SheetPrim;

/// <summary>Dados para desenhar uma prancha.</summary>
public sealed class SheetRenderInput
{
    public SheetTemplate Template { get; init; } = SheetTemplate.Default();
    public SheetContent Content { get; init; } = new();
    public int Scale { get; init; } = 500;
    public string Folha { get; init; } = "01";
    public string SheetNumber { get; init; } = "";
    public DateTime Date { get; init; } = DateTime.Today;
    /// <summary>Coordenada da grade para um ponto do papel (null = sem grade).</summary>
    public Func<Vec2, (double A, double B)?>? CrsAt { get; init; }
    public bool Geographic { get; init; }
    public bool WestAsO { get; init; } = true;
    public SheetTable? Table { get; init; }
    public PaperRect? TableRect { get; init; }
    /// <summary>Direção do norte no papel (vetor unitário).</summary>
    public Vec2 North { get; init; } = new(0, 1);
    /// <summary>false = carimbo próprio do escritório (família de carimbo): só o conteúdo do mapa.</summary>
    public bool FrameAndBlocks { get; init; } = true;
    /// <summary>Cores das hachuras por índice (convenções).</summary>
    public IReadOnlyList<(byte R, byte G, byte B)> Palette { get; init; } = new[] { ((byte)220, (byte)60, (byte)50) };
    /// <summary>Imagens disponíveis ("croqui", "logo").</summary>
    public ISet<string> Images { get; init; } = new HashSet<string>();
}

/// <summary>Monta os elementos gráficos da prancha (moldura, blocos, grade, tabela, norte, escala, convenções).</summary>
public static class SheetRenderer
{
    public static PaperRect NorthRect(SheetTemplate t)
    {
        var m = t.MapRect();
        double s = t.NorthSizeMm;
        return MapFit.CornerRect(m, t.NorthCorner, s * 0.75, s * 1.15, 2.5);
    }

    /// <summary>Faixas em volta do mapa (obstáculos para as anotações não saírem do mapa).</summary>
    public static List<PaperRect> OutsideMap(PaperRect m, double t = 80) => new()
    {
        new PaperRect(m.X0 - t, m.Y0 - t, m.X1 + t, m.Y0 + 0.5),
        new PaperRect(m.X0 - t, m.Y1 - 0.5, m.X1 + t, m.Y1 + t),
        new PaperRect(m.X0 - t, m.Y0, m.X0 + 0.5, m.Y1),
        new PaperRect(m.X1 - 0.5, m.Y0, m.X1 + t, m.Y1),
    };

    public static List<SheetPrim> Render(SheetRenderInput i)
    {
        var t = i.Template;
        var st = t.Styles;
        var res = new List<SheetPrim>();
        var (pw, ph) = t.PaperSize();
        var map = t.MapRect();
        var north = NorthRect(t);

        if (i.FrameAndBlocks)
        {
            Rect(res, new PaperRect(0, 0, pw, ph), PLineStyle.Fina, paper: true);
            Rect(res, t.Frame(), PLineStyle.Moldura);
        }

        // ---- Grade de coordenadas (atrás do desenho), recortada pela tabela e pelo norte.
        if (t.Grid && i.CrsAt is not null)
        {
            var holes = new List<PaperRect> { north.Inset(-1) };
            if (i.TableRect is PaperRect tr) holes.Add(tr);
            foreach (var g in MapGrid.Build(map, i.CrsAt, i.Geographic, t.GridSpacingMm, i.WestAsO))
            {
                foreach (var (a, b) in Clip(g.P0, g.P1, holes)) res.Add(new PLine(a, b, PLineStyle.Grade));
                foreach (var lb in g.Labels)
                {
                    // Valores perto dos cantos se atropelam: ficam de fora.
                    bool nearCorner = lb.Side is MapSide.Inferior or MapSide.Superior
                        ? lb.At.X < map.X0 + 8 || lb.At.X > map.X1 - 8
                        : lb.At.Y < map.Y0 + 10 || lb.At.Y > map.Y1 - 10;
                    if (nearCorner) continue;
                    double gap = 0.8;
                    res.Add(lb.Side switch
                    {
                        MapSide.Inferior => new PText(lb.At - new Vec2(0, gap), lb.Text, st.GradeMm, H: PTextH.Center, V: PTextV.Top),
                        MapSide.Superior => new PText(lb.At + new Vec2(0, gap), lb.Text, st.GradeMm, H: PTextH.Center, V: PTextV.Bottom),
                        // Laterais: texto girado 90° (lido de baixo para cima), como no modelo.
                        MapSide.Esquerda => new PText(lb.At - new Vec2(gap, 0), lb.Text, st.GradeMm, H: PTextH.Center, V: PTextV.Bottom, Rotation: Math.PI / 2),
                        _ => new PText(lb.At + new Vec2(gap, 0), lb.Text, st.GradeMm, H: PTextH.Center, V: PTextV.Top, Rotation: Math.PI / 2),
                    });
                }
            }
        }
        if (i.FrameAndBlocks) Rect(res, map, PLineStyle.Bloco);

        // ---- Norte.
        NorthArrow(res, north, i.North, st);

        // ---- Tabela dentro do mapa (fundo branco).
        if (i.Table is { } tab && i.TableRect is PaperRect trc) Table(res, tab, trc);

        if (!i.FrameAndBlocks) return res;

        // ---- Blocos do carimbo.
        foreach (var b in t.Blocks.Where(b => b.Visible && b.W > 2 && b.H > 2))
        {
            var r = b.Rect;
            Rect(res, r, PLineStyle.Bloco);
            var body = r;
            if (b.Title.Length > 0 && b.Kind != SheetBlockKind.Titulo)
            {
                // Cabeçalho: reduz até 1,3 mm para caber; se ainda não couber, quebra em duas linhas.
                double hs = st.CabecalhoMm;
                while (hs > 1.3 && AnnotationPlanner.TextWidth(b.Title, hs, true) > r.Width - 2) hs = Math.Round(hs - 0.1, 2);
                var hl = AnnotationPlanner.TextWidth(b.Title, hs, true) > r.Width - 2 ? Wrap(b.Title, r.Width - 2, hs, true) : new List<string> { b.Title };
                double hh = hs * 1.55 * hl.Count + hs * 0.6;
                res.Add(new PText(new Vec2(r.Center.X, r.Y1 - hh / 2), string.Join("\n", hl), hs, true, PTextH.Center, PTextV.Middle));
                res.Add(new PLine(new Vec2(r.X0, r.Y1 - hh), new Vec2(r.X1, r.Y1 - hh), PLineStyle.Fina));
                body = new PaperRect(r.X0, r.Y0, r.X1, r.Y1 - hh);
            }
            switch (b.Kind)
            {
                case SheetBlockKind.Identificacao: Fields(res, body, i.Content.Identificacao, st); break;
                case SheetBlockKind.Registro: Fields(res, body, i.Content.Registro, st); break;
                case SheetBlockKind.QuadroAreas: Quadro(res, body, i.Content.Quadro, st); break;
                case SheetBlockKind.Assinaturas: Signatures(res, body, i.Content.Assinaturas, st); break;
                case SheetBlockKind.Convencoes: Legend(res, body, i.Content.Legenda, st, i.Palette); break;
                case SheetBlockKind.Orientacao: Orientation(res, body, i, st); break;
                case SheetBlockKind.Croqui:
                    var ir = body.Inset(1.2);
                    if (i.Images.Contains("croqui")) res.Add(new PImage(ir, "croqui"));
                    else res.Add(new PText(ir.Center, "(croqui de localização)", st.RotuloMm, H: PTextH.Center, V: PTextV.Middle, R: 120, G: 120, B: 120));
                    break;
                case SheetBlockKind.Titulo: Title(res, r, i, st); break;
                case SheetBlockKind.Escritorio:
                {
                    var inner = body.Inset(1.2);
                    bool logo = i.Images.Contains("logo");
                    if (logo) res.Add(new PImage(new PaperRect(inner.X0, inner.Y0, inner.X0 + Math.Min(inner.Width * 0.4, inner.Height * 1.6), inner.Y1), "logo"));
                    string name = string.IsNullOrWhiteSpace(t.OfficeName) ? b.Text : t.OfficeName + (b.Text.Length > 0 ? "\n" + b.Text : "");
                    double x0 = logo ? inner.X0 + Math.Min(inner.Width * 0.4, inner.Height * 1.6) + 1.5 : inner.X0;
                    if (name.Length > 0) FitText(res, new PaperRect(x0, inner.Y0, inner.X1, inner.Y1), name, st.ValorMm, true, PTextH.Left);
                    break;
                }
                case SheetBlockKind.Observacoes:
                    if (b.Text.Length > 0) FitText(res, body.Inset(1.2), b.Text, st.ValorMm, false, PTextH.Left);
                    break;
            }
        }
        return res;
    }

    // ------------------------------------------------------------------ partes

    private static void Rect(List<SheetPrim> res, PaperRect r, PLineStyle s, bool paper = false)
    {
        var st = paper ? PLineStyle.Papel : s;
        res.Add(new PLine(new Vec2(r.X0, r.Y0), new Vec2(r.X1, r.Y0), st));
        res.Add(new PLine(new Vec2(r.X1, r.Y0), new Vec2(r.X1, r.Y1), st));
        res.Add(new PLine(new Vec2(r.X1, r.Y1), new Vec2(r.X0, r.Y1), st));
        res.Add(new PLine(new Vec2(r.X0, r.Y1), new Vec2(r.X0, r.Y0), st));
    }

    /// <summary>Partes do segmento fora dos retângulos.</summary>
    public static List<(Vec2, Vec2)> Clip(Vec2 a, Vec2 b, IReadOnlyList<PaperRect> holes)
    {
        var spans = new List<(double, double)> { (0, 1) };
        var d = b - a;
        foreach (var h in holes)
        {
            // Liang–Barsky: intervalo [t0, t1] dentro do retângulo.
            double t0 = 0, t1 = 1;
            bool inside = true;
            foreach (var (p, q) in new[] { (-d.X, a.X - h.X0), (d.X, h.X1 - a.X), (-d.Y, a.Y - h.Y0), (d.Y, h.Y1 - a.Y) })
            {
                if (Math.Abs(p) < 1e-12) { if (q < 0) { inside = false; break; } continue; }
                double r = q / p;
                if (p < 0) t0 = Math.Max(t0, r); else t1 = Math.Min(t1, r);
            }
            if (!inside || t0 >= t1) continue;
            var next = new List<(double, double)>();
            foreach (var (s0, s1) in spans)
            {
                if (t1 <= s0 || t0 >= s1) { next.Add((s0, s1)); continue; }
                if (t0 > s0) next.Add((s0, t0));
                if (t1 < s1) next.Add((t1, s1));
            }
            spans = next;
        }
        return spans.Where(s => (s.Item2 - s.Item1) * d.Length > 0.3).Select(s => (a + d * s.Item1, a + d * s.Item2)).ToList();
    }

    /// <summary>Quebra o texto em linhas que cabem na largura.</summary>
    public static List<string> Wrap(string text, double widthMm, double sizeMm, bool bold = false)
    {
        var res = new List<string>();
        foreach (var para in text.Replace("\r", "").Split('\n'))
        {
            var words = para.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cur = "";
            foreach (var w in words)
            {
                string cand = cur.Length == 0 ? w : cur + " " + w;
                if (AnnotationPlanner.TextWidth(cand, sizeMm, bold) <= widthMm || cur.Length == 0) cur = cand;
                else { res.Add(cur); cur = w; }
            }
            res.Add(cur);
        }
        return res;
    }

    private static double LineH(double sizeMm) => sizeMm * 1.55;

    /// <summary>Texto que cabe no retângulo (quebra linhas e reduz até 1,3 mm).</summary>
    private static void FitText(List<SheetPrim> res, PaperRect r, string text, double size, bool bold, PTextH h)
    {
        List<string> lines;
        while (true)
        {
            lines = Wrap(text, r.Width, size, bold);
            if (lines.Count * LineH(size) <= r.Height || size <= 1.3) break;
            size = Math.Round(size - 0.1, 2);
        }
        double x = h switch { PTextH.Center => r.Center.X, PTextH.Right => r.X1, _ => r.X0 };
        res.Add(new PText(new Vec2(x, r.Y1), string.Join("\n", lines), size, bold, h, PTextV.Top));
    }

    private static void Fields(List<SheetPrim> res, PaperRect body, IReadOnlyList<SheetField> fields, SheetTextStyles st)
    {
        var inner = new PaperRect(body.X0 + 1.2, body.Y0 + 0.8, body.X1 - 1.2, body.Y1 - 0.9);
        double size = st.ValorMm;
        List<string> lines;
        while (true)
        {
            lines = new List<string>();
            foreach (var f in fields)
                lines.AddRange(Wrap($"{f.Label}: {(string.IsNullOrWhiteSpace(f.Value) ? "—" : f.Value)}", inner.Width, size));
            if (lines.Count * LineH(size) <= inner.Height || size <= 1.3) break;
            size = Math.Round(size - 0.1, 2);
        }
        res.Add(new PText(new Vec2(inner.X0, inner.Y1), string.Join("\n", lines), size, false, PTextH.Left, PTextV.Top));
    }

    private static void Quadro(List<SheetPrim> res, PaperRect body, IReadOnlyList<SheetField> fields, SheetTextStyles st)
    {
        if (fields.Count == 0) return;
        double h = body.Height / fields.Count;
        for (int k = 0; k < fields.Count; k++)
        {
            var cell = new PaperRect(body.X0, body.Y1 - (k + 1) * h, body.X1, body.Y1 - k * h);
            double size = st.ValorMm;
            while (size > 1.3 && AnnotationPlanner.TextWidth(fields[k].Value, size, true) > cell.Width - 2) size = Math.Round(size - 0.1, 2);
            res.Add(new PText(new Vec2(cell.Center.X, cell.Center.Y + 0.4), fields[k].Label, st.RotuloMm, false, PTextH.Center, PTextV.Bottom));
            res.Add(new PText(new Vec2(cell.Center.X, cell.Center.Y - 0.2), fields[k].Value, size, true, PTextH.Center, PTextV.Top));
            if (k > 0) res.Add(new PLine(new Vec2(cell.X0 + 2, cell.Y1), new Vec2(cell.X1 - 2, cell.Y1), PLineStyle.Tracejada));
        }
    }

    private static void Signatures(List<SheetPrim> res, PaperRect body, IReadOnlyList<string[]> sigs, SheetTextStyles st)
    {
        if (sigs.Count == 0) return;
        double h = body.Height / sigs.Count;
        for (int k = 0; k < sigs.Count; k++)
        {
            var cell = new PaperRect(body.X0, body.Y1 - (k + 1) * h, body.X1, body.Y1 - k * h);
            var lines = sigs[k].Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            double size = st.RotuloMm;
            double textH = lines.Count * LineH(size);
            double ly = cell.Y0 + Math.Max(textH + 1.2, cell.Height * 0.38);
            res.Add(new PLine(new Vec2(cell.X0 + 3, ly), new Vec2(cell.X1 - 3, ly), PLineStyle.Fina));
            var wrapped = lines.SelectMany(l => Wrap(l, cell.Width - 2, size)).ToList();
            while (size > 1.2 && wrapped.Count * LineH(size) > ly - cell.Y0 - 0.5)
            {
                size = Math.Round(size - 0.1, 2);
                wrapped = lines.SelectMany(l => Wrap(l, cell.Width - 2, size)).ToList();
            }
            res.Add(new PText(new Vec2(cell.Center.X, ly - 0.5), string.Join("\n", wrapped), size, false, PTextH.Center, PTextV.Top));
        }
    }

    private static void Legend(List<SheetPrim> res, PaperRect body, IReadOnlyList<LegendItem> items, SheetTextStyles st, IReadOnlyList<(byte R, byte G, byte B)> palette)
    {
        if (items.Count == 0) return;
        var inner = body.Inset(1.2);
        double rowH = Math.Min(st.RotuloMm * 2.4, inner.Height / items.Count);
        double size = Math.Min(st.RotuloMm, rowH / 1.7);
        double symW = Math.Min(8, inner.Width * 0.28);
        double y = inner.Y1 - rowH / 2;
        foreach (var it in items)
        {
            double sx = inner.X0, cy = y, sh = Math.Min(rowH * 0.6, 2.6);
            var sym = new PaperRect(sx, cy - sh / 2, sx + symW, cy + sh / 2);
            var col = palette.Count == 0 ? ((byte)220, (byte)60, (byte)50) : palette[Math.Abs(it.ColorIndex) % palette.Count];
            switch (it.Kind)
            {
                case LegendKind.AreaImovel:
                    res.Add(new PFill(Corners(sym), col.Item1, col.Item2, col.Item3, Hatch: true));
                    Rect(res, sym, PLineStyle.Limite);
                    break;
                case LegendKind.Remanescente:
                    res.Add(new PFill(Corners(sym), col.Item1, col.Item2, col.Item3, Hatch: true, Cross: true));
                    Rect(res, sym, PLineStyle.Limite);
                    break;
                case LegendKind.AreaEdificada:
                    res.Add(new PFill(Corners(sym), 150, 150, 150, Hatch: true, Cross: true));
                    Rect(res, sym, PLineStyle.Fina);
                    break;
                case LegendKind.Limite:
                    res.Add(new PLine(new Vec2(sym.X0, cy), new Vec2(sym.X1, cy), PLineStyle.Limite));
                    break;
                case LegendKind.Vertice:
                    res.Add(new PLine(new Vec2(sym.X0, cy), new Vec2(sym.X1, cy), PLineStyle.Fina));
                    res.Add(new PCircle(sym.Center, Math.Min(0.9, sh / 2), true));
                    break;
                case LegendKind.PontoReferencia:
                    res.Add(new PCircle(new Vec2(sym.X0 + 1.2, cy - 0.5), 0.9, true));
                    res.Add(new PLine(new Vec2(sym.X0 + 1.2, cy - 0.5), new Vec2(sym.X0 + 3.4, cy + 0.9), PLineStyle.Fina));
                    res.Add(new PLine(new Vec2(sym.X0 + 3.4, cy + 0.9), new Vec2(sym.X1, cy + 0.9), PLineStyle.Fina));
                    break;
                case LegendKind.MudancaConfrontante:
                    res.Add(new PLine(new Vec2(sym.X0, cy), new Vec2(sym.X1, cy), PLineStyle.Limite));
                    res.Add(new PLine(new Vec2(sym.Center.X, cy - sh / 2), new Vec2(sym.Center.X, cy + sh / 2), PLineStyle.Vermelha));
                    break;
                case LegendKind.MeioFio:
                    res.Add(new PLine(new Vec2(sym.X0, cy), new Vec2(sym.X1, cy), PLineStyle.MeioFio));
                    break;
                case LegendKind.Calcada:
                    res.Add(new PLine(new Vec2(sym.X0, cy - sh / 2), new Vec2(sym.X1, cy - sh / 2), PLineStyle.MeioFio));
                    res.Add(new PLine(new Vec2(sym.X0, cy + sh / 2), new Vec2(sym.X1, cy + sh / 2), PLineStyle.Limite));
                    break;
            }
            double tx = sym.X1 + 1.5;
            double s2 = size;
            while (s2 > 1.1 && AnnotationPlanner.TextWidth(it.Text, s2) > inner.X1 - tx) s2 = Math.Round(s2 - 0.1, 2);
            res.Add(new PText(new Vec2(tx, cy), it.Text, s2, false, PTextH.Left, PTextV.Middle));
            y -= rowH;
        }
    }

    private static IReadOnlyList<Vec2> Corners(PaperRect r) => new[] { new Vec2(r.X0, r.Y0), new Vec2(r.X1, r.Y0), new Vec2(r.X1, r.Y1), new Vec2(r.X0, r.Y1) };

    private static void Orientation(List<SheetPrim> res, PaperRect body, SheetRenderInput i, SheetTextStyles st)
    {
        var inner = body.Inset(1.2);
        double size = Math.Min(st.RotuloMm, 1.6);
        var lines = i.Content.Orientacao.SelectMany(l => Wrap(l, inner.Width, size)).ToList();
        double barH = 1.6, scaleH = st.ValorMm * 1.9;
        double avail = inner.Height - scaleH - barH - 3.5;
        while (size > 1.1 && lines.Count * LineH(size) > avail)
        {
            size = Math.Round(size - 0.1, 2);
            lines = i.Content.Orientacao.SelectMany(l => Wrap(l, inner.Width, size)).ToList();
        }
        if (lines.Count > 0) res.Add(new PText(new Vec2(inner.Center.X, inner.Y1), string.Join("\n", lines), size, false, PTextH.Center, PTextV.Top));
        double yScale = inner.Y0 + barH + 3.6;
        res.Add(new PText(new Vec2(inner.Center.X, yScale), $"ESCALA: 1:{i.Scale}", st.ValorMm, true, PTextH.Center, PTextV.Bottom));
        var bar = ScaleBar.Build(i.Scale, inner.Width - 6);
        double bx = inner.Center.X - bar.LengthMm / 2, by = inner.Y0 + 2.2;
        for (int k = 0; k < bar.Segments; k++)
        {
            double x0 = bx + bar.LengthMm * k / bar.Segments, x1 = bx + bar.LengthMm * (k + 1) / bar.Segments;
            var r = new PaperRect(x0, by, x1, by + barH * 0.6);
            if (k % 2 == 0) res.Add(new PFill(Corners(r), 0, 0, 0));
            Rect(res, r, PLineStyle.Fina);
        }
        // Valores só no início, no meio e no fim (legíveis mesmo em barras curtas).
        for (int k = 0; k < bar.Ticks.Count; k++)
        {
            if (k != 0 && k != bar.Ticks.Count - 1 && k != bar.Ticks.Count / 2) continue;
            var (mm, text) = bar.Ticks[k];
            res.Add(new PText(new Vec2(bx + mm, by - 0.3), text, Math.Min(1.4, size), false, PTextH.Center, PTextV.Top));
        }
    }

    private static void Title(List<SheetPrim> res, PaperRect r, SheetRenderInput i, SheetTextStyles st)
    {
        double fw = Math.Min(16, r.Width * 0.24);
        var main = new PaperRect(r.X0, r.Y0, r.X1 - fw, r.Y1);
        var folha = new PaperRect(r.X1 - fw, r.Y0, r.X1, r.Y1);
        res.Add(new PLine(new Vec2(folha.X0, folha.Y0), new Vec2(folha.X0, folha.Y1), PLineStyle.Fina));
        res.Add(new PText(new Vec2(folha.Center.X, folha.Center.Y + 0.6), "FOLHA", st.RotuloMm, false, PTextH.Center, PTextV.Bottom));
        res.Add(new PText(new Vec2(folha.Center.X, folha.Center.Y), i.Folha, st.TituloMm, true, PTextH.Center, PTextV.Top));
        // Título (quebra/reduz para caber), subtítulo e data/número da prancha embaixo.
        var inner = main.Inset(1.0);
        string sub = i.Content.Subtitulo;
        string foot = $"{i.Date:dd/MM/yyyy}" + (i.SheetNumber.Length > 0 ? $"  •  {i.SheetNumber}" : "");
        double footH = LineH(1.4);
        double subSize = st.RotuloMm;
        var subLines = sub.Length > 0 ? Wrap(sub, inner.Width, subSize) : new List<string>();
        double size = st.TituloMm;
        List<string> tl;
        while (true)
        {
            tl = Wrap(i.Content.Titulo, inner.Width, size, true);
            if (tl.Count * LineH(size) + subLines.Count * LineH(subSize) + footH <= inner.Height || size <= 1.6) break;
            size = Math.Round(size - 0.1, 2);
        }
        double blockH = tl.Count * LineH(size) + subLines.Count * LineH(subSize);
        double top = inner.Y0 + footH + (inner.Height - footH + blockH) / 2;
        top = Math.Min(top, inner.Y1);
        res.Add(new PText(new Vec2(inner.Center.X, top), string.Join("\n", tl), size, true, PTextH.Center, PTextV.Top));
        if (subLines.Count > 0)
            res.Add(new PText(new Vec2(inner.Center.X, top - tl.Count * LineH(size)), string.Join("\n", subLines), subSize, false, PTextH.Center, PTextV.Top));
        res.Add(new PText(new Vec2(inner.Center.X, inner.Y0), foot, 1.4, false, PTextH.Center, PTextV.Bottom, R: 90, G: 90, B: 90));
    }

    private static void NorthArrow(List<SheetPrim> res, PaperRect box, Vec2 dir, SheetTextStyles st)
    {
        dir = dir.LengthSquared < 1e-9 ? new Vec2(0, 1) : dir.Normalized();
        var side = dir.PerpRight;
        double len = Math.Min(box.Height * 0.62, box.Width * 1.25);
        var c = new Vec2(box.Center.X, box.Y0 + box.Height * 0.42);
        var tip = c + dir * (len / 2);
        var tail = c - dir * (len / 2);
        var notch = c - dir * (len * 0.22);
        var bl = tail - side * (len * 0.32);
        var br = tail + side * (len * 0.32);
        // Metade escura e metade clara (como no modelo).
        res.Add(new PFill(new[] { tip, br, notch }, 0, 0, 0));
        res.Add(new PLine(tip, bl, PLineStyle.Limite));
        res.Add(new PLine(bl, notch, PLineStyle.Limite));
        res.Add(new PLine(notch, br, PLineStyle.Limite));
        res.Add(new PLine(br, tip, PLineStyle.Limite));
        res.Add(new PText(tip + dir * 0.8, "N", Math.Max(2.2, st.ValorMm * 1.4), true, PTextH.Center, PTextV.Bottom));
    }

    private static void Table(List<SheetPrim> res, SheetTable t, PaperRect r)
    {
        res.Add(new PFill(Corners(r), 255, 255, 255));
        Rect(res, r, PLineStyle.Bloco);
        double y = r.Y1 - 0.6;
        for (int k = 0; k < t.TitleLines.Count; k++)
        {
            res.Add(new PText(new Vec2(r.Center.X, y - t.TitleLineH / 2), t.TitleLines[k], t.TitleMm, k == 0, PTextH.Center, PTextV.Middle));
            y -= t.TitleLineH;
        }
        y -= 0.6;
        double x0 = r.X0 + (r.Width - t.Width) / 2;
        var xs = new List<double> { x0 };
        foreach (var w in t.ColWidths) xs.Add(xs[^1] + w);
        int rows = t.Rows.Count + 1;
        double top = y, bottom = y - rows * t.RowH;
        for (int k = 0; k <= rows; k++) res.Add(new PLine(new Vec2(xs[0], top - k * t.RowH), new Vec2(xs[^1], top - k * t.RowH), PLineStyle.Fina));
        foreach (var x in xs) res.Add(new PLine(new Vec2(x, top), new Vec2(x, bottom), PLineStyle.Fina));
        void Row(int k, string[] cells, bool bold)
        {
            double cy = top - k * t.RowH - t.RowH / 2;
            for (int c = 0; c < cells.Length && c < t.ColWidths.Length; c++)
                if (!string.IsNullOrEmpty(cells[c]))
                    res.Add(new PText(new Vec2((xs[c] + xs[c + 1]) / 2, cy), cells[c], t.TextMm, bold, PTextH.Center, PTextV.Middle));
        }
        Row(0, t.Header, true);
        for (int k = 0; k < t.Rows.Count; k++) Row(k + 1, t.Rows[k], t.BoldRows.Contains(k));
    }
}
