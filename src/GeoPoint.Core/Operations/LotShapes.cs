using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;

namespace GeoPoint.Core.Operations;

public enum LotShapeKind
{
    Quadrado, Retangulo, Triangulo, TrapezioRetangulo, Trapezio, QuadrilateroAngulo, QuadrilateroDiagonal,
    LoteL, EsquinaChanfro, EsquinaRaio, Poligono,
}

/// <summary>Parâmetro de um formato de lote (número, escolha ou texto).</summary>
public sealed class ShapeParam
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public double Default { get; init; }
    public string[]? Choices { get; init; }
    public bool IsText { get; init; }
    public string DefaultText { get; init; } = "";
    public string Hint { get; init; } = "";
}

public sealed class LotShapeDef
{
    public LotShapeKind Kind { get; init; }
    public string Nome { get; init; } = "";
    public string Descricao { get; init; } = "";
    public List<ShapeParam> Params { get; init; } = new();
    /// <summary>Pode ser repetido lado a lado (vários lotes iguais ao longo da frente).</summary>
    public bool Tileable { get; init; }
    public override string ToString() => Nome;
}

public sealed class LotShapeResult
{
    /// <summary>Contorno fechado (anti-horário). Origem no canto frente-esquerda; frente ao longo de +X; lote para +Y.</summary>
    public List<Curve2> Edges { get; } = new();
    /// <summary>Nome de cada lado (Frente, Lateral direita, Fundos...).</summary>
    public List<string> SideNames { get; } = new();
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    /// <summary>Largura da frente (para repetir lado a lado).</summary>
    public double FrontWidth { get; set; }
    public bool Ok => Errors.Count == 0 && Edges.Count >= 3;
    public double Area => Ok ? new Ring(Edges).Area : 0;
    public double Perimeter => Edges.Sum(e => e.Length);
}

/// <summary>
/// Gerador de formatos usuais de lotes a partir das medidas de cada lado.
/// Convenção: quem está na rua olhando o lote vê a frente embaixo; a lateral direita fica à direita.
/// </summary>
public static class LotShapes
{
    private static ShapeParam N(string key, string label, double def, string hint = "") => new() { Key = key, Label = label, Default = def, Hint = hint };

    public static IReadOnlyList<LotShapeDef> All { get; } = new List<LotShapeDef>
    {
        new() { Kind = LotShapeKind.Quadrado, Nome = "Quadrado", Descricao = "Quatro lados iguais e cantos em 90°.", Tileable = true,
            Params = { N("lado", "Lado (m)", 12) } },
        new() { Kind = LotShapeKind.Retangulo, Nome = "Retângulo", Descricao = "Frente e profundidade; cantos em 90°. O formato mais comum de lote urbano.", Tileable = true,
            Params = { N("frente", "Frente (m)", 12), N("prof", "Profundidade / laterais (m)", 30) } },
        new() { Kind = LotShapeKind.Triangulo, Nome = "Triângulo", Descricao = "Pelos três lados (frente, lateral direita e lateral esquerda).",
            Params = { N("frente", "Frente (m)", 20), N("dir", "Lateral direita (m)", 25), N("esq", "Lateral esquerda (m)", 22) } },
        new() { Kind = LotShapeKind.TrapezioRetangulo, Nome = "Trapézio retângulo", Descricao = "Frente e fundos paralelos, lateral esquerda perpendicular; a lateral direita é calculada.",
            Params = { N("frente", "Frente (m)", 12), N("fundos", "Fundos (m)", 15), N("prof", "Lateral esquerda / profundidade (m)", 30) } },
        new() { Kind = LotShapeKind.Trapezio, Nome = "Trapézio", Descricao = "Frente e fundos paralelos com a profundidade; o recuo define a posição dos fundos (recuo = metade da diferença dá um trapézio simétrico).",
            Params = { N("frente", "Frente (m)", 12), N("fundos", "Fundos (m)", 8), N("prof", "Profundidade (m)", 30), N("recuo", "Recuo dos fundos à esquerda (m)", 2, "Distância, medida paralela à frente, do canto frente-esquerda até o canto fundos-esquerda.") } },
        new() { Kind = LotShapeKind.QuadrilateroAngulo, Nome = "Quadrilátero – 4 lados + ângulo", Descricao = "Os quatro lados da matrícula e o ângulo interno no canto frente/lateral esquerda (90° se não souber).",
            Params = { N("frente", "Frente (m)", 12), N("dir", "Lateral direita (m)", 30), N("fundos", "Fundos (m)", 12.5), N("esq", "Lateral esquerda (m)", 31), N("ang", "Ângulo frente/lateral esquerda (°)", 90) } },
        new() { Kind = LotShapeKind.QuadrilateroDiagonal, Nome = "Quadrilátero – 4 lados + diagonal", Descricao = "Os quatro lados e a diagonal do canto frente-esquerda ao canto fundos-direita (medição de campo).",
            Params = { N("frente", "Frente (m)", 12), N("dir", "Lateral direita (m)", 30), N("fundos", "Fundos (m)", 12.5), N("esq", "Lateral esquerda (m)", 31), N("diag", "Diagonal frente-esq. → fundos-dir. (m)", 32.6) } },
        new() { Kind = LotShapeKind.LoteL, Nome = "Lote em L", Descricao = "Retângulo com um recorte retangular no canto dos fundos.",
            Params = { N("frente", "Frente (m)", 20), N("prof", "Profundidade (m)", 30), N("rlarg", "Recorte – largura (m)", 8), N("rprof", "Recorte – profundidade (m)", 12),
                new ShapeParam { Key = "lado", Label = "Recorte no canto", Choices = new[] { "Fundos à direita", "Fundos à esquerda" } } } },
        new() { Kind = LotShapeKind.EsquinaChanfro, Nome = "Esquina com chanfro", Descricao = "Lote de esquina com o canto cortado em reta (chanfro).",
            Params = { N("frente", "Frente (m)", 15), N("prof", "Profundidade (m)", 30), N("cf", "Chanfro na frente (m)", 3), N("cl", "Chanfro na lateral (m)", 3),
                new ShapeParam { Key = "lado", Label = "Esquina", Choices = new[] { "À direita", "À esquerda" } } } },
        new() { Kind = LotShapeKind.EsquinaRaio, Nome = "Esquina com curva", Descricao = "Lote de esquina com o canto em curva (raio de concordância do alinhamento).",
            Params = { N("frente", "Frente (m)", 15), N("prof", "Profundidade (m)", 30), N("raio", "Raio da curva (m)", 6),
                new ShapeParam { Key = "lado", Label = "Esquina", Choices = new[] { "À direita", "À esquerda" } } } },
        new() { Kind = LotShapeKind.Poligono, Nome = "Polígono (lados e ângulos)", Descricao = "Qualquer formato: os lados em sequência, começando pela frente e seguindo pela lateral direita, e o ângulo interno no fim de cada lado.",
            Params = {
                new ShapeParam { Key = "lados", Label = "Lados (m), separados por ;", IsText = true, DefaultText = "12; 30; 12; 30", Hint = "Ex.: 12; 30; 5; 4; 7; 26" },
                new ShapeParam { Key = "angs", Label = "Ângulos internos (°) no fim de cada lado", IsText = true, DefaultText = "90; 90; 90; 90", Hint = "Ex.: 90; 90; 90; 270; 90; 90 (270 = canto reentrante). Aceita 89°30'15\"." } } },
    };

    public static LotShapeDef Def(LotShapeKind k) => All.First(d => d.Kind == k);

    /// <summary>Monta o formato com os valores informados (chave → valor) e textos (lados/ângulos).</summary>
    public static LotShapeResult Build(LotShapeKind kind, IReadOnlyDictionary<string, double> v, IReadOnlyDictionary<string, string>? text = null)
    {
        var r = new LotShapeResult();
        double V(string k) => v.TryGetValue(k, out double x) ? x : Def(kind).Params.First(p => p.Key == k).Default;
        bool Pos(params string[] keys)
        {
            foreach (var k in keys)
                if (!(V(k) > 0)) { r.Errors.Add($"{Def(kind).Params.First(p => p.Key == k).Label.Replace(" (m)", "")} deve ser maior que zero."); }
            return r.Errors.Count == 0;
        }
        void Poly(params (Vec2 p, string name)[] pts)
        {
            for (int i = 0; i < pts.Length; i++)
            {
                r.Edges.Add(new LineCurve(pts[i].p, pts[(i + 1) % pts.Length].p));
                r.SideNames.Add(pts[i].name);
            }
        }
        switch (kind)
        {
            case LotShapeKind.Quadrado:
            {
                if (!Pos("lado")) break;
                double l = V("lado");
                Poly((new(0, 0), "Frente"), (new(l, 0), "Lateral direita"), (new(l, l), "Fundos"), (new(0, l), "Lateral esquerda"));
                r.FrontWidth = l;
                break;
            }
            case LotShapeKind.Retangulo:
            {
                if (!Pos("frente", "prof")) break;
                double f = V("frente"), p = V("prof");
                Poly((new(0, 0), "Frente"), (new(f, 0), "Lateral direita"), (new(f, p), "Fundos"), (new(0, p), "Lateral esquerda"));
                r.FrontWidth = f;
                break;
            }
            case LotShapeKind.Triangulo:
            {
                if (!Pos("frente", "dir", "esq")) break;
                double a = V("frente"), b = V("dir"), c = V("esq");
                if (a >= b + c || b >= a + c || c >= a + b) { r.Errors.Add("Os três lados não formam um triângulo (cada lado deve ser menor que a soma dos outros dois)."); break; }
                double x = (c * c - b * b + a * a) / (2 * a);
                double y = Math.Sqrt(Math.Max(0, c * c - x * x));
                Poly((new(0, 0), "Frente"), (new(a, 0), "Lateral direita"), (new(x, y), "Lateral esquerda"));
                r.FrontWidth = a;
                break;
            }
            case LotShapeKind.TrapezioRetangulo:
            {
                if (!Pos("frente", "fundos", "prof")) break;
                double f = V("frente"), t = V("fundos"), p = V("prof");
                Poly((new(0, 0), "Frente"), (new(f, 0), "Lateral direita"), (new(t, p), "Fundos"), (new(0, p), "Lateral esquerda"));
                r.FrontWidth = f;
                break;
            }
            case LotShapeKind.Trapezio:
            {
                if (!Pos("frente", "fundos", "prof")) break;
                double f = V("frente"), t = V("fundos"), p = V("prof"), d = V("recuo");
                Poly((new(0, 0), "Frente"), (new(f, 0), "Lateral direita"), (new(d + t, p), "Fundos"), (new(d, p), "Lateral esquerda"));
                r.FrontWidth = f;
                break;
            }
            case LotShapeKind.QuadrilateroAngulo:
            {
                if (!Pos("frente", "dir", "fundos", "esq")) break;
                double a = V("frente"), b = V("dir"), c = V("fundos"), d = V("esq"), ang = V("ang");
                if (!(ang > 0 && ang < 180)) { r.Errors.Add("O ângulo deve estar entre 0° e 180°."); break; }
                var A = new Vec2(0, 0); var B = new Vec2(a, 0);
                var D = Vec2.FromAngle(ang * Math.PI / 180) * d;
                var Cs = CircleIntersections(B, b, D, c);
                if (Cs.Count == 0) { r.Errors.Add("Com esse ângulo os lados não fecham: confira as medidas ou o ângulo."); break; }
                // Solução que deixa o quadrilátero simples e convexo (mais longe da frente).
                var C = Cs.OrderByDescending(p => (p - A).Cross(B - A) < 0 ? p.Y : double.NegativeInfinity).First();
                Poly((A, "Frente"), (B, "Lateral direita"), (C, "Fundos"), (D, "Lateral esquerda"));
                if (!Simple(r.Edges)) { r.Edges.Clear(); r.SideNames.Clear(); r.Errors.Add("As medidas geram um contorno que se cruza: confira os lados e o ângulo."); break; }
                r.FrontWidth = a;
                break;
            }
            case LotShapeKind.QuadrilateroDiagonal:
            {
                if (!Pos("frente", "dir", "fundos", "esq", "diag")) break;
                double a = V("frente"), b = V("dir"), c = V("fundos"), d = V("esq"), e = V("diag");
                if (a >= b + e || b >= a + e || e >= a + b) { r.Errors.Add("Frente, lateral direita e diagonal não formam um triângulo."); break; }
                if (c >= d + e || d >= c + e || e >= c + d) { r.Errors.Add("Fundos, lateral esquerda e diagonal não formam um triângulo."); break; }
                var A = new Vec2(0, 0); var B = new Vec2(a, 0);
                double cx = (e * e - b * b + a * a) / (2 * a);
                var C = new Vec2(cx, Math.Sqrt(Math.Max(0, e * e - cx * cx)));
                // D do outro lado da diagonal AC em relação a B.
                var u = C.Normalized();
                double dx = (d * d - c * c + e * e) / (2 * e);
                double dy = Math.Sqrt(Math.Max(0, d * d - dx * dx));
                var D = u * dx + u.PerpLeft * dy;
                Poly((A, "Frente"), (B, "Lateral direita"), (C, "Fundos"), (D, "Lateral esquerda"));
                if (!Simple(r.Edges)) { r.Edges.Clear(); r.SideNames.Clear(); r.Errors.Add("As medidas geram um contorno que se cruza: confira os lados e a diagonal."); break; }
                r.FrontWidth = a;
                break;
            }
            case LotShapeKind.LoteL:
            {
                if (!Pos("frente", "prof", "rlarg", "rprof")) break;
                double f = V("frente"), p = V("prof"), w = V("rlarg"), q = V("rprof");
                if (w >= f) { r.Errors.Add("A largura do recorte deve ser menor que a frente."); break; }
                if (q >= p) { r.Errors.Add("A profundidade do recorte deve ser menor que a profundidade do lote."); break; }
                if (V("lado") < 0.5)
                    Poly((new(0, 0), "Frente"), (new(f, 0), "Lateral direita"), (new(f, p - q), "Divisa do recorte"), (new(f - w, p - q), "Divisa do recorte"),
                        (new(f - w, p), "Fundos"), (new(0, p), "Lateral esquerda"));
                else
                    Poly((new(0, 0), "Frente"), (new(f, 0), "Lateral direita"), (new(f, p), "Fundos"), (new(w, p), "Divisa do recorte"),
                        (new(w, p - q), "Divisa do recorte"), (new(0, p - q), "Lateral esquerda"));
                r.FrontWidth = f;
                break;
            }
            case LotShapeKind.EsquinaChanfro:
            {
                if (!Pos("frente", "prof", "cf", "cl")) break;
                double f = V("frente"), p = V("prof"), cf = V("cf"), cl = V("cl");
                if (cf >= f || cl >= p) { r.Errors.Add("O chanfro deve ser menor que a frente e que a profundidade."); break; }
                if (V("lado") < 0.5)
                    Poly((new(0, 0), "Frente"), (new(f - cf, 0), "Chanfro"), (new(f, cl), "Lateral direita"), (new(f, p), "Fundos"), (new(0, p), "Lateral esquerda"));
                else
                    Poly((new(cf, 0), "Frente"), (new(f, 0), "Lateral direita"), (new(f, p), "Fundos"), (new(0, p), "Lateral esquerda"), (new(0, cl), "Chanfro"));
                r.FrontWidth = f;
                break;
            }
            case LotShapeKind.EsquinaRaio:
            {
                if (!Pos("frente", "prof", "raio")) break;
                double f = V("frente"), p = V("prof"), rr = V("raio");
                if (rr >= f || rr >= p) { r.Errors.Add("O raio deve ser menor que a frente e que a profundidade."); break; }
                if (V("lado") < 0.5)
                {
                    r.Edges.Add(new LineCurve(new(0, 0), new(f - rr, 0))); r.SideNames.Add("Frente");
                    r.Edges.Add(new ArcCurve(new(f - rr, rr), rr, -Math.PI / 2, Math.PI / 2)); r.SideNames.Add("Curva da esquina");
                    r.Edges.Add(new LineCurve(new(f, rr), new(f, p))); r.SideNames.Add("Lateral direita");
                    r.Edges.Add(new LineCurve(new(f, p), new(0, p))); r.SideNames.Add("Fundos");
                    r.Edges.Add(new LineCurve(new(0, p), new(0, 0))); r.SideNames.Add("Lateral esquerda");
                }
                else
                {
                    r.Edges.Add(new LineCurve(new(rr, 0), new(f, 0))); r.SideNames.Add("Frente");
                    r.Edges.Add(new LineCurve(new(f, 0), new(f, p))); r.SideNames.Add("Lateral direita");
                    r.Edges.Add(new LineCurve(new(f, p), new(0, p))); r.SideNames.Add("Fundos");
                    r.Edges.Add(new LineCurve(new(0, p), new(0, rr))); r.SideNames.Add("Lateral esquerda");
                    r.Edges.Add(new ArcCurve(new(rr, rr), rr, Math.PI, Math.PI / 2)); r.SideNames.Add("Curva da esquina");
                }
                r.FrontWidth = f;
                break;
            }
            case LotShapeKind.Poligono:
            {
                string ls = text is not null && text.TryGetValue("lados", out var t1) ? t1 : Def(kind).Params[0].DefaultText;
                string an = text is not null && text.TryGetValue("angs", out var t2) ? t2 : Def(kind).Params[1].DefaultText;
                var sides = new List<double>();
                foreach (var part in Split(ls))
                {
                    if (!AngleText.TryParseNumber(part, out double x) || x <= 0) { r.Errors.Add($"Lado inválido: \"{part}\"."); break; }
                    sides.Add(x);
                }
                var angs = new List<double>();
                foreach (var part in Split(an))
                {
                    if (!AngleText.TryParseAngle(part, out double x) || x <= 0 || x >= 360) { r.Errors.Add($"Ângulo inválido: \"{part}\"."); break; }
                    angs.Add(x);
                }
                if (r.Errors.Count > 0) break;
                if (sides.Count < 3) { r.Errors.Add("Informe pelo menos 3 lados."); break; }
                if (angs.Count < sides.Count - 1) { r.Errors.Add($"Informe o ângulo interno no fim de cada lado ({sides.Count - 1} ou {sides.Count} ângulos)."); break; }
                var pts = new List<Vec2> { new(0, 0) };
                double heading = 0;
                for (int i = 0; i < sides.Count; i++)
                {
                    pts.Add(pts[^1] + Vec2.FromAngle(heading) * sides[i]);
                    if (i < angs.Count) heading += Math.PI - angs[i] * Math.PI / 180; // giro à esquerda (interior à esquerda)
                }
                double mis = pts[^1].Length;
                double perim = sides.Sum();
                if (mis > Math.Max(0.05, perim * 0.002))
                    r.Errors.Add($"O polígono não fecha: o último lado termina a {AngleText.FormatNumber(mis, 3, true)} m do início. Confira os lados e os ângulos (a soma dos ângulos internos de {sides.Count} lados é {(sides.Count - 2) * 180}°).");
                else if (mis > 0.001)
                    r.Warnings.Add($"Erro de fechamento de {AngleText.FormatNumber(mis, 3, true)} m (absorvido no último lado).");
                if (r.Errors.Count > 0) break;
                pts.RemoveAt(pts.Count - 1);
                var names = new List<string> { "Frente" };
                for (int i = 1; i < pts.Count; i++) names.Add($"Lado {i + 1}");
                Poly(pts.Select((p, i) => (p, names[i])).ToArray());
                if (new Ring(r.Edges).Area < 0) { r.Errors.Add("Os ângulos descrevem o contorno no sentido contrário: use os ângulos internos (a soma deve ser (n−2)×180°)."); r.Edges.Clear(); r.SideNames.Clear(); break; }
                if (!Simple(r.Edges)) { r.Edges.Clear(); r.SideNames.Clear(); r.Errors.Add("O contorno se cruza: confira os lados e os ângulos."); break; }
                r.FrontWidth = sides[0];
                break;
            }
        }
        if (r.Ok && r.Area <= 1e-6) r.Errors.Add("Área nula: confira as medidas.");
        return r;
    }

    private static IEnumerable<string> Split(string s) =>
        s.Split(new[] { ';', '/', '\n', '\r', '|' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0);

    private static List<Vec2> CircleIntersections(Vec2 c0, double r0, Vec2 c1, double r1)
    {
        var res = new List<Vec2>();
        double d = c0.DistanceTo(c1);
        if (d < 1e-12 || d > r0 + r1 + 1e-9 || d < Math.Abs(r0 - r1) - 1e-9) return res;
        double a = (r0 * r0 - r1 * r1 + d * d) / (2 * d);
        double h = Math.Sqrt(Math.Max(0, r0 * r0 - a * a));
        var u = (c1 - c0) / d;
        var m = c0 + u * a;
        res.Add(m + u.PerpLeft * h);
        if (h > 1e-9) res.Add(m - u.PerpLeft * h);
        return res;
    }

    /// <summary>Contorno sem autointerseções (lados não consecutivos não se cruzam).</summary>
    private static bool Simple(List<Curve2> edges)
    {
        int n = edges.Count;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                if (j == i + 1 || (i == 0 && j == n - 1)) continue;
                if (SegCross(edges[i].Start, edges[i].End, edges[j].Start, edges[j].End)) return false;
            }
        return new Ring(edges).Area > 0;
    }

    private static bool SegCross(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
    {
        double d1 = (b - a).Cross(c - a), d2 = (b - a).Cross(d - a), d3 = (d - c).Cross(a - c), d4 = (d - c).Cross(b - c);
        return d1 * d2 < -1e-12 && d3 * d4 < -1e-12;
    }

    /// <summary>Posiciona o formato no desenho: canto frente-esquerda em <paramref name="origin"/>, frente na direção <paramref name="angleRad"/>.</summary>
    public static List<Curve2> Place(IEnumerable<Curve2> local, Vec2 origin, double angleRad, double offsetAlongFront = 0)
    {
        var res = new List<Curve2>();
        Vec2 T(Vec2 p) => origin + new Vec2(p.X + offsetAlongFront, p.Y).Rotate(angleRad);
        foreach (var c in local)
        {
            if (c is ArcCurve arc)
                res.Add(new ArcCurve(T(arc.Center), arc.Radius, arc.StartAngle + angleRad, arc.Sweep));
            else res.Add(new LineCurve(T(c.Start), T(c.End)));
        }
        return res;
    }
}
