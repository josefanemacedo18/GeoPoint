using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Revit.Infrastructure;
using GeoPoint.Revit.UI;

namespace GeoPoint.Revit.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class CmdReferencia : CommandBase
{
    protected override string Title => "Definir referência";

    protected override Result Run(GpSession s)
    {
        var uidoc = s.UiDoc;
        XYZ pick;
        try
        {
            pick = uidoc.Selection.PickPoint(ObjectSnapTypes.Endpoints | ObjectSnapTypes.Intersections,
                "GeoPoint: clique no vértice que tem a coordenada conhecida (use o snap de extremidade/interseção)");
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            Ui.Warn("Vista sem plano de trabalho", "Abra uma vista de planta para clicar no ponto de referência.");
            return Result.Cancelled;
        }
        var a = RvtUnits.ToVec(pick);
        var ctx = s.NewContext();
        string label = FindVertexLabel(ctx, a, out string where);
        string north = ProjectNorthText(ctx);
        string info = $"Ponto clicado: X = {Fmt.Raw(a.X, 3)} m, Y = {Fmt.Raw(a.Y, 3)} m (coordenadas do desenho)" + (where.Length > 0 ? $" – {where}" : "");
        var w = new ReferenceWindow("Coordenada do ponto de referência", info, true, north, s.Project.Reference);
        if (Ui.ShowDialog(w, s.MainWindow) != true) return Result.Cancelled;

        var r = new GeoReferenceRecord
        {
            IsSet = true,
            AnchorX = a.X, AnchorY = a.Y, AnchorZFeet = pick.Z,
            AnchorLabel = label,
            Lat = w.Sirgas.LatDeg, Lon = w.Sirgas.LonDeg, Height = w.AltitudeValue,
            InputText = w.InputText, InputKind = w.InputKind, InputDatum = w.InputDatum,
            Precision = w.Precision,
            Mode = OrientationMode.ProjectTrueNorth,
            RotationRad = s.Project.Frame.AngleRad,
            SetAt = DateTime.Now,
        };

        string extra = "";
        if (w.Mode == OrientationMode.EdgeAzimuth)
        {
            var reference = uidoc.Selection.PickObject(ObjectType.Element, new LineAndCadFilter(false), "GeoPoint: clique na linha que parte do ponto de referência");
            if (s.Doc.GetElement(reference) is not CurveElement ce) return Result.Cancelled;
            var c = ce.GeometryCurve;
            var p0 = RvtUnits.ToVec(c.GetEndPoint(0));
            var p1 = RvtUnits.ToVec(c.GetEndPoint(1));
            var (from, to) = p0.DistanceTo(a) <= p1.DistanceTo(a) ? (p0, p1) : (p1, p0);
            if (from.DistanceTo(a) > 0.05)
                Ui.Warn("Linha não parte do ponto", $"A linha escolhida começa a {Fmt.N(from.DistanceTo(a), 3)} m do ponto de referência. O azimute será aplicado à direção da linha a partir da extremidade mais próxima.");
            var v = to - from;
            double beta = Math.Atan2(v.Y, v.X);
            double alpha = w.EdgeAzimuth!.Value;
            r.Mode = OrientationMode.EdgeAzimuth;
            r.EdgeAzimuthDeg = alpha;
            r.RotationRad = AngleUtil.NormPi((90 - alpha) * Math.PI / 180 - beta);
            double diff = AngleUtil.ToDeg(AngleUtil.NormPi(r.RotationRad - s.Project.Frame.AngleRad));
            extra = $"\nDiferença em relação ao norte verdadeiro do projeto: {Fmt.N(diff, 4)}°.";
        }
        else if (w.Mode == OrientationMode.TwoPoints)
        {
            XYZ pick2;
            pick2 = uidoc.Selection.PickPoint(ObjectSnapTypes.Endpoints | ObjectSnapTypes.Intersections, "GeoPoint: clique no segundo ponto com coordenada conhecida");
            var b = RvtUnits.ToVec(pick2);
            if (b.DistanceTo(a) < 1)
            {
                Ui.Warn("Pontos muito próximos", "Os dois pontos devem estar a pelo menos 1 m de distância (de preferência dezenas de metros).");
                return Result.Cancelled;
            }
            string label2 = FindVertexLabel(ctx, b, out string where2);
            var w2 = new ReferenceWindow("Coordenada do segundo ponto", $"Segundo ponto: X = {Fmt.Raw(b.X, 3)} m, Y = {Fmt.Raw(b.Y, 3)} m" + (where2.Length > 0 ? $" – {where2}" : ""), false, "", null);
            if (Ui.ShowDialog(w2, s.MainWindow) != true) return Result.Cancelled;
            var origin = new GeodeticCoord(r.Lat, r.Lon, r.Height);
            var (e2, n2, _) = Geocentric.ToEnu(origin, new GeodeticCoord(w2.Sirgas.LatDeg, w2.Sirgas.LonDeg, r.Height), Ellipsoid.GRS80);
            var v = b - a;
            double rot = AngleUtil.NormPi(Math.Atan2(n2, e2) - Math.Atan2(v.Y, v.X));
            double dDraw = v.Length, dCoord = Math.Sqrt(e2 * e2 + n2 * n2);
            double rotDiff = AngleUtil.ToDeg(AngleUtil.NormPi(rot - s.Project.Frame.AngleRad));
            r.P2X = b.X; r.P2Y = b.Y; r.P2Lat = w2.Sirgas.LatDeg; r.P2Lon = w2.Sirgas.LonDeg;
            r.TwoPointDistanceDiff = dCoord - dDraw;
            r.TwoPointRotationDiffDeg = rotDiff;
            string msg = $"Distância no desenho: {Fmt.N(dDraw, 3)} m\nDistância pelas coordenadas: {Fmt.N(dCoord, 3)} m\nDiferença: {Fmt.N(dCoord - dDraw, 3)} m ({Fmt.N((dCoord - dDraw) / dDraw * 100, 3)}%)\n" +
                         $"Rotação calculada x norte verdadeiro do projeto: {Fmt.N(rotDiff, 4)}°\n\n" +
                         "Coordenadas do Google/GPS de navegação costumam ter erro de alguns metros; diferenças grandes indicam ponto trocado ou desenho fora de escala.";
            if (Ui.Confirm("Conferência com dois pontos", msg, "Usar a orientação calculada pelos dois pontos", "Manter o norte verdadeiro do projeto"))
            {
                r.Mode = OrientationMode.TwoPoints;
                r.RotationRad = rot;
            }
            extra = $"\nDiferença de distância entre os dois pontos: {Fmt.N(dCoord - dDraw, 3)} m.";
            _ = label2;
        }

        s.Project.Reference = r;
        s.SaveProject("GeoPoint: definir referência");

        var ctx2 = s.NewContext();
        var utm = ctx2.Coords.ToCrs(a, CrsDef.Utm());
        string utmText = utm is { } u ? $"UTM {u.ZoneLabel}: E {AngleText.FormatNumber(u.East, 3)} m, N {AngleText.FormatNumber(u.North, 3)} m" : "";
        string done = $"Ponto {(string.IsNullOrWhiteSpace(label) ? "" : label + " ")}definido em {AngleText.FormatDms(r.Lat, 4, AngleKind.Latitude)}, {AngleText.FormatDms(r.Lon, 4, AngleKind.Longitude)}.\n{utmText}{extra}\n\n" +
                      "Todas as coordenadas dos lotes passam a ser calculadas a partir deste ponto.";
        if (s.Project.ActiveLots.Any() && Ui.Confirm("Referência definida", done + "\n\nDeseja abrir os lotes no Google Earth agora para conferir sobre a imagem de satélite?",
                "Conferir no Google Earth (KMZ)", "Não"))
            CmdExportarSig.QuickKmz(s);
        else if (!s.Project.ActiveLots.Any()) Ui.Info("Referência definida", done);
        return Result.Succeeded;
    }

    internal static string FindVertexLabel(ProjectContext ctx, Vec2 p, out string where)
    {
        where = "";
        foreach (var lot in ProjectContext.SortLots(ctx.Project.ActiveLots))
        {
            try
            {
                var rep = ctx.Report(lot);
                var v = rep.Vertices.FirstOrDefault(x => x.Internal.DistanceTo(p) <= 0.005);
                if (v is not null)
                {
                    where = $"vértice {v.Name} de {lot.DisplayName}";
                    return v.Name;
                }
            }
            catch { }
        }
        return "";
    }

    internal static string ProjectNorthText(ProjectContext ctx)
    {
        double azY = CoordinateService.AzimuthOfEnuVector(new Vec2(0, 1).Rotate(ctx.Project.Frame.AngleRad));
        if (Math.Abs(AngleUtil.NormPi(ctx.Project.Frame.AngleRad)) < 1e-9)
            return "O norte do projeto coincide com o norte verdadeiro (ângulo 0°).";
        return $"Lido do projeto: o norte do projeto (eixo Y) aponta para o azimute verdadeiro {AngleText.FormatDms(azY, 2, AngleKind.Azimuth)}. " +
               "Ajuste em Gerenciar › Localização do projeto, se necessário.";
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CmdCaminhamento : CommandBase
{
    protected override string Title => "Desenhar por azimutes";

    protected override Result Run(GpSession s)
    {
        var w = new TraverseWindow(s.Project.Reference.IsSet);
        if (Ui.ShowDialog(w, s.MainWindow) != true || w.ResultData is null) return Result.Cancelled;
        var res = w.ResultData;
        var ctx = s.NewContext();

        Vec2 start;
        double z;
        if (w.StartAtReference)
        {
            start = ctx.Coords.Anchor;
            z = s.Project.Reference.AnchorZFeet;
        }
        else
        {
            XYZ p;
            try
            {
                p = s.UiDoc.Selection.PickPoint(ObjectSnapTypes.Endpoints | ObjectSnapTypes.Intersections | ObjectSnapTypes.Nearest | ObjectSnapTypes.Points,
                    "GeoPoint: clique no ponto inicial do caminhamento");
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                Ui.Warn("Vista sem plano de trabalho", "Abra uma vista de planta para clicar no ponto inicial.");
                return Result.Cancelled;
            }
            start = RvtUnits.ToVec(p);
            z = p.Z;
        }

        // Curvas no plano local (leste/norte) → desenho (rotação inversa do norte verdadeiro).
        Vec2 Map(Vec2 enu) => start + ctx.Coords.VectorFromEnu(enu);
        var curves = res.Curves.Select(c => c is ArcCurve
            ? CurveFactory.FromThreePoints(Map(c.Start), Map(c.Mid), Map(c.End))
            : (Curve2)new LineCurve(Map(c.Start), Map(c.End))).ToList();

        using var t = new Transaction(s.Doc, "GeoPoint: desenhar caminhamento");
        t.Start();
        var target = CurveTarget.ForActiveView(s.Doc, s.ActiveView, w.UseDetailLines, w.UseDetailLines ? null : z);
        // Linhas com o estilo padrão: elas servem de base para o reconhecimento dos lotes.
        var created = target.Create(s.Doc, curves, null);
        t.Commit();
        var ids = created.Select(c => c.Id).ToList();
        if (ids.Count > 0)
        {
            s.UiDoc.Selection.SetElementIds(ids);
            try { s.UiDoc.ShowElements(ids); } catch { }
        }
        Ui.Info("Caminhamento desenhado", $"{ids.Count} linha(s) criada(s) e selecionada(s). Use 'Reconhecer Lotes' para transformar o contorno em lote.");
        return Result.Succeeded;
    }
}
