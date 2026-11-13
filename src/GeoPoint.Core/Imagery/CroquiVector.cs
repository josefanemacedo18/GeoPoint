using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Urban;

namespace GeoPoint.Core.Imagery;

/// <summary>Cor RGBA.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static readonly Rgba None = new(0, 0, 0, 0);
}

/// <summary>Polígono ou linha do croqui (pixels da imagem, origem no canto superior esquerdo, y para baixo).</summary>
public sealed record CroquiShape(IReadOnlyList<Vec2> Pts, bool Closed, Rgba Fill, Rgba Stroke, double Width, bool Dashed = false);

/// <summary>Texto do croqui: <see cref="At"/> é o centro; <see cref="SizePx"/> é o tamanho da fonte (em).</summary>
public sealed record CroquiText(Vec2 At, string Text, double SizePx, double AngleRad, bool Bold, Rgba Color, bool Halo = true);

/// <summary>Croqui pronto para desenhar (WPF no Revit, SVG nas prévias).</summary>
public sealed class CroquiDrawing
{
    public int Width { get; init; }
    public int Height { get; init; }
    public Rgba Background { get; init; } = new(246, 244, 239);
    public List<CroquiShape> Shapes { get; } = new();
    public List<CroquiText> Texts { get; } = new();
    /// <summary>Largura mostrada no terreno (m).</summary>
    public double SpanM { get; init; }

    /// <summary>Assinatura do conteúdo (para o nome do arquivo: o mesmo croqui gera o mesmo arquivo, sem duplicar imagens no Revit).</summary>
    public string Signature()
    {
        var sb = new System.Text.StringBuilder($"v1|{Width}x{Height}|");
        foreach (var s in Shapes) sb.Append(string.Join(",", s.Pts.Select(p => $"{p.X:0.#},{p.Y:0.#}"))).Append($"|{s.Fill}|{s.Stroke}|{s.Width:0.##};");
        foreach (var t in Texts) sb.Append($"{t.Text}@{t.At.X:0.#},{t.At.Y:0.#},{t.SizePx:0.#},{t.AngleRad:0.###};");
        return sb.ToString();
    }
}

/// <summary>
/// Croqui de localização desenhado (sem imagem de satélite): os lotes do projeto em volta, o(s) lote(s) da prancha em destaque,
/// as vias com o nome, o norte e a escala gráfica. Funciona sem internet e sem georreferência.
/// </summary>
public static class CroquiVector
{
    public static readonly Rgba Highlight = new(230, 110, 0);
    public static readonly Rgba HighlightFill = new(255, 150, 40, 120);

    /// <param name="aspect">Largura/altura do bloco do croqui.</param>
    /// <param name="context">Quantas vezes o tamanho do(s) lote(s) a janela mostra.</param>
    /// <param name="minSpanM">Largura mínima mostrada (m), para lotes pequenos mostrarem a quadra.</param>
    public static CroquiDrawing Build(ProjectContext ctx, IReadOnlyList<LotRecord> lots, int widthPx, double aspect, double context = 3, double minSpanM = 80)
    {
        aspect = Math.Clamp(aspect, 0.2, 5);
        int W = Math.Max(64, widthPx), H = Math.Max(48, (int)Math.Round(W / aspect));
        List<Vec2> Ring(LotRecord l)
        {
            try { return l.ToRegion().Outer.ToPolygon(0.1).Select(ctx.Coords.ToEnu).ToList(); }
            catch { return new List<Vec2>(); }
        }
        var mine = lots.Select(Ring).Where(r => r.Count >= 3).ToList();
        if (mine.Count == 0) throw new InvalidOperationException("Sem lotes para o croqui.");
        var bb = BBox.Of(mine.SelectMany(r => r));
        double spanW = Math.Max(Math.Max(bb.Width, bb.Height * aspect) * context, minSpanM);
        double spanH = spanW * H / W;
        double s = W / spanW;   // px por metro
        var c = bb.Center;
        Vec2 Img(Vec2 e) => new((e.X - (c.X - spanW / 2)) * s, ((c.Y + spanH / 2) - e.Y) * s);
        var view = new BBox(c.X - spanW / 2, c.Y - spanH / 2, c.X + spanW / 2, c.Y + spanH / 2);

        var d = new CroquiDrawing { Width = W, Height = H, SpanM = spanW };
        double line = Math.Max(1, W / 400.0);

        // Lotes vizinhos (todos os lotes ativos do projeto que aparecem na janela).
        var ids = new HashSet<string>(lots.Select(l => l.Id));
        foreach (var l in ctx.Project.ActiveLots.Where(l => !ids.Contains(l.Id)))
        {
            var r = Ring(l);
            if (r.Count < 3 || !BBox.Of(r).Intersects(view)) continue;
            d.Shapes.Add(new CroquiShape(r.Select(Img).ToList(), true, new Rgba(229, 228, 222), new Rgba(150, 150, 145), line));
        }

        // Vias: o desenho esquemático (meio-fio e alinhamento) quando há vias cadastradas; senão, o nome na testada.
        bool streetNames = false;
        try
        {
            var frontages = new List<StreetFrontage>();
            var regions = new List<Region>();
            foreach (var l in ctx.Project.ActiveLots)
            {
                try
                {
                    var rep = ctx.Report(l);
                    regions.Add(rep.Region);
                    foreach (var seg in rep.Segments.Where(x => x.ConfrontanteViaId.Length > 0))
                        frontages.Add(new StreetFrontage(seg.ConfrontanteViaId, seg.Curve, seg.OutwardNormal));
                }
                catch { }
            }
            var vias = ctx.Project.Vias.Where(v => frontages.Any(f => f.ViaId == v.Id) || v.MeioFio.Count > 0).ToList();
            if (vias.Count > 0)
            {
                var crossings = ctx.Project.Cruzamentos.Where(x => x.ViaIds.Count(id => vias.Any(v => v.Id == id)) >= 2).ToList();
                var st = StreetSchematic.Build(vias, crossings, frontages, regions);
                foreach (var cb in st.Curbs) d.Shapes.Add(new CroquiShape(cb.Select(p => Img(ctx.Coords.ToEnu(p))).ToList(), false, Rgba.None, new Rgba(95, 95, 95), line * 1.4));
                foreach (var al in st.Alignments) d.Shapes.Add(new CroquiShape(al.Select(p => Img(ctx.Coords.ToEnu(p))).ToList(), false, Rgba.None, new Rgba(160, 160, 155), line));
                foreach (var lb in st.Labels)
                {
                    var at = ctx.Coords.ToEnu(lb.Position);
                    if (!view.Contains(at)) continue;
                    double ang = AnnotationPlanner.Readable(lb.AngleRad + ctx.Coords.Rotation);
                    d.Texts.Add(new CroquiText(Img(at), lb.Text.ToUpperInvariant(), Math.Max(9, W / 34.0), -ang, true, new Rgba(60, 60, 60)));
                    streetNames = true;
                }
            }
        }
        catch { /* sem vias desenháveis: segue com os nomes nas testadas */ }

        // Lote(s) da prancha em destaque.
        foreach (var r in mine)
            d.Shapes.Add(new CroquiShape(r.Select(Img).ToList(), true, HighlightFill, Highlight, Math.Max(2, W / 160.0)));

        // Nome da via na testada (quando não há o desenho das vias): fora do lote, ao longo da divisa.
        if (!streetNames)
        {
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in lots)
            {
                LotReport rep;
                try { rep = ctx.Report(l); } catch { continue; }
                foreach (var seg in rep.Segments.Where(x => x.Testada && x.Confrontante.Trim().Length > 0).OrderByDescending(x => x.Curve.Length))
                {
                    string name = seg.Confrontante.Trim().ToUpperInvariant();
                    if (!done.Add(name)) continue;
                    var mid = seg.Curve.PointAt(0.5);
                    double size = Math.Max(9, W / 34.0);
                    var at = ctx.Coords.ToEnu(mid + seg.OutwardNormal * (size * 1.4 / s));
                    var dir = ctx.Coords.VectorToEnu(seg.Curve.End - seg.Curve.Start);
                    double ang = AnnotationPlanner.Readable(Math.Atan2(dir.Y, dir.X));
                    d.Texts.Add(new CroquiText(Img(at), name, size, -ang, true, new Rgba(60, 60, 60)));
                }
            }
        }

        // Nome do lote dentro dele, se couber.
        foreach (var (l, r) in lots.Zip(lots.Select(Ring)).Where(x => x.Second.Count >= 3))
        {
            var rb = BBox.Of(r.Select(Img));
            double size = Math.Max(8, W / 40.0);
            string name = l.DisplayName;
            if (AnnotationPlanner.TextWidth(name, size * AnnotationPlanner.ArialCapHeight, true) < rb.Width * 0.9 && size * 1.3 < rb.Height * 0.8)
                d.Texts.Add(new CroquiText(Img(BBox.Of(r).Center), name, size, 0, true, new Rgba(120, 50, 0)));
        }

        AddNorth(d, W, H);
        AddScaleBar(d, W, H, s);
        return d;
    }

    private static void AddNorth(CroquiDrawing d, int W, int H)
    {
        double h = Math.Max(18, Math.Min(W, H) / 7.0), w = h * 0.55;
        var tip = new Vec2(W - w - 8, 8);
        var baseL = tip + new Vec2(-w / 2, h);
        var baseR = tip + new Vec2(w / 2, h);
        var notch = tip + new Vec2(0, h * 0.72);
        d.Shapes.Add(new CroquiShape(new[] { tip, baseL, notch }, true, new Rgba(255, 255, 255), new Rgba(40, 40, 40), 1.2));
        d.Shapes.Add(new CroquiShape(new[] { tip, notch, baseR }, true, new Rgba(40, 40, 40), new Rgba(40, 40, 40), 1.2));
        d.Texts.Add(new CroquiText(tip + new Vec2(0, h + h * 0.28), "N", h * 0.42, 0, true, new Rgba(40, 40, 40)));
    }

    /// <summary>Escala gráfica com um comprimento "redondo" de cerca de 1/4 da largura.</summary>
    private static void AddScaleBar(CroquiDrawing d, int W, int H, double pxPerM)
    {
        double want = W / 4.0 / pxPerM;
        double[] nice = { 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000, 5000 };
        double len = nice.LastOrDefault(n => n <= want);
        if (len <= 0) len = nice[0];
        double px = len * pxPerM, x0 = 10, y0 = H - 12, th = Math.Max(4, H / 60.0);
        d.Shapes.Add(new CroquiShape(new[] { new Vec2(x0, y0 - th), new Vec2(x0 + px / 2, y0 - th), new Vec2(x0 + px / 2, y0), new Vec2(x0, y0) }, true, new Rgba(40, 40, 40), new Rgba(40, 40, 40), 1));
        d.Shapes.Add(new CroquiShape(new[] { new Vec2(x0 + px / 2, y0 - th), new Vec2(x0 + px, y0 - th), new Vec2(x0 + px, y0), new Vec2(x0 + px / 2, y0) }, true, new Rgba(255, 255, 255), new Rgba(40, 40, 40), 1));
        double size = Math.Max(8, W / 45.0);
        d.Texts.Add(new CroquiText(new Vec2(x0 + px / 2, y0 - th - size * 0.8), $"{len:0} m", size, 0, false, new Rgba(40, 40, 40)));
    }
}
