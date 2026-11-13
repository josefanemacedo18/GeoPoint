using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;
using GeoPoint.Core.Settings;
using GeoPoint.Core.Urban;

namespace GeoPoint.Core.Calc;

public sealed class VertexReport
{
    public string Name { get; set; } = "";
    public Vec2 Internal { get; init; }
    public Vec2 Enu { get; init; }
    public Vec2 Shared { get; init; }
    public GeodeticCoord? Geo { get; init; }
    public CrsCoord? Utm { get; init; }
    public double? Elevation { get; set; }
}

public sealed class SegmentReport
{
    public int Index { get; init; }
    public VertexReport From { get; init; } = null!;
    public VertexReport To { get; init; } = null!;
    public Curve2 Curve { get; init; } = null!;
    public bool IsArc => Curve.IsArc;

    /// <summary>Comprimento horizontal (desenvolvimento, para arcos) – plano topográfico local.</summary>
    public double LengthHorizontal { get; init; }
    /// <summary>Corda horizontal.</summary>
    public double ChordHorizontal { get; init; }
    public double? LengthUtm { get; init; }
    public double? ChordUtm { get; init; }
    public double? LengthGeodesic { get; init; }
    public double? ChordGeodesic { get; init; }

    /// <summary>Azimute verdadeiro da corda (graus).</summary>
    public double AzimuthTrue { get; init; }
    public double? AzimuthGrid { get; init; }
    public double? AzimuthGeodesic { get; init; }

    public double? Radius { get; init; }
    public double? CentralAngleDeg { get; init; }
    /// <summary>Sentido do arco no percurso (true = horário).</summary>
    public bool ArcClockwise { get; init; }

    public string Confrontante { get; set; } = "";
    public bool ConfrontanteAutomatico { get; set; }
    public ConfrontanteTipo ConfrontanteTipo { get; set; }
    public string ConfrontanteLotId { get; set; } = "";
    public string ConfrontanteViaId { get; set; } = "";
    /// <summary>Lado do lote (frente, fundos, laterais) – definido pelo usuário ou calculado pela testada.</summary>
    public LadoLote Lado { get; set; } = LadoLote.Automatico;
    public bool LadoAutomatico { get; set; } = true;
    public bool Testada { get; set; }
    public double? Recuo { get; set; }
    public EdgeInfo? Info { get; set; }
    /// <summary>Normal externa da divisa (no desenho).</summary>
    public Vec2 OutwardNormal { get; set; }

    public double Azimuth(AzimuthType t) => t switch
    {
        AzimuthType.GridUtm => AzimuthGrid ?? AzimuthTrue,
        AzimuthType.Geodesic => AzimuthGeodesic ?? AzimuthTrue,
        _ => AzimuthTrue,
    };

    public double Length(DistanceType t) => t switch
    {
        DistanceType.GridUtm => LengthUtm ?? LengthHorizontal,
        DistanceType.Geodesic => LengthGeodesic ?? LengthHorizontal,
        _ => LengthHorizontal,
    };

    public double Chord(DistanceType t) => t switch
    {
        DistanceType.GridUtm => ChordUtm ?? ChordHorizontal,
        DistanceType.Geodesic => ChordGeodesic ?? ChordHorizontal,
        _ => ChordHorizontal,
    };
}

public sealed class LotReport
{
    public LotRecord Lot { get; init; } = null!;
    public Region Region { get; init; } = null!;
    public bool Clockwise { get; init; }
    public List<VertexReport> Vertices { get; } = new();
    public List<SegmentReport> Segments { get; } = new();

    public double AreaLocal { get; init; }
    public double? AreaUtm { get; set; }
    public double PerimeterLocal { get; set; }
    public double? PerimeterUtm { get; set; }
    public double? PerimeterGeodesic { get; set; }
    public double Testada { get; set; }

    public double AreaDeducoes { get; set; }
    public List<(LotRecord Lot, double Area)> Deducoes { get; } = new();
    public double AreaLiquida => AreaLocal - AreaDeducoes;

    public UtmZone? Zone { get; set; }
    public double? MeanScale { get; set; }
    public double? MeanConvergenceDeg { get; set; }

    public List<string> Warnings { get; } = new();

    public double Area(AreaType t) => t == AreaType.Utm ? AreaUtm ?? AreaLocal : AreaLocal;
    public double Perimeter(DistanceType t) => t switch
    {
        DistanceType.GridUtm => PerimeterUtm ?? PerimeterLocal,
        DistanceType.Geodesic => PerimeterGeodesic ?? PerimeterLocal,
        _ => PerimeterLocal,
    };
}

/// <summary>Contexto de cálculo de um projeto: referência, configurações, vizinhanças e descontos.</summary>
public sealed class ProjectContext
{
    private readonly Dictionary<string, LotReport> _cache = new();
    private Func<Vec2, VertexRecord?>? _vertexFinder;
    private List<(LotRecord lot, Region region, BBox bb)>? _active;
    private List<(LotRecord lot, Region region, BBox bb)>? _archived;
    private readonly Dictionary<(string, string), bool> _overlaps = new();

    public ProjectContext(ParcelProject project, GeoPointSettings settings)
    {
        Project = project;
        Settings = settings;
        Coords = new CoordinateService(project.Reference, project.Frame);
    }

    public ParcelProject Project { get; }
    public GeoPointSettings Settings { get; }
    public CoordinateService Coords { get; }

    public RuleProfile? Rules => Settings.FindProfile(Project.Info.RuleProfileName);

    /// <summary>Texto do confrontante quando ele é um lote do projeto (pelo proprietário, conforme as Configurações).</summary>
    public string ConfrontanteName(LotRecord nb)
    {
        string owner = Project.ConfrontanteOwner(nb);
        return Settings.Naming.ConfrontanteNome switch
        {
            ConfrontanteNome.Lote => nb.DisplayName,
            ConfrontanteNome.LoteEProprietario => owner.Length > 0 ? $"{nb.DisplayName} ({owner})" : nb.DisplayName,
            _ => owner.Length > 0 ? owner : nb.DisplayName,
        };
    }

    public void Invalidate()
    {
        _cache.Clear();
        _vertexFinder = null;
        _active = null;
        _archived = null;
        _overlaps.Clear();
    }

    public static IEnumerable<LotRecord> SortLots(IEnumerable<LotRecord> lots) =>
        lots.OrderBy(l => l.Status)
            .ThenBy(l => l.Quadra, NaturalComparer.Instance)
            .ThenBy(l => l.Numero, NaturalComparer.Instance)
            .ThenBy(l => l.Nome, NaturalComparer.Instance);

    private List<(LotRecord lot, Region region, BBox bb)> Active() => _active ??= Load(Project.ActiveLots);

    private List<(LotRecord lot, Region region, BBox bb)> Archived() =>
        _archived ??= Load(Project.PhaseLots.Where(l => l.Status == LotStatus.Arquivado));

    private static List<(LotRecord lot, Region region, BBox bb)> Load(IEnumerable<LotRecord> lots)
    {
        var list = new List<(LotRecord, Region, BBox)>();
        foreach (var l in lots)
        {
            try
            {
                var r = l.ToRegion();
                list.Add((l, r, r.Bounds));
            }
            catch { /* geometria inválida é reportada pela validação */ }
        }
        return list;
    }

    /// <summary>Verdadeiro se as áreas dos dois lotes se sobrepõem (não são apenas vizinhas).</summary>
    private bool Overlaps(LotRecord a, Region ra, LotRecord b, Region rb)
    {
        var key = string.CompareOrdinal(a.Id, b.Id) < 0 ? (a.Id, b.Id) : (b.Id, a.Id);
        if (_overlaps.TryGetValue(key, out bool v)) return v;
        double ov = 0;
        try { ov = Overlay.IntersectionArea(ra, rb); } catch { }
        v = ov > 0.01;
        _overlaps[key] = v;
        return v;
    }

    /// <summary>
    /// Lote vizinho que contém a divisa (ponto médio sobre o contorno do outro lote) sem sobrepor a área.
    /// Lotes ativos são comparados com ativos; arquivados (situação anterior a uma operação) com arquivados.
    /// </summary>
    public LotRecord? NeighborAt(LotRecord lot, Vec2 midpoint)
    {
        var candidates = lot.Status == LotStatus.Arquivado ? Archived() : Active();
        Region? own = null;
        foreach (var (other, region, bb) in candidates)
        {
            if (other.Id == lot.Id) continue;
            if (LotUseInfo.IsDeduction(other.Uso) != LotUseInfo.IsDeduction(lot.Uso)) continue;
            // A gleba contém as áreas do parcelamento: não é confrontante delas (nem elas da gleba).
            if ((other.Uso == LotUse.Gleba) != (lot.Uso == LotUse.Gleba)) continue;
            if (!bb.Expand(0.01).Contains(midpoint)) continue;
            bool touches = region.AllRings().Any(ring => ring.Edges.Any(e => e.DistanceTo(midpoint) <= 0.002));
            if (!touches) continue;
            try { own ??= lot.ToRegion(); } catch { return null; }
            if (Overlaps(lot, own, other, region)) continue;
            return other;
        }
        return null;
    }

    /// <summary>Nome do vértice conforme a numeração escolhida (projeto, quadra ou lote).</summary>
    public string VertexName(LotRecord lot, Vec2 p, int indexInLot)
    {
        var n = Settings.Naming;
        string Fmt(int k) => n.VertexPrefix + k.ToString(new string('0', Math.Max(1, n.VertexDigits)));
        if (n.Numbering == VertexNumbering.Lote) return Fmt(indexInLot + 1);
        if (_vertexFinder is null)
        {
            VertexRegistry.EnsureAll(this);
            _vertexFinder = VertexRegistry.Finder(Project);
        }
        var rec = _vertexFinder(p);
        if (rec is null)
        {
            VertexRegistry.Register(this, new[] { lot });
            _vertexFinder = VertexRegistry.Finder(Project);
            rec = _vertexFinder(p);
        }
        if (rec is null) return Fmt(indexInLot + 1);
        if (n.Numbering == VertexNumbering.Quadra && rec.QuadraNumbers.TryGetValue((lot.Quadra ?? "").Trim(), out int qn)) return Fmt(qn);
        return Fmt(rec.Number);
    }

    /// <summary>Anel externo orientado no sentido de descrição e iniciado no vértice inicial.</summary>
    public Ring OrientedRing(LotRecord lot, out bool clockwise)
    {
        var region = lot.ToRegion();
        clockwise = lot.Clockwise ?? Settings.Naming.Clockwise;
        var ring = clockwise ? region.Outer.Reversed() : region.Outer;
        int start = StartIndex(lot, ring);
        return ring.RotateStart(start);
    }

    private int StartIndex(LotRecord lot, Ring ring)
    {
        int n = ring.Count;
        if (lot.StartX is double sx && lot.StartY is double sy)
        {
            var target = new Vec2(sx, sy);
            int bestI = 0; double bestD = double.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                double d = ring.Edges[i].Start.DistanceTo(target);
                if (d < bestD) { bestD = d; bestI = i; }
            }
            if (bestD <= 0.01) return bestI;
        }

        var enu = ring.Edges.Select(e => Coords.ToEnu(e.Start)).ToList();
        int north = 0;
        for (int i = 1; i < n; i++)
        {
            double dn = enu[i].Y - enu[north].Y;
            if (dn > 1e-6 || (Math.Abs(dn) <= 1e-6 && enu[i].X < enu[north].X)) north = i;
        }

        switch (Settings.Naming.StartRule)
        {
            case StartVertexRule.MaisANoroeste:
            {
                int best = 0; double bestScore = double.NegativeInfinity;
                for (int i = 0; i < n; i++)
                {
                    double s = enu[i].Y - enu[i].X;
                    if (s > bestScore + 1e-9) { bestScore = s; best = i; }
                }
                return best;
            }
            case StartVertexRule.Testada:
            {
                var testada = new bool[n];
                bool any = false;
                for (int i = 0; i < n; i++)
                {
                    var info = FindInfo(lot, ring.Edges[i].Mid);
                    testada[i] = info?.Testada == true;
                    any |= testada[i];
                }
                if (!any) return north;
                // Início de uma sequência de testada, procurando a partir do vértice mais ao norte.
                for (int k = 0; k < n; k++)
                {
                    int i = (north + k) % n;
                    int prev = (i - 1 + n) % n;
                    if (testada[i] && !testada[prev]) return i;
                }
                return north;
            }
            default:
                return north;
        }
    }

    public static EdgeInfo? FindInfo(LotRecord lot, Vec2 mid)
    {
        EdgeInfo? best = null;
        double bestD = 0.01;
        foreach (var info in lot.EdgeInfos)
        {
            double d = new Vec2(info.MX, info.MY).DistanceTo(mid);
            if (d <= bestD) { bestD = d; best = info; }
        }
        return best;
    }

    public LotReport Report(LotRecord lot)
    {
        if (_cache.TryGetValue(lot.Id, out var cached)) return cached;
        var rep = BuildReport(lot);
        _cache[lot.Id] = rep;
        return rep;
    }

    private LotReport BuildReport(LotRecord lot)
    {
        var region = lot.ToRegion();
        var ring = OrientedRing(lot, out bool clockwise);
        var fmt = Settings.Naming;
        var c = Coords;
        bool geo = c.HasGeoreference;
        UtmZone? zone = geo ? c.ResolveZone(CrsDef.Utm()) : null;

        var rep = new LotReport { Lot = lot, Region = region, Clockwise = clockwise, AreaLocal = region.Area, Zone = zone };

        var utmDef = CrsDef.Utm();
        for (int i = 0; i < ring.Count; i++)
        {
            var p = ring.Edges[i].Start;
            var g = c.ToGeodetic(p);
            CrsCoord? utm = geo ? c.ToCrs(p, utmDef) : null;
            var v = new VertexReport
            {
                Name = VertexName(lot, p, i),
                Internal = p,
                Enu = c.ToEnu(p),
                Shared = c.ToShared(p),
                Geo = g,
                Utm = utm,
            };
            if (lot.VertexElevations.TryGetValue(LotRecord.VertexKey(p), out double z)) v.Elevation = z;
            rep.Vertices.Add(v);
        }

        double sumScale = 0, sumConv = 0; int nScale = 0;
        foreach (var v in rep.Vertices)
            if (v.Utm is { } u) { sumScale += u.Scale; sumConv += u.ConvergenceDeg; nScale++; }
        if (nScale > 0) { rep.MeanScale = sumScale / nScale; rep.MeanConvergenceDeg = sumConv / nScale; }

        double perim = 0, perimUtm = 0, perimGeo = 0, testada = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            var curve = ring.Edges[i];
            var from = rep.Vertices[i];
            var to = rep.Vertices[(i + 1) % ring.Count];
            double len = curve.Length;
            double chord = curve.Start.DistanceTo(curve.End);
            double az = c.TrueAzimuth(curve.Start, curve.End);

            double? chordUtm = null, lenUtm = null, azGrid = null, chordGeo = null, lenGeo = null, azGeo = null;
            if (from.Utm is { } u1 && to.Utm is { } u2)
            {
                double de = u2.East - u1.East, dn = u2.North - u1.North;
                chordUtm = Math.Sqrt(de * de + dn * dn);
                azGrid = CoordinateService.AzimuthOfEnuVector(new Vec2(de, dn));
                if (curve.IsArc)
                {
                    var mid = c.ToCrs(curve.Mid, utmDef);
                    double kMean = (u1.Scale + u2.Scale + (mid?.Scale ?? u1.Scale) * 4) / 6.0;
                    double heightFactor = HeightFactor(c);
                    lenUtm = len * kMean * heightFactor;
                }
                else lenUtm = chordUtm;
            }
            if (from.Geo is { } g1 && to.Geo is { } g2)
            {
                var inv = Geodesic.Inverse(g1, g2, Ellipsoid.GRS80);
                chordGeo = inv.Distance;
                azGeo = inv.Azimuth1Deg;
                lenGeo = curve.IsArc && chord > 0 ? len * inv.Distance / chord : inv.Distance;
            }

            double? radius = null, central = null;
            bool arcCw = false;
            if (curve is ArcCurve arc)
            {
                radius = arc.Radius;
                central = Math.Abs(arc.Sweep) * 180 / Math.PI;
                // Rotação preserva o sentido; sweep negativo = horário.
                arcCw = arc.Sweep < 0;
            }

            var info = FindInfo(lot, curve.Mid);
            var seg = new SegmentReport
            {
                Index = i, From = from, To = to, Curve = curve,
                LengthHorizontal = len, ChordHorizontal = chord,
                LengthUtm = lenUtm, ChordUtm = chordUtm, LengthGeodesic = lenGeo, ChordGeodesic = chordGeo,
                AzimuthTrue = az, AzimuthGrid = azGrid, AzimuthGeodesic = azGeo,
                Radius = radius, CentralAngleDeg = central, ArcClockwise = arcCw,
                Info = info,
                Testada = info?.Testada == true,
                Recuo = info?.Recuo,
                OutwardNormal = OutwardNormal(curve, clockwise),
            };
            ResolveConfrontante(lot, seg, info);
            rep.Segments.Add(seg);
            perim += len;
            perimUtm += lenUtm ?? len;
            perimGeo += lenGeo ?? len;
            if (seg.Testada) testada += len;
        }
        AssignSides(rep);
        rep.PerimeterLocal = perim;
        if (geo)
        {
            rep.PerimeterUtm = perimUtm;
            rep.PerimeterGeodesic = perimGeo;
            rep.AreaUtm = UtmArea(region, utmDef);
        }
        rep.Testada = testada;

        // Descontos (APP, servidões etc. sobrepostos ao lote).
        if (!LotUseInfo.IsDeduction(lot.Uso))
        {
            foreach (var (other, oregion, bb) in Active())
            {
                if (other.Id == lot.Id || !LotUseInfo.IsDeduction(other.Uso)) continue;
                if (!bb.Intersects(region.Bounds)) continue;
                double a = Overlay.IntersectionArea(region, oregion);
                if (a > 1e-4)
                {
                    rep.Deducoes.Add((other, a));
                    rep.AreaDeducoes += a;
                }
            }
        }

        if (!geo) rep.Warnings.Add("Projeto sem ponto de referência: coordenadas geográficas/UTM indisponíveis.");
        if (rep.Segments.Any(s => string.IsNullOrWhiteSpace(s.Confrontante))) rep.Warnings.Add("Há divisas sem confrontante informado.");
        return rep;
    }

    /// <summary>Normal externa (para fora do lote) da divisa, pela corda.</summary>
    private static Vec2 OutwardNormal(Curve2 c, bool clockwise)
    {
        var d = (c.End - c.Start).Normalized();
        if (d.LengthSquared == 0) d = c.TangentAt(0.5);
        // Anel anti-horário: interior à esquerda → externo à direita. Horário: o contrário.
        return clockwise ? d.PerpLeft : d.PerpRight;
    }

    /// <summary>Resolve o texto do confrontante conforme o tipo (texto, lote ou via) ou sugere o vizinho.</summary>
    private void ResolveConfrontante(LotRecord lot, SegmentReport seg, EdgeInfo? info)
    {
        var mid = seg.Curve.Mid;
        if (info is not null)
        {
            seg.ConfrontanteTipo = info.Tipo;
            switch (info.Tipo)
            {
                case ConfrontanteTipo.Via:
                {
                    var via = Project.FindVia(info.ViaId);
                    seg.ConfrontanteViaId = info.ViaId;
                    seg.Confrontante = via?.Nome ?? info.Confrontante;
                    if (!string.IsNullOrWhiteSpace(seg.Confrontante)) return;
                    break;
                }
                case ConfrontanteTipo.Lote:
                {
                    var target = Project.FindLot(info.LotId);
                    seg.ConfrontanteLotId = info.LotId;
                    // Lote vizinho arquivado por uma operação: usa o lote que ocupa a divisa hoje.
                    if (target is not null && target.Status == LotStatus.Arquivado && lot.Status == LotStatus.Ativo)
                    {
                        var now = NeighborAt(lot, mid);
                        if (now is not null) { seg.Confrontante = ConfrontanteName(now); seg.ConfrontanteLotId = now.Id; return; }
                    }
                    if (target is not null) { seg.Confrontante = ConfrontanteName(target); return; }
                    if (!string.IsNullOrWhiteSpace(info.Confrontante)) { seg.Confrontante = info.Confrontante; return; }
                    break;
                }
                default:
                    if (!string.IsNullOrWhiteSpace(info.Confrontante)) { seg.Confrontante = info.Confrontante.Trim(); return; }
                    break;
            }
        }
        var nb = NeighborAt(lot, mid);
        if (nb is not null)
        {
            seg.Confrontante = ConfrontanteName(nb);
            seg.ConfrontanteAutomatico = true;
            seg.ConfrontanteTipo = ConfrontanteTipo.Lote;
            seg.ConfrontanteLotId = nb.Id;
        }
    }

    /// <summary>
    /// Lados do lote: frente pelas testadas; fundos, lateral direita e esquerda vistos da via, de frente para o lote.
    /// Lados definidos pelo usuário prevalecem.
    /// </summary>
    private static void AssignSides(LotReport rep)
    {
        var fronts = rep.Segments.Where(s => s.Testada).ToList();
        Vec2? nf = fronts.Count > 0 ? fronts.OrderByDescending(s => s.LengthHorizontal).First().OutwardNormal : null;
        foreach (var s in rep.Segments)
        {
            if (s.Info is { Lado: not LadoLote.Automatico } info)
            {
                s.Lado = info.Lado;
                s.LadoAutomatico = false;
                continue;
            }
            s.LadoAutomatico = true;
            if (s.Testada) { s.Lado = LadoLote.Frente; continue; }
            if (nf is not Vec2 f) { s.Lado = LadoLote.Automatico; continue; }
            double d = s.OutwardNormal.Dot(f);
            if (d > 0.7) s.Lado = LadoLote.Frente;
            else if (d < -0.7) s.Lado = LadoLote.Fundos;
            else
            {
                // Observador na via olhando para o lote: direção = -f; direita = rotação de -90°.
                var facing = -f;
                var right = new Vec2(facing.Y, -facing.X);
                s.Lado = s.OutwardNormal.Dot(right) > 0 ? LadoLote.LateralDireita : LadoLote.LateralEsquerda;
            }
        }
    }

    /// <summary>Fator de altitude (redução ao elipsoide) usado nos arcos em UTM.</summary>
    private static double HeightFactor(CoordinateService c)
    {
        double lat = c.Origin.LatRad;
        double r = Ellipsoid.GRS80.GaussianRadius(lat);
        return r / (r + c.Origin.Height);
    }

    private double? UtmArea(Region region, CrsDef utmDef)
    {
        double total = 0;
        bool first = true;
        foreach (var ring in region.AllRings())
        {
            var pts = ring.ToPolygon(0.005);
            var proj = new List<Vec2>(pts.Count);
            foreach (var p in pts)
            {
                var u = Coords.ToCrs(p, utmDef);
                if (u is null) return null;
                proj.Add(new Vec2(u.Value.East, u.Value.North));
            }
            double a = Math.Abs(PolygonUtil.SignedArea(proj));
            total += first ? a : -a;
            first = false;
        }
        // Correção da diferença entre a área poligonal densificada e a área exata com arcos (plano local).
        double localPoly = 0; first = true;
        foreach (var ring in region.AllRings())
        {
            double a = Math.Abs(PolygonUtil.SignedArea(ring.ToPolygon(0.005)));
            localPoly += first ? a : -a;
            first = false;
        }
        if (localPoly > 0) total *= region.Area / localPoly;
        return total;
    }
}

/// <summary>Comparador "natural" (Lote 2 antes de Lote 10).</summary>
public sealed class NaturalComparer : IComparer<string?>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        x ??= ""; y ??= "";
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                int si = i, sj = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;
                var nx = x[si..i].TrimStart('0'); var ny = y[sj..j].TrimStart('0');
                if (nx.Length != ny.Length) return nx.Length.CompareTo(ny.Length);
                int c = string.CompareOrdinal(nx, ny);
                if (c != 0) return c;
            }
            else
            {
                int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (c != 0) return c;
                i++; j++;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
