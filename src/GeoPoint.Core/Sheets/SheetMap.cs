using System.Globalization;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;

namespace GeoPoint.Core.Sheets;

public enum MapSide { Inferior, Superior, Esquerda, Direita }

/// <summary>Valor da grade escrito na borda do mapa.</summary>
public readonly record struct GridLabel(Vec2 At, MapSide Side, string Text);

/// <summary>Linha da grade de coordenadas no papel (mm), já recortada pelo mapa.</summary>
public sealed class GridLine
{
    /// <summary>true = linha de E/longitude constante (corta o mapa de baixo para cima).</summary>
    public bool IsA { get; init; }
    public double Value { get; init; }
    public Vec2 P0 { get; init; }
    public Vec2 P1 { get; init; }
    public List<GridLabel> Labels { get; } = new();
}

/// <summary>Grade de coordenadas ("linhas de fundo") do mapa da prancha, com os valores nas bordas.</summary>
public static class MapGrid
{
    /// <param name="map">Retângulo do mapa no papel (mm).</param>
    /// <param name="crsAt">Coordenada (A, B) no sistema da grade para um ponto do papel. A = E (ou longitude), B = N (ou latitude).</param>
    /// <param name="geographic">Sistema geográfico (graus): passos em segundos e rótulos em graus/minutos/segundos.</param>
    /// <param name="targetMm">Espaçamento desejado entre linhas (mm no papel).</param>
    public static List<GridLine> Build(PaperRect map, Func<Vec2, (double A, double B)?> crsAt, bool geographic, double targetMm, bool westAsO = true)
    {
        var res = new List<GridLine>();
        var c00 = crsAt(new Vec2(map.X0, map.Y0));
        var c10 = crsAt(new Vec2(map.X1, map.Y0));
        var c01 = crsAt(new Vec2(map.X0, map.Y1));
        if (c00 is null || c10 is null || c01 is null) return res;
        // Mapeamento afim papel → coordenadas (a área de uma prancha é pequena: erro desprezível).
        var (a0, b0) = c00.Value;
        double aX = (c10.Value.A - a0) / map.Width, bX = (c10.Value.B - b0) / map.Width;
        double aY = (c01.Value.A - a0) / map.Height, bY = (c01.Value.B - b0) / map.Height;
        double Aat(Vec2 p) => a0 + aX * (p.X - map.X0) + aY * (p.Y - map.Y0);
        double Bat(Vec2 p) => b0 + bX * (p.X - map.X0) + bY * (p.Y - map.Y0);
        var corners = new[] { new Vec2(map.X0, map.Y0), new Vec2(map.X1, map.Y0), new Vec2(map.X1, map.Y1), new Vec2(map.X0, map.Y1) };

        void Family(bool isA, Func<Vec2, double> f, double gradPerMm)
        {
            if (gradPerMm <= 0) return;
            double step = NiceStep(gradPerMm * targetMm, geographic);
            double lo = corners.Min(f), hi = corners.Max(f);
            for (double v = Math.Ceiling(lo / step) * step; v <= hi + 1e-12; v += step)
            {
                var pts = new List<(Vec2 P, MapSide S)>();
                for (int i = 0; i < 4; i++)
                {
                    var p = corners[i]; var q = corners[(i + 1) % 4];
                    double fp = f(p) - v, fq = f(q) - v;
                    if (fp * fq > 0 || Math.Abs(fp - fq) < 1e-15) continue;
                    double t = fp / (fp - fq);
                    var x = p + (q - p) * t;
                    var side = i switch { 0 => MapSide.Inferior, 1 => MapSide.Direita, 2 => MapSide.Superior, _ => MapSide.Esquerda };
                    if (!pts.Any(o => o.P.DistanceTo(x) < 0.01)) pts.Add((x, side));
                }
                if (pts.Count < 2) continue;
                var line = new GridLine { IsA = isA, Value = v, P0 = pts[0].P, P1 = pts[1].P };
                if (line.P0.DistanceTo(line.P1) < 2) continue;
                string text = Label(v, isA, geographic, westAsO);
                foreach (var (pp, ss) in pts.Take(2)) line.Labels.Add(new GridLabel(pp, ss, text));
                res.Add(line);
            }
        }
        Family(true, Aat, Math.Sqrt(aX * aX + aY * aY));
        Family(false, Bat, Math.Sqrt(bX * bX + bY * bY));
        return res;
    }

    private static readonly double[] MetricSteps = { 1, 2, 2.5, 5 };
    private static readonly double[] SecondSteps = { 0.1, 0.2, 0.5, 1, 2, 3, 5, 10, 15, 20, 30, 60, 120, 180, 300, 600, 900, 1200, 1800, 3600, 7200 };

    /// <summary>Passo "redondo" mais próximo do desejado (metros, ou graus quando geográfico).</summary>
    public static double NiceStep(double wanted, bool geographic)
    {
        if (wanted <= 0) return 1;
        if (geographic)
        {
            double sec = wanted * 3600;
            double best = SecondSteps.OrderBy(s => Math.Abs(Math.Log(s / sec))).First();
            return best / 3600;
        }
        double exp = Math.Pow(10, Math.Floor(Math.Log10(wanted)));
        double bestM = exp;
        double bestErr = double.MaxValue;
        foreach (double m in MetricSteps.Concat(new[] { 10.0 }))
        {
            double c = m * exp;
            double err = Math.Abs(Math.Log(c / wanted));
            if (err < bestErr) { bestErr = err; bestM = c; }
        }
        return bestM;
    }

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Label(double v, bool isA, bool geographic, bool westAsO = true)
    {
        if (!geographic)
        {
            string num = Math.Abs(v - Math.Round(v)) < 1e-6 ? v.ToString("#,##0", PtBr) : v.ToString("#,##0.0", PtBr);
            return (isA ? "E " : "N ") + num;
        }
        // A = longitude, B = latitude (graus).
        double abs = Math.Abs(v);
        int d = (int)Math.Floor(abs + 1e-12);
        double mf = (abs - d) * 60;
        int m = (int)Math.Floor(mf + 1e-9);
        double s = (mf - m) * 60;
        if (s > 59.95) { s = 0; m++; }
        if (m >= 60) { m = 0; d++; }
        string sec = Math.Abs(s - Math.Round(s)) < 0.05 ? Math.Round(s).ToString("00", PtBr) : s.ToString("00.0", PtBr);
        string hemi = isA ? (v < 0 ? (westAsO ? "O" : "W") : "E") : (v < 0 ? "S" : "N");
        return $"{d}°{m:00}'{sec}\" {hemi}";
    }
}

/// <summary>Escala gráfica.</summary>
public sealed class ScaleBarSpec
{
    public double TotalM { get; init; }
    public double LengthMm { get; init; }
    public int Segments { get; init; }
    public List<(double Mm, string Text)> Ticks { get; } = new();
}

public static class ScaleBar
{
    /// <summary>Escala gráfica que cabe em <paramref name="maxMm"/> (comprimento "redondo" no terreno).</summary>
    public static ScaleBarSpec Build(int scale, double maxMm, int segments = 4)
    {
        double maxM = maxMm * scale / 1000.0;
        double[] nice = { 1, 2, 2.5, 4, 5 };
        double best = 1;
        for (double e = 0.01; e <= 1e6; e *= 10)
            foreach (double n in nice)
            {
                double v = n * e;
                if (v <= maxM + 1e-9 && v > best) best = v;
            }
        var spec = new ScaleBarSpec { TotalM = best, LengthMm = best * 1000.0 / scale, Segments = segments };
        for (int i = 0; i <= segments; i++)
        {
            double m = best * i / segments;
            string t = m.ToString(m < 10 && Math.Abs(m - Math.Round(m)) > 1e-9 ? "0.#" : "0", CultureInfo.GetCultureInfo("pt-BR"));
            if (i == segments) t += " m";
            spec.Ticks.Add((spec.LengthMm * i / segments, t));
        }
        return spec;
    }
}

/// <summary>Resultado do encaixe do desenho no mapa.</summary>
public sealed class MapFitResult
{
    public int Scale { get; init; }
    /// <summary>Onde fica o centro do desenho no papel (mm).</summary>
    public Vec2 DrawingCenter { get; init; }
    /// <summary>Retângulo da tabela (coordenadas ou quadro de lotes) dentro do mapa; null = sem tabela.</summary>
    public PaperRect? Table { get; init; }
    public MapCorner Corner { get; init; }
    /// <summary>false = não coube sem sobreposição (usou a melhor posição possível).</summary>
    public bool Fits { get; init; }
}

public static class MapFit
{
    /// <summary>
    /// Escolhe a menor escala (da lista) em que o desenho (<paramref name="wM"/> × <paramref name="hM"/> m, mais a folga para as anotações)
    /// cabe no mapa sem encostar na tabela nem no norte. A tabela vai para um canto do mapa; o desenho se desloca para o espaço livre.
    /// </summary>
    public static MapFitResult Fit(PaperRect map, double wM, double hM, double marginMm, (double W, double H)? table, PaperRect? north,
        MapCorner preferred, IReadOnlyList<int> scales, int fixedScale = 0)
    {
        var list = fixedScale > 0 ? new[] { fixedScale } : scales.OrderBy(s => s).ToArray();
        if (list.Length == 0) list = SheetLayout.StandardScales;
        var corners = new[] { preferred }.Concat(Enum.GetValues(typeof(MapCorner)).Cast<MapCorner>().Where(c => c != preferred)).ToList();
        if (north is PaperRect nb) corners = corners.Where(c => !CornerRect(map, c, 1, 1).Intersects(nb)).Concat(corners.Where(c => CornerRect(map, c, 1, 1).Intersects(nb))).ToList();
        const double pad = 1.0;
        foreach (int s in list)
        {
            double w = wM * 1000 / s + 2 * marginMm, h = hM * 1000 / s + 2 * marginMm;
            if (table is not { } tb)
            {
                var c = Place(map, w, h, null, north);
                if (c is { } cc) return new MapFitResult { Scale = s, DrawingCenter = cc, Fits = true, Corner = preferred };
                continue;
            }
            foreach (var corner in corners)
            {
                var tr = CornerRect(map, corner, tb.W, tb.H, pad);
                if (north is PaperRect n2 && tr.Intersects(n2)) continue;
                var c = Place(map, w, h, tr, north);
                if (c is { } cc) return new MapFitResult { Scale = s, DrawingCenter = cc, Table = tr, Corner = corner, Fits = true };
            }
        }
        // Não coube: maior escala da lista, desenho no centro, tabela no canto preferido.
        int last = list[^1];
        return new MapFitResult
        {
            Scale = last, DrawingCenter = map.Center, Fits = false, Corner = preferred,
            Table = table is { } t2 ? CornerRect(map, preferred, t2.W, t2.H, pad) : null,
        };
    }

    public static PaperRect CornerRect(PaperRect map, MapCorner c, double w, double h, double pad = 1.0) => c switch
    {
        MapCorner.InferiorEsquerdo => new PaperRect(map.X0 + pad, map.Y0 + pad, map.X0 + pad + w, map.Y0 + pad + h),
        MapCorner.InferiorDireito => new PaperRect(map.X1 - pad - w, map.Y0 + pad, map.X1 - pad, map.Y0 + pad + h),
        MapCorner.SuperiorDireito => new PaperRect(map.X1 - pad - w, map.Y1 - pad - h, map.X1 - pad, map.Y1 - pad),
        _ => new PaperRect(map.X0 + pad, map.Y1 - pad - h, map.X0 + pad + w, map.Y1 - pad),
    };

    /// <summary>Centro de um retângulo w×h dentro do mapa sem tocar nos obstáculos (o mais perto do centro do mapa); null = não cabe.</summary>
    private static Vec2? Place(PaperRect map, double w, double h, PaperRect? a, PaperRect? b)
    {
        if (w > map.Width + 1e-9 || h > map.Height + 1e-9) return null;
        var obstacles = new[] { a, b }.Where(x => x is not null).Select(x => x!.Value).ToList();
        // Candidatos: centro do mapa e posições encostadas nos obstáculos (antes e depois de cada um, nos dois eixos).
        var xs = new List<double> { map.Center.X, map.X0 + w / 2, map.X1 - w / 2 };
        var ys = new List<double> { map.Center.Y, map.Y0 + h / 2, map.Y1 - h / 2 };
        foreach (var o in obstacles)
        {
            xs.Add(o.X1 + w / 2); xs.Add(o.X0 - w / 2);
            ys.Add(o.Y1 + h / 2); ys.Add(o.Y0 - h / 2);
        }
        Vec2? best = null; double bestD = double.MaxValue;
        foreach (double x in xs)
            foreach (double y in ys)
            {
                if (x - w / 2 < map.X0 - 1e-9 || x + w / 2 > map.X1 + 1e-9 || y - h / 2 < map.Y0 - 1e-9 || y + h / 2 > map.Y1 + 1e-9) continue;
                var r = new PaperRect(x - w / 2, y - h / 2, x + w / 2, y + h / 2);
                if (obstacles.Any(o => o.Intersects(r))) continue;
                double d = new Vec2(x, y).DistanceTo(map.Center);
                if (d < bestD) { bestD = d; best = new Vec2(x, y); }
            }
        return best;
    }
}

/// <summary>Textos da prancha que dependem do sistema de coordenadas.</summary>
public static class SheetTexts
{
    /// <summary>Linhas da chamada do ponto de referência no sistema escolhido (ex.: "235.142,50 m E" / "9.299.292,99 m S").</summary>
    public static List<string> CalloutLines(CoordinateService coords, ValueFormatter fmt, CrsDef def, Vec2 p)
    {
        if (!coords.HasGeoreference || def.Kind == CrsKind.ProjectLocal)
        {
            var local = coords.ToCrs(p, CrsDef.ProjectLocalDef());
            return local is { } l ? new List<string> { $"X = {fmt.Plane(l.East)} m", $"Y = {fmt.Plane(l.North)} m" } : new List<string>();
        }
        var c = coords.ToCrs(p, def);
        if (c is not { } cc) return new List<string>();
        return def.Kind switch
        {
            CrsKind.Geographic => new List<string> { "Lat: " + fmt.Lat(cc.Lat), "Lon: " + fmt.Lon(cc.Lon) },
            CrsKind.LocalTopographic => new List<string> { $"X = {fmt.Plane(cc.East)} m", $"Y = {fmt.Plane(cc.North)} m" },
            CrsKind.Utm => new List<string> { $"{fmt.Plane(cc.East)} m E", $"{fmt.Plane(cc.North)} m {(coords.ResolveZone(def).South ? "S" : "N")}" },
            _ => new List<string> { $"{fmt.Plane(cc.East)} m E", $"{fmt.Plane(cc.North)} m N" },
        };
    }

    /// <summary>Código EPSG (quando conhecido) do sistema.</summary>
    public static string Epsg(CrsDef def, UtmZone zone)
    {
        if (def.Datum != DatumId.Sirgas2000) return "";
        return def.Kind switch
        {
            CrsKind.Geographic => "EPSG:4674",
            CrsKind.Utm when zone.South && zone.Number is >= 18 and <= 25 => "EPSG:" + (31960 + zone.Number),
            CrsKind.Utm when !zone.South && zone.Number is >= 17 and <= 22 => "EPSG:" + (31955 + zone.Number),
            _ => "",
        };
    }

    /// <summary>Linhas do bloco Orientação: sistema geodésico, projeção e EPSG.</summary>
    public static List<string> OrientationLines(CoordinateService coords, CrsDef def)
    {
        if (!coords.HasGeoreference) return new List<string> { "Coordenadas locais do projeto", "(sem georreferência)" };
        var datum = Datum.Get(def.Datum).Name;
        var res = new List<string> { "SISTEMA GEODÉSICO DE REFERÊNCIA:", datum };
        switch (def.Kind)
        {
            case CrsKind.Utm:
            {
                var z = coords.ResolveZone(def);
                res.Add($"PROJEÇÃO UTM – FUSO {z.Number}{(z.South ? "S" : "N")}");
                res.Add($"MC {ValueFormatter.CentralMeridianText(z.CentralMeridianDeg)}");
                var e = Epsg(def, z);
                if (e.Length > 0) res.Add(e);
                break;
            }
            case CrsKind.Geographic:
                res.Add("COORDENADAS GEOGRÁFICAS");
                if (def.Datum == DatumId.Sirgas2000) res.Add("EPSG:4674");
                break;
            case CrsKind.LocalTopographic:
                res.Add("PLANO TOPOGRÁFICO LOCAL (NBR 14166)");
                break;
            case CrsKind.CustomTm:
                res.Add($"PROJEÇÃO {def.CustomName}");
                break;
        }
        return res;
    }
}
