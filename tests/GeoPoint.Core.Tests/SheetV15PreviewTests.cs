using System.Globalization;
using System.Text;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Sheets;

namespace GeoPoint.Core.Tests;

/// <summary>Monta a prancha completa (modelo novo) como o Revit faria e grava um SVG (só com GEOPOINT_OUT definido).</summary>
public class SheetV15PreviewTests
{
    public static (SheetRenderInput input, List<SheetPrim> prims, AnnotationPlan plan, List<LotReport> reps, Func<Vec2, Vec2> toPaper, double k)
        Compose(ProjectContext ctx, IReadOnlyList<LotRecord> lots, SheetSpec spec, SheetTemplate tpl)
    {
        var fmt = new ValueFormatter(ctx.Settings.Format);
        var def = CrsDef.Utm();
        var reps = lots.Select(ctx.Report).ToList();
        var map = tpl.MapRect();
        var bb = BBox.Of(reps.SelectMany(r => r.Vertices.Select(v => v.Internal)));
        SheetTable? table = spec.Detailed ? SheetTable.Coordinates(reps[0], ctx, fmt, def, tpl.Styles) : lots.Count > 1 ? SheetTable.Lots(lots, ctx, fmt, tpl.Styles) : null;
        table?.FitInto(map.Width * 0.7, map.Height * 0.45);
        var fit = MapFit.Fit(map, bb.Width, bb.Height, tpl.DrawingMarginMm, table is null ? null : (table.Width, table.Height), SheetRenderer.NorthRect(tpl), tpl.TableCorner, tpl.ScaleList);
        double k = 1000.0 / fit.Scale; // mm no papel por metro
        var c = bb.Center;
        Vec2 ToPaper(Vec2 p) => fit.DrawingCenter + (p - c) * k;
        Vec2 ToModel(Vec2 q) => c + (q - fit.DrawingCenter) / k;
        // Anotações (em metros do modelo): texto de 2 mm no papel.
        double textM = 2.0 / k;
        var obstacles = new List<Obb>();
        void Obst(PaperRect r) => obstacles.Add(Obb.Of(ToModel(r.Center), 0, r.Width / 2 / k, r.Height / 2 / k));
        Obst(SheetRenderer.NorthRect(tpl));
        if (fit.Table is PaperRect tr) Obst(tr);
        foreach (var r in SheetRenderer.OutsideMap(map)) Obst(r);
        var o = new AnnotationPlanOptions
        {
            TextM = textM, OffsetM = 1.2 / k, DotRadiusM = 0.6 / k, PadM = 0.4 / k, LineFactor = 1.5, Obstacles = obstacles,
            Vertices = spec.Detailed, EdgeAngle = spec.Detailed, Dots = spec.Detailed,
            LabelOf = r => string.Join("\n", new[] { r.Lot.DisplayName, "LUCAS ARAUJO MARINHO BRASIL", "MATRÍCULA: 7607", "ÁREA = " + fmt.Area(r.AreaLocal), "PERÍMETRO = " + fmt.Distance(r.PerimeterLocal) }),
            PerLotVertexNames = lots.Count > 1,
            RefCallout = r => new RefCallout(r.Vertices[0].Internal, r.Vertices[0].Name, SheetTexts.CalloutLines(ctx.Coords, fmt, def, r.Vertices[0].Internal)),
        };
        var plan = AnnotationPlanner.Plan(reps, fmt, o);
        var content = SheetContent.Build(ctx, fmt, spec, lots, def);
        var input = new SheetRenderInput
        {
            Template = tpl, Content = content, Scale = fit.Scale, Folha = "02", SheetNumber = "GP-02", Date = new DateTime(2026, 11, 13),
            CrsAt = q => ctx.Coords.ToCrs(ToModel(q), def) is { } cc ? (cc.East, cc.North) : null,
            Table = fit.Table is null ? null : table, TableRect = fit.Table, Images = new HashSet<string>(),
            Palette = new[] { ((byte)225, (byte)140, (byte)20), ((byte)40, (byte)90, (byte)210), ((byte)30, (byte)150, (byte)80) },
        };
        return (input, SheetRenderer.Render(input), plan, reps, ToPaper, k);
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    public static string Svg(SheetRenderInput input, List<SheetPrim> prims, AnnotationPlan plan, List<LotReport> reps, Func<Vec2, Vec2> toPaper, double k, double px = 4)
    {
        var (W, H) = input.Template.PaperSize();
        string X(double x) => F(x * px);
        string Y(double y) => F((H - y) * px);
        var sb = new StringBuilder($"<svg xmlns='http://www.w3.org/2000/svg' width='{F(W * px)}' height='{F(H * px)}'><rect width='100%' height='100%' fill='white'/>");
        sb.Append("<defs><pattern id='h' width='6' height='6' patternUnits='userSpaceOnUse' patternTransform='rotate(45)'><line x1='0' y1='0' x2='0' y2='6' stroke='#e08c14' stroke-width='1.2'/></pattern></defs>");
        string gridCol = $"rgb({input.Template.GridColor[0]},{input.Template.GridColor[1]},{input.Template.GridColor[2]})";
        foreach (var g in prims.OfType<PLine>().Where(l => l.Style == PLineStyle.Grade))
            sb.Append($"<line x1='{X(g.A.X)}' y1='{Y(g.A.Y)}' x2='{X(g.B.X)}' y2='{Y(g.B.Y)}' stroke='{gridCol}' stroke-width='{F(0.13 * px)}'/>");
        // Mapa (o que o Revit mostra no viewport): lotes hachurados e anotações.
        string[] fills = { "#f8d7b0", "#d7e3fb", "#d6f0de" };
        int ci = 0;
        foreach (var r in reps)
        {
            var pts = string.Join(" ", r.Vertices.Select(v => toPaper(v.Internal)).Select(p => $"{X(p.X)},{Y(p.Y)}"));
            sb.Append($"<polygon points='{pts}' fill='{fills[ci++ % fills.Length]}' stroke='black' stroke-width='{F(0.35 * px)}'/>");
        }
        foreach (var l in plan.Lines)
        {
            var a = toPaper(l.A); var b = toPaper(l.B);
            string col = l.Kind is AnnoLineKind.Leader or AnnoLineKind.RefLeader ? "#333" : "#c00000";
            sb.Append($"<line x1='{X(a.X)}' y1='{Y(a.Y)}' x2='{X(b.X)}' y2='{Y(b.Y)}' stroke='{col}' stroke-width='{F(0.18 * px)}' {(l.Kind == AnnoLineKind.ConfrontanteTick ? "stroke-dasharray='4,3'" : "")}/>");
        }
        foreach (var d in plan.Dots) { var c = toPaper(d.At); sb.Append($"<circle cx='{X(c.X)}' cy='{Y(c.Y)}' r='{F(d.Radius * k * px)}' fill='black'/>"); }
        foreach (var t in plan.Texts)
        {
            var at = toPaper(t.At);
            double deg = -t.Rotation * 180 / Math.PI;
            string color = t.Kind is AnnoTextKind.Confrontante or AnnoTextKind.Measure ? "#c00000" : "black";
            var lines = t.Text.Split('\n');
            double size = (t.Kind == AnnoTextKind.LotLabel ? 2.2 : 2.0) * px;
            for (int i = 0; i < lines.Length; i++)
            {
                double dy = (i - (lines.Length - 1) / 2.0) * size * 1.5 + size * 0.35;
                bool bold = t.BoldFirstLine && i == 0;
                sb.Append($"<text x='{X(at.X)}' y='{Y(at.Y)}' font-family='Arial' font-size='{F(size)}' fill='{color}' text-anchor='middle' {(bold ? "font-weight='bold'" : "")} transform='rotate({F(deg)} {X(at.X)} {Y(at.Y)}) translate(0 {F(dy)})'>{System.Security.SecurityElement.Escape(lines[i])}</text>");
            }
        }
        // Elementos da folha.
        foreach (var p in prims)
        {
            switch (p)
            {
                case PFill f:
                {
                    var pts = string.Join(" ", f.Pts.Select(q => $"{X(q.X)},{Y(q.Y)}"));
                    string fill = f.Hatch ? "url(#h)" : $"rgb({f.R},{f.G},{f.B})";
                    sb.Append($"<polygon points='{pts}' fill='{fill}' stroke='none'/>");
                    break;
                }
                case PLine { Style: PLineStyle.Grade }:
                    break;
                case PLine l:
                {
                    var (w, col, dash) = l.Style switch
                    {
                        PLineStyle.Moldura => (0.5, "black", ""),
                        PLineStyle.Bloco => (0.3, "black", ""),
                        PLineStyle.Papel => (0.1, "#bbb", ""),
                        PLineStyle.Tracejada => (0.13, "#555", "stroke-dasharray='3,2'"),
                        PLineStyle.Vermelha => (0.25, "#c00000", ""),
                        PLineStyle.MeioFio => (0.25, "#666", ""),
                        PLineStyle.Limite => (0.35, "black", ""),
                        _ => (0.15, "black", ""),
                    };
                    sb.Append($"<line x1='{X(l.A.X)}' y1='{Y(l.A.Y)}' x2='{X(l.B.X)}' y2='{Y(l.B.Y)}' stroke='{col}' stroke-width='{F(w * px)}' {dash}/>");
                    break;
                }
                case PCircle c:
                    sb.Append($"<circle cx='{X(c.C.X)}' cy='{Y(c.C.Y)}' r='{F(c.Radius * px)}' fill='{(c.Filled ? "black" : "none")}' stroke='black' stroke-width='{F(0.15 * px)}'/>");
                    break;
                case PImage im:
                    sb.Append($"<rect x='{X(im.Rect.X0)}' y='{Y(im.Rect.Y1)}' width='{F(im.Rect.Width * px)}' height='{F(im.Rect.Height * px)}' fill='#5a7a4a'/>");
                    break;
                case PText t:
                {
                    var lines = t.Text.Split('\n');
                    double lh = t.SizeMm * 1.55;
                    double total = lines.Length * lh;
                    // Topo do bloco conforme o alinhamento vertical (eixo local do texto).
                    double topOff = t.V switch { PTextV.Top => 0, PTextV.Middle => total / 2, _ => total };
                    string anchor = t.H switch { PTextH.Center => "middle", PTextH.Right => "end", _ => "start" };
                    double deg = -t.Rotation * 180 / Math.PI;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        double baseLocal = topOff - (i * lh + t.SizeMm * 1.15);   // deslocamento vertical local (mm, para cima +)
                        sb.Append($"<text x='{X(t.At.X)}' y='{Y(t.At.Y)}' font-family='Arial' font-size='{F(t.SizeMm * 1.38 * px)}' fill='rgb({t.R},{t.G},{t.B})' text-anchor='{anchor}' {(t.Bold ? "font-weight='bold'" : "")} transform='rotate({F(deg)} {X(t.At.X)} {Y(t.At.Y)}) translate(0 {F(-baseLocal * px)})'>{System.Security.SecurityElement.Escape(lines[i])}</text>");
                    }
                    break;
                }
            }
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>SVG de uma composição do núcleo (mesmos elementos que a janela de pranchas mostra).</summary>
    public static string PrimSvg(SheetComposition comp, double px = 4)
    {
        var (W, H) = comp.Input.Template.PaperSize();
        string X(double x) => F(x * px);
        string Y(double y) => F((H - y) * px);
        var sb = new StringBuilder($"<svg xmlns='http://www.w3.org/2000/svg' width='{F(W * px)}' height='{F(H * px)}'><rect width='100%' height='100%' fill='white'/>");
        int hid = 0;
        string gridCol = $"rgb({comp.Input.Template.GridColor[0]},{comp.Input.Template.GridColor[1]},{comp.Input.Template.GridColor[2]})";
        foreach (var p in comp.All)
        {
            switch (p)
            {
                case PFill f:
                {
                    var pts = string.Join(" ", f.Pts.Select(q => $"{X(q.X)},{Y(q.Y)}"));
                    string fill = $"rgb({f.R},{f.G},{f.B})";
                    if (f.Hatch)
                    {
                        hid++;
                        sb.Append($"<defs><pattern id='h{hid}' width='6' height='6' patternUnits='userSpaceOnUse' patternTransform='rotate(45)'><rect width='6' height='6' fill='rgba({f.R},{f.G},{f.B},0.12)'/><line x1='0' y1='0' x2='0' y2='6' stroke='{fill}' stroke-width='1'/>{(f.Cross ? $"<line x1='0' y1='0' x2='6' y2='0' stroke='{fill}' stroke-width='1'/>" : "")}</pattern></defs>");
                        fill = $"url(#h{hid})";
                    }
                    sb.Append($"<polygon points='{pts}' fill='{fill}' stroke='none'/>");
                    break;
                }
                case PLine l:
                {
                    var (w, col, dash) = l.Style switch
                    {
                        PLineStyle.Moldura => (0.5, "black", ""),
                        PLineStyle.Bloco => (0.3, "black", ""),
                        PLineStyle.Grade => (0.13, gridCol, ""),
                        PLineStyle.Papel => (0.1, "#bbb", ""),
                        PLineStyle.Tracejada => (0.15, "#c00000", "stroke-dasharray='4,3'"),
                        PLineStyle.Vermelha => (0.18, "#c00000", ""),
                        PLineStyle.Divisao => (0.4, "#aa0000", ""),
                        PLineStyle.MeioFio => (0.25, "#666", ""),
                        PLineStyle.Limite => (0.35, "black", ""),
                        _ => (0.15, "#333", ""),
                    };
                    sb.Append($"<line x1='{X(l.A.X)}' y1='{Y(l.A.Y)}' x2='{X(l.B.X)}' y2='{Y(l.B.Y)}' stroke='{col}' stroke-width='{F(w * px)}' {dash}/>");
                    break;
                }
                case PCircle c:
                    sb.Append($"<circle cx='{X(c.C.X)}' cy='{Y(c.C.Y)}' r='{F(c.Radius * px)}' fill='{(c.Filled ? "black" : "none")}'/>");
                    break;
                case PImage im when im.Key == "logo" && comp.Input.Template.LogoBytes() is { } logo:
                    sb.Append($"<image x='{X(im.Rect.X0)}' y='{Y(im.Rect.Y1)}' width='{F(im.Rect.Width * px)}' height='{F(im.Rect.Height * px)}' preserveAspectRatio='xMidYMid meet' " +
                              $"href='data:image/{ImageHeader.Extension(logo)};base64,{Convert.ToBase64String(logo)}'/>");
                    break;
                case PImage im:
                    sb.Append($"<rect x='{X(im.Rect.X0)}' y='{Y(im.Rect.Y1)}' width='{F(im.Rect.Width * px)}' height='{F(im.Rect.Height * px)}' fill='#5a7a4a'/>");
                    break;
                case PText t:
                {
                    var lines = t.Text.Split('\n');
                    double lh = t.SizeMm * 1.55;
                    double total = lines.Length * lh;
                    double topOff = t.V switch { PTextV.Top => 0, PTextV.Middle => total / 2, _ => total };
                    string anchor = t.H switch { PTextH.Center => "middle", PTextH.Right => "end", _ => "start" };
                    double deg = -t.Rotation * 180 / Math.PI;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        double baseLocal = topOff - (i * lh + t.SizeMm * 1.15);
                        sb.Append($"<text x='{X(t.At.X)}' y='{Y(t.At.Y)}' font-family='Arial' font-size='{F(t.SizeMm / 0.716 * px)}' fill='rgb({t.R},{t.G},{t.B})' text-anchor='{anchor}' {(t.Bold ? "font-weight='bold'" : "")} transform='rotate({F(deg)} {X(t.At.X)} {Y(t.At.Y)}) translate(0 {F(-baseLocal * px)})'>{System.Security.SecurityElement.Escape(lines[i])}</text>");
                    }
                    break;
                }
            }
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    [Fact]
    public void CoreComposition_WritesPreviews()
    {
        var outPath = Environment.GetEnvironmentVariable("GEOPOINT_OUT");
        var v1 = new Vec2(0, 0); var v2 = new Vec2(-29.25, 14.99); var v3 = new Vec2(17.21, 41.62);
        var lot = TestProjects.Poly("01", v1, v2, v3);
        lot.EdgeInfos.Add(new EdgeInfo { MX = (v1.X + v2.X) / 2, MY = (v1.Y + v2.Y) / 2, Confrontante = "Rua E" });
        lot.EdgeInfos.Add(new EdgeInfo { MX = (v2.X + v3.X) / 2, MY = (v2.Y + v3.Y) / 2, Confrontante = "Jacy Rodrigues Aguiar" });
        lot.EdgeInfos.Add(new EdgeInfo { MX = (v3.X + v1.X) / 2, MY = (v3.Y + v1.Y) / 2, Confrontante = "Maria José" });
        var p = TestProjects.Project(lot);
        p.Info.Proprietario = "Lucas Araujo Marinho Brasil"; p.Info.Documento = "027.108.961-09"; p.Info.Matricula = "7607";
        p.Info.Municipio = "Porto Franco"; p.Info.UF = "MA"; p.Info.Endereco = "Rua E, nº SN"; p.Info.Bairro = "Jardim São Manoel"; p.Info.Cep = "65970-000";
        var ctx = TestProjects.Context(p);
        var spec = new SheetSpec { Kind = SheetKind.Atual, Title = "SITUAÇÃO ATUAL", LotIds = { lot.Id }, CoordTable = true };
        var comp = SheetPreview.Compose(ctx, new[] { lot }, spec, SheetTemplate.Default());
        Assert.Contains(comp.Map, x => x is PText t && t.Text == "P-01" && t.Bold);
        Assert.Contains(comp.Sheet, x => x is PText t && t.Text.StartsWith("N "));
        if (string.IsNullOrEmpty(outPath)) return;
        Directory.CreateDirectory(outPath);
        File.WriteAllText(Path.Combine(outPath, "v15-prancha-lote.svg"), PrimSvg(comp));
        var a1 = TestProjects.Rect(0, 0, 12, 30, "01"); var a2 = TestProjects.Rect(12, 0, 12, 30, "02"); var a3 = TestProjects.Rect(24, 0, 12, 30, "03");
        a3.IsRemanescente = true;
        foreach (var l in new[] { a1, a2, a3 }) l.EdgeInfos.Add(new EdgeInfo { MX = l.Outer[0].X1 + 6, MY = 0, Confrontante = "Rua Projetada 1", Testada = true });
        a1.EdgeInfos.Add(new EdgeInfo { MX = 0, MY = 15, Confrontante = "Coité" });
        a3.EdgeInfos.Add(new EdgeInfo { MX = 36, MY = 15, Confrontante = "Bemeval" });
        var p2 = TestProjects.Project(a1, a2, a3);
        p2.Info.Proprietario = "Maria da Silva"; p2.Info.Matricula = "1234"; p2.Info.Municipio = "Porto Franco"; p2.Info.UF = "MA";
        var op = new OperationRecord { Type = OperationType.Desmembramento, ChildIds = { a1.Id, a2.Id, a3.Id } };
        p2.Operations.Add(op);
        var ctx2 = TestProjects.Context(p2);
        var colors = new Dictionary<string, int> { [a1.Id] = 0, [a2.Id] = 1, [a3.Id] = 2 };
        var geral = new SheetSpec { Kind = SheetKind.Proposta, Title = "SITUAÇÃO PROPOSTA", Subtitle = "DESMEMBRAMENTO DO LOTE 01", LotIds = { a1.Id, a2.Id, a3.Id }, QuadroIds = { a1.Id, a2.Id, a3.Id }, OperationId = op.Id, Hatch = true, Colors = colors };
        File.WriteAllText(Path.Combine(outPath, "v15-prancha-geral.svg"), PrimSvg(SheetPreview.Compose(ctx2, new[] { a1, a2, a3 }, geral, SheetTemplate.Default())));
        var ind = new SheetSpec { Kind = SheetKind.IndividualProposta, Title = "SITUAÇÃO PROPOSTA – LOTE 02", Subtitle = "DESMEMBRAMENTO DO LOTE 01", LotIds = { a2.Id }, OperationId = op.Id, Hatch = true, Colors = colors, CoordTable = true };
        File.WriteAllText(Path.Combine(outPath, "v15-prancha-individual-paisagem.svg"), PrimSvg(SheetPreview.Compose(ctx2, new[] { a2 }, ind, SheetTemplate.Default(SheetOrientation.Paisagem))));
    }

    [Fact]
    public void Compose_TriangleLot_FitsWithoutOverlaps()
    {
        // Triângulo do modelo (Folha 02): 32,87 m, 53,55 m e 45,04 m.
        var v1 = new Vec2(0, 0); var v2 = new Vec2(-29.25, 14.99); var v3 = new Vec2(17.21, 41.62);
        var lot = TestProjects.Poly("01", v1, v2, v3);
        lot.EdgeInfos.Add(new EdgeInfo { MX = (v1.X + v2.X) / 2, MY = (v1.Y + v2.Y) / 2, Confrontante = "Rua E" });
        lot.EdgeInfos.Add(new EdgeInfo { MX = (v2.X + v3.X) / 2, MY = (v2.Y + v3.Y) / 2, Confrontante = "Jacy Rodrigues Aguiar" });
        var p = TestProjects.Project(lot);
        p.Info.Proprietario = "Lucas Araujo Marinho Brasil"; p.Info.Documento = "027.108.961-09"; p.Info.Matricula = "7607";
        p.Info.Municipio = "Porto Franco"; p.Info.UF = "MA"; p.Info.Endereco = "Rua, nº SN"; p.Info.Bairro = "Jardim São Manoel"; p.Info.Cep = "65970-000";
        var ctx = TestProjects.Context(p);
        var spec = new SheetSpec { Kind = SheetKind.Atual, Title = "SITUAÇÃO ATUAL", LotIds = { lot.Id }, CoordTable = true };
        var tpl = SheetTemplate.Default();
        var (input, prims, plan, reps, toPaper, k) = Compose(ctx, new[] { lot }, spec, tpl);
        Assert.True(input.Scale <= 500);
        Assert.NotNull(input.TableRect);
        Assert.Contains(plan.Texts, t => t.Kind == AnnoTextKind.RefName && t.Text == "P-01");
        Assert.Contains(plan.Texts, t => t.Kind == AnnoTextKind.RefCoord && t.Text.Contains(" m E") && t.Text.Contains(" m S"));
        Assert.DoesNotContain(plan.Texts, t => t.Kind == AnnoTextKind.Vertex && t.Text == "P-01");   // o nome vai na chamada
        // Desenho e tabela não se sobrepõem.
        var tr = input.TableRect!.Value;
        foreach (var v in reps[0].Vertices) Assert.False(tr.Contains(toPaper(v.Internal)));
        Assert.Contains(prims, x => x is PLine { Style: PLineStyle.Grade });
        Assert.Contains(prims, x => x is PText t && t.Text.StartsWith("E "));
        Assert.Contains(prims, x => x is PText t && t.Text.Contains("ESCALA: 1:"));
        Assert.Contains(input.Content.Identificacao, f => f.Label == "CPF/CNPJ" && f.Value == "027.108.961-09");
        var outPath = Environment.GetEnvironmentVariable("GEOPOINT_OUT");
        if (!string.IsNullOrEmpty(outPath))
        {
            Directory.CreateDirectory(outPath);
            File.WriteAllText(Path.Combine(outPath, "prancha-lote.svg"), Svg(input, prims, plan, reps, toPaper, k));
        }
    }

    [Fact]
    public void Compose_Desmembramento_GeneralAndLandscape()
    {
        var a = TestProjects.Rect(0, 0, 12, 30, "01");
        var b = TestProjects.Rect(12, 0, 12, 30, "02");
        var c = TestProjects.Rect(24, 0, 12, 30, "03");
        foreach (var l in new[] { a, b, c }) l.EdgeInfos.Add(new EdgeInfo { MX = l.Outer[0].X1 + 6, MY = 0, Confrontante = "Rua Projetada 1", Testada = true });
        var p = TestProjects.Project(a, b, c);
        p.Info.Proprietario = "Maria da Silva"; p.Info.Municipio = "Porto Franco"; p.Info.UF = "MA";
        var op = new OperationRecord { Type = OperationType.Desmembramento, ChildIds = { a.Id, b.Id, c.Id } };
        p.Operations.Add(op);
        var ctx = TestProjects.Context(p);
        var spec = new SheetSpec { Kind = SheetKind.Proposta, Title = "SITUAÇÃO PROPOSTA", Subtitle = "DESMEMBRAMENTO DO LOTE 01", LotIds = { a.Id, b.Id, c.Id }, OperationId = op.Id, Hatch = true };
        var outPath = Environment.GetEnvironmentVariable("GEOPOINT_OUT");
        if (!string.IsNullOrEmpty(outPath)) Directory.CreateDirectory(outPath);
        foreach (var o in new[] { SheetOrientation.Retrato, SheetOrientation.Paisagem })
        {
            var tpl = SheetTemplate.Default(o);
            Assert.Empty(tpl.Validate());
            var (input, prims, plan, reps, toPaper, k) = Compose(ctx, new[] { a, b, c }, spec, tpl);
            Assert.Equal(3, plan.Texts.Count(t => t.Kind == AnnoTextKind.RefName));
            Assert.Contains(input.Content.Registro, f => f.Label == "Procedimento" && f.Value == "Desmembramento");
            if (!string.IsNullOrEmpty(outPath))
                File.WriteAllText(Path.Combine(outPath, $"prancha-geral-{o}.svg".ToLowerInvariant()), Svg(input, prims, plan, reps, toPaper, k));
        }
    }

    [Fact]
    public void Grid_ScaleBar_Library()
    {
        Assert.Equal(20, MapGrid.NiceStep(18, false));
        Assert.Equal(50, MapGrid.NiceStep(44, false));
        Assert.Equal(10.0 / 3600, MapGrid.NiceStep(9.0 / 3600, true), 9);
        Assert.Equal("E 235.140", MapGrid.Label(235140, true, false));
        Assert.Equal("6°20'00\" S", MapGrid.Label(-6.3333333333, false, true));
        var bar = ScaleBar.Build(500, 40);
        Assert.Equal(20, bar.TotalM);
        Assert.Equal(40, bar.LengthMm, 6);
        Assert.Equal("20 m", bar.Ticks[^1].Text);
        // Biblioteca: grava, guarda histórico e restaura.
        var dir = Path.Combine(Path.GetTempPath(), "gp-tpl-" + Guid.NewGuid().ToString("N"));
        try
        {
            var t = SheetTemplate.Default();
            t.Name = "Escritório X";
            SheetTemplateLibrary.Save(t, folder: dir);
            t.Styles.TituloMm = 4.2;
            SheetTemplateLibrary.Save(t, "título maior", dir);
            var all = SheetTemplateLibrary.LoadAll(dir);
            var x = all.Single(q => q.Name == "Escritório X");
            Assert.Equal(4.2, x.Styles.TituloMm);
            Assert.Single(x.History);
            var back = SheetTemplateLibrary.Restore(x, x.History[0]);
            Assert.Equal(3.5, back.Styles.TituloMm);
            Assert.Contains(all, q => q.Name == "GeoPoint – Padrão");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
        // Clip da grade: o trecho dentro da tabela some.
        var parts = SheetRenderer.Clip(new Vec2(0, 5), new Vec2(100, 5), new[] { new PaperRect(40, 0, 60, 10) });
        Assert.Equal(2, parts.Count);
    }
}

public class CroquiFrameTests
{
    [Fact]
    public void TileMath_RoundTrip_And_Frame()
    {
        var p = GeoPoint.Core.Imagery.TileMath.ToPixel(-47.3944, -6.3336, 18);
        var (lon, lat) = GeoPoint.Core.Imagery.TileMath.FromPixel(p, 18);
        Assert.Equal(-47.3944, lon, 7);
        Assert.Equal(-6.3336, lat, 7);
        Assert.Equal("0321", GeoPoint.Core.Imagery.TileMath.QuadKey(5, 6, 4));
        // Lote de ~45 m: a janela mostra a vizinhança (≥ 120 m) na proporção do bloco.
        var poly = new List<(double, double)> { (-47.39441, -6.33366), (-47.39445, -6.33347), (-47.39403, -6.33328) };
        var f = GeoPoint.Core.Imagery.CroquiFrame.Fit(poly, 1.3, 480, 3, 19);
        Assert.InRange(f.Zoom, 16, 19);
        Assert.Equal(369, f.HeightPx);
        Assert.NotEmpty(f.Tiles);
        foreach (var (x, y) in poly)
        {
            var px = f.ImagePixel(x, y);
            Assert.InRange(px.X, 0, f.WidthPx);
            Assert.InRange(px.Y, 0, f.HeightPx);
        }
        Assert.Contains("{z}", GeoPoint.Core.Imagery.ImagerySettings.EsriUrl);
        Assert.Equal("https://t/17/5/6", GeoPoint.Core.Imagery.TileMath.TileUrl("https://t/{z}/{x}/{y}", 5, 6, 17));
    }
}
