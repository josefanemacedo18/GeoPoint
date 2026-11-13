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
    protected override string? BackupReason => "referencia";

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
        string viewInfo = ViewNorthText(s, out double viewRotation);
        string warning = NorthGuard.MismatchText(s);
        string info = $"Ponto clicado: X = {Fmt.Raw(a.X, 3)} m, Y = {Fmt.Raw(a.Y, 3)} m (coordenadas do desenho)" + (where.Length > 0 ? $" – {where}" : "");
        var w = new ReferenceWindow("Coordenada do ponto de referência", info, true, north, s.Project.Reference, viewInfo, warning);
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
            NorthConfirmed = true,
        };

        string extra = "";
        if (w.Mode == OrientationMode.ViewUp)
        {
            r.Mode = OrientationMode.ViewUp;
            r.RotationRad = viewRotation;
            double diff = AngleUtil.ToDeg(AngleUtil.NormPi(viewRotation - s.Project.Frame.AngleRad));
            extra = $"\nO norte considerado é o topo da vista \"{s.ActiveView.Name}\" (diferença de {Fmt.N(diff, 4)}° em relação ao norte verdadeiro gravado no projeto).";
        }
        else if (w.Mode == OrientationMode.EdgeAzimuth)
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
            if (Ui.Confirm("Conferência com dois pontos", msg, "Usar a orientação calculada pelos dois pontos", "Manter o topo da tela como norte"))
            {
                r.Mode = OrientationMode.TwoPoints;
                r.RotationRad = rot;
            }
            else
            {
                r.Mode = OrientationMode.ViewUp;
                r.RotationRad = viewRotation;
            }
            extra = $"\nDiferença de distância entre os dois pontos: {Fmt.N(dCoord - dDraw, 3)} m.";
            _ = label2;
        }

        s.Project.Reference = r;
        using (var tr = new Transaction(s.Doc, "GeoPoint: definir referência"))
        {
            tr.Start();
            ControlPointDrawer.RedrawAll(s);   // pontos importados acompanham a nova referência
            ProjectStore.Save(s.Doc, s.Project);
            tr.Commit();
        }

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

    /// <summary>Para onde o topo da vista atual aponta segundo o norte verdadeiro do projeto.</summary>
    internal static string ViewNorthText(GpSession s, out double viewRotation)
    {
        var v = s.ActiveView;
        var up = v.UpDirection;
        // Rotação que leva a direção "para cima na tela" ao norte (+Y do sistema leste/norte).
        viewRotation = AngleUtil.NormPi(Math.PI / 2 - Math.Atan2(up.Y, up.X));
        if (Math.Abs(up.Z) > 0.5) return "";
        double az = CoordinateService.AzimuthOfEnuVector(new Vec2(up.X, up.Y).Rotate(s.Project.Frame.AngleRad));
        string orient = "";
        try
        {
            var prm = v.get_Parameter(BuiltInParameter.PLAN_VIEW_NORTH);
            if (prm is not null && prm.HasValue) orient = prm.AsInteger() == 1 ? "orientada pelo norte verdadeiro" : "orientada pelo norte do projeto";
        }
        catch { }
        bool same = Math.Min(az, 360 - az) < 0.01;
        string text = $"Vista \"{v.Name}\"{(orient.Length > 0 ? " (" + orient + ")" : "")}: pelo norte verdadeiro gravado no projeto, o topo da tela aponta para o azimute {AngleText.FormatDms(az, 0, AngleKind.Azimuth)}.";
        text += same
            ? " Coincide com o norte – as duas opções dão o mesmo resultado."
            : " Se o seu desenho foi feito com o norte para cima nesta vista, escolha esta opção (ex.: 270° significa que o projeto colocaria o norte do desenho no oeste).";
        return text;
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
    protected override string? BackupReason => "desenhar por azimutes";

    protected override Result Run(GpSession s)
    {
        var w = new TraverseWindow(s.Project.Reference.IsSet, new GeoPoint.Core.Memorial.ValueFormatter(s.Settings.Format));
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

[Transaction(TransactionMode.Manual)]
public sealed class CmdImportarKml : CommandBase
{
    protected override string Title => "Importar pontos";
    protected override string? BackupReason => "importar pontos";

    protected override Result Run(GpSession s)
    {
        var view = s.ActiveView;
        if (!ElementUtil.SupportsDetails(view) || view is ViewSheet || !ElementUtil.IsPlanLike(view))
        {
            Ui.Warn("Vista inadequada", "Abra a vista de planta onde os pontos devem aparecer.");
            return Result.Cancelled;
        }
        // Origem: KML/KMZ, planilha (CSV/TXT) ou coordenadas copiadas (Excel, SIGEF, memorial).
        var src = new TaskDialog("GeoPoint – Importar pontos")
        {
            MainInstruction = "De onde vêm as coordenadas?",
            MainContent = "Planilhas: uma linha por ponto com nome e as duas coordenadas (UTM E/N ou latitude/longitude, em graus decimais ou graus/minutos/segundos), separadas por ponto e vírgula, tabulação ou vírgula.",
            CommonButtons = TaskDialogCommonButtons.Cancel,
        };
        src.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Arquivo KML/KMZ (Google Earth / QGIS)");
        src.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Planilha CSV ou TXT");
        src.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Colar as coordenadas copiadas (Excel, SIGEF, memorial)");
        var choice = src.Show();
        if (choice is not (TaskDialogResult.CommandLink1 or TaskDialogResult.CommandLink2 or TaskDialogResult.CommandLink3)) return Result.Cancelled;
        string path;
        List<GeoPoint.Core.Export.KmlPoint> pts;
        if (choice == TaskDialogResult.CommandLink1)
        {
            var p0 = Ui.OpenFile("Importar pontos do Google Earth / QGIS", "KML/KMZ (*.kml;*.kmz)|*.kml;*.kmz", Folders.Default(s, ""));
            if (p0 is null) return Result.Cancelled;
            path = p0;
            try { pts = GeoPoint.Core.Export.KmlReader.ReadPoints(path); }
            catch (Exception ex) { Ui.Error("Arquivo inválido", "Não foi possível ler o arquivo: " + ex.Message, ex); return Result.Failed; }
            if (pts.Count == 0) { Ui.Info("Sem pontos", "O arquivo não tem marcadores (pontos). Linhas e polígonos não são importados."); return Result.Cancelled; }
        }
        else
        {
            string text;
            if (choice == TaskDialogResult.CommandLink2)
            {
                var p0 = Ui.OpenFile("Importar coordenadas de planilha", "Planilha/texto (*.csv;*.txt)|*.csv;*.txt|Todos|*.*", Folders.Default(s, ""));
                if (p0 is null) return Result.Cancelled;
                path = p0;
                text = System.IO.File.ReadAllText(path);
            }
            else
            {
                path = "Coordenadas coladas";
                text = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : "";
                if (string.IsNullOrWhiteSpace(text)) { Ui.Info("Área de transferência vazia", "Copie as linhas da planilha (nome, E, N ou latitude, longitude) e use de novo."); return Result.Cancelled; }
            }
            // Fuso UTM: o do projeto; sem referência, o do primeiro sistema UTM das configurações (ou 23).
            var zone = s.NewContext().Coords.AutoZone;
            int zn = zone?.Number ?? (s.Settings.Memorial.CoordinateSystems.FirstOrDefault(c => c.Kind == CrsKind.Utm && c.UtmZone > 0)?.UtmZone ?? 23);
            bool south = zone?.South ?? true;
            GeoPoint.Core.Export.CoordinateTableReader.Result parsed;
            try { parsed = GeoPoint.Core.Export.CoordinateTableReader.Parse(text, zn, south); }
            catch (Exception ex) { Ui.Warn("Coordenadas", ex.Message); return Result.Cancelled; }
            pts = parsed.Points;
            if (pts.Count == 0) { Ui.Info("Sem coordenadas", "Nenhuma linha com nome e duas coordenadas foi encontrada." + (parsed.Warnings.Count > 0 ? "\n\n" + string.Join("\n", parsed.Warnings.Take(8)) : "")); return Result.Cancelled; }
            // Avisa como as coordenadas foram lidas (datum e, para UTM sem referência no projeto, o fuso).
            var info = new List<string> { parsed.DatumText };
            if (parsed.Kind == "UTM" && zone is null)
                info.Add($"Coordenadas lidas como UTM fuso {zn}{(south ? "S" : "N")}. Se o fuso for outro, defina a referência do projeto (ou o fuso em Configurações) e importe de novo.");
            if (!parsed.DatumFromFile || parsed.Datum is DatumId.Sad69 or DatumId.CorregoAlegre || zone is null)
                Ui.Info("Coordenadas importadas", string.Join("\n\n", info));
            if (parsed.Warnings.Count > 0) Logger.Info("Importar coordenadas: " + string.Join(" | ", parsed.Warnings));
        }

        var w = new ImportKmlWindow(System.IO.Path.GetFileName(path), pts, s.Project.Reference.IsSet);
        if (Ui.ShowDialog(w, s.MainWindow) != true) return Result.Cancelled;
        var chosen = w.Selected;
        string origem = System.IO.Path.GetFileName(path);
        bool refChanged = false;

        if (w.ClickMode)
        {
            var cp = w.ClickPoint!;
            XYZ pick;
            try
            {
                pick = s.UiDoc.Selection.PickPoint(ObjectSnapTypes.Endpoints | ObjectSnapTypes.Intersections | ObjectSnapTypes.Nearest,
                    $"GeoPoint: clique no local do ponto \"{cp.Nome}\"");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            var a = RvtUnits.ToVec(pick);
            var old = s.Project.Reference;
            s.Project.Reference = new GeoReferenceRecord
            {
                IsSet = true, AnchorX = a.X, AnchorY = a.Y, AnchorZFeet = pick.Z, AnchorLabel = cp.Nome,
                Lat = cp.Point.Lat, Lon = cp.Point.Lon, Height = cp.Point.Alt > 0 ? cp.Point.Alt : old.IsSet ? old.Height : 0,
                InputText = $"KML/KMZ: {origem} – {cp.Nome}", InputKind = CrsKind.Geographic, InputDatum = DatumId.Wgs84,
                Precision = PrecisionClass.GoogleMapsEarth,
                Mode = OrientationMode.ViewUp, RotationRad = NorthGuard.ViewRotation(view), NorthConfirmed = true, SetAt = DateTime.Now,
            };
            refChanged = true;
        }

        var imported = new List<ControlPoint>();
        foreach (var r in chosen)
        {
            var existing = s.Project.ControlPoints.FirstOrDefault(c => c.Origem == origem && c.Nome == r.Nome);
            var cp = existing ?? new ControlPoint { Origem = origem };
            cp.Nome = string.IsNullOrWhiteSpace(r.Nome) ? r.Point.Name : r.Nome.Trim();
            cp.Lat = r.Point.Lat; cp.Lon = r.Point.Lon; cp.Altitude = r.Point.Alt;
            if (existing is null) s.Project.ControlPoints.Add(cp);
            imported.Add(cp);
        }
        int drawn, lines = 0;
        using (var t = new Transaction(s.Doc, "GeoPoint: importar pontos"))
        {
            t.Start();
            drawn = ControlPointDrawer.Draw(s, view, imported);
            if (refChanged) ControlPointDrawer.RedrawAll(s);
            // Polígono ligando os pontos na ordem (linhas de modelo prontas para Reconhecer Lotes).
            if (w.ConnectLines && imported.Count >= 3)
            {
                var ctxL = s.NewContext();
                var pos = imported.Select(cp => ControlPointCheck.Position(ctxL, cp)).Where(p => p is not null).Select(p => p!.Value).ToList();
                var curves = new List<Curve2>();
                for (int i = 0; i < pos.Count; i++)
                    if (pos[i].DistanceTo(pos[(i + 1) % pos.Count]) > 0.001) curves.Add(new LineCurve(pos[i], pos[(i + 1) % pos.Count]));
                var target = CurveTarget.ForActiveView(s.Doc, view, false);
                lines = target.Create(s.Doc, curves, null).Count;
            }
            ProjectStore.Save(s.Doc, s.Project);
            t.Commit();
        }
        string msg = $"{drawn} ponto(s) importado(s) de {origem} na vista \"{view.Name}\"." + (lines > 0 ? $"\n{lines} linha(s) ligando os pontos: use Reconhecer Lotes para criar o lote." : "") +
                     (refChanged ? $"\nO ponto \"{w.ClickPoint!.Nome}\" passou a ser o ponto de referência do projeto (o topo da tela é o norte)." : "") +
                     "\n\nUse o cruzamento das cruzes laranja (snap de interseção) para desenhar as linhas de limite.";
        var cmp = ControlPointCheck.Compare(s.NewContext()).Where(x => imported.Contains(x.Point) && x.Distance is not null).ToList();
        if (cmp.Count > 0)
            msg += "\n\nConferência com os vértices já desenhados:\n" + string.Join("\n", cmp.Take(12).Select(x => $"• {x.Point.Nome}: {x.VertexName} do {x.Lot!.DisplayName} a {Fmt.N(x.Distance!.Value, 2)} m"));
        Ui.Info("Pontos importados", msg);
        return Result.Succeeded;
    }
}
