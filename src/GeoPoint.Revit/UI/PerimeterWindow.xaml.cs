using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Export;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;

namespace GeoPoint.Revit.UI;

/// <summary>Trecho por azimute (ou rumo) e distância.</summary>
public sealed class CourseRow : Notify
{
    private string _az = "";
    private string _dist = "";
    private string _radius = "";
    private bool _right = true;
    public string Az { get => _az; set => Set(ref _az, value ?? ""); }
    public string Dist { get => _dist; set => Set(ref _dist, value ?? ""); }
    public string Radius { get => _radius; set => Set(ref _radius, value ?? ""); }
    public bool Right { get => _right; set => Set(ref _right, value); }
    public string Source { get; set; } = "";
    private string _output = "";
    /// <summary>Direção no formato configurado no projeto (azimute ou rumo).</summary>
    public string Output { get => _output; set => Set(ref _output, value ?? ""); }
}

/// <summary>Vértice por coordenadas.</summary>
public sealed class CoordRow : Notify
{
    private string _name = "", _a = "", _b = "", _radius = "", _output = "", _check = "";
    private bool _right = true, _bad;
    public string Name { get => _name; set => Set(ref _name, value ?? ""); }
    public string A { get => _a; set => Set(ref _a, value ?? ""); }
    public string B { get => _b; set => Set(ref _b, value ?? ""); }
    public string Radius { get => _radius; set => Set(ref _radius, value ?? ""); }
    public bool Right { get => _right; set => Set(ref _right, value); }
    /// <summary>Azimute e distância até o próximo vértice (formato do projeto).</summary>
    public string Output { get => _output; set => Set(ref _output, value ?? ""); }
    /// <summary>Conferência com o azimute e a distância escritos no texto.</summary>
    public string Check { get => _check; set => Set(ref _check, value ?? ""); }
    public bool Bad { get => _bad; set => Set(ref _bad, value); }
}

public enum PerimeterPlacement { Click, ProjectReference, Coordinates }

/// <summary>O que desenhar e onde.</summary>
public sealed class PerimeterDrawingData
{
    /// <summary>Trechos no plano local (leste, norte, m) com o primeiro vértice em (0, 0).</summary>
    public List<Curve2> Curves { get; } = new();
    public PerimeterPlacement Placement { get; set; }
    /// <summary>Coordenada SIRGAS 2000 do primeiro vértice (quando conhecida).</summary>
    public GeodeticCoord? FirstGeodetic { get; set; }
    public bool SetReference { get; set; }
    public string StartName { get; set; } = "P-01";
    /// <summary>Texto da coordenada do primeiro vértice (registro na referência do projeto).</summary>
    public string StartText { get; set; } = "";
    public CrsKind InputKind { get; set; } = CrsKind.Utm;
    public DatumId InputDatum { get; set; } = DatumId.Sirgas2000;
    public int Vertices { get; set; }
    /// <summary>Resumo (perímetro, área, conferência) para a mensagem final.</summary>
    public string Summary { get; set; } = "";
}

/// <summary>Desenhar perímetro: por azimutes e distâncias ou pelas coordenadas dos vértices.</summary>
public partial class PerimeterWindow : Window
{
    private readonly ObservableCollection<CourseRow> _rows = new();
    private readonly ObservableCollection<CoordRow> _coords = new();
    private readonly bool _hasReference;
    private readonly ValueFormatter _fmt;
    private readonly double _height;
    private readonly System.Windows.Threading.DispatcherTimer _timer, _coordTimer;
    private bool _loading = true;
    /// <summary>Depois de ler coordenadas, sugere desenhar na posição delas (até o usuário escolher outra opção).</summary>
    private bool _preferCoordPos = true;
    /// <summary>Azimutes e distâncias escritos no texto das coordenadas (conferência).</summary>
    private List<Course> _stated = new();
    /// <summary>Coordenadas encontradas no texto dos azimutes (ponto de partida e sugestão de usar a aba Coordenadas).</summary>
    private CoordinateTextResult? _azCoords;
    private PerimeterResult? _per;
    private TraverseResult? _trav;

    public const string TemplateText =
        "Inicia-se no vértice P-01; deste, segue com azimute de 90°00'00\" e distância de 30,00 m até o vértice P-02;\r\n" +
        "deste, segue com azimute de 180°00'00\" e distância de 12,00 m até o vértice P-03;\r\n" +
        "deste, segue com azimute de 270°00'00\" e distância de 30,00 m até o vértice P-04;\r\n" +
        "deste, segue com azimute de 0°00'00\" e distância de 12,00 m até o vértice P-01, ponto inicial.";

    public const string CoordTemplateText =
        "Inicia-se no vértice P-01, de coordenadas N 9.299.292,99 m e E 235.142,50 m;\r\n" +
        "deste, segue com azimute de 90°00'00\" e distância de 30,00 m até o vértice P-02, de coordenadas N 9.299.292,99 m e E 235.172,50 m;\r\n" +
        "deste, segue com azimute de 180°00'00\" e distância de 12,00 m até o vértice P-03, de coordenadas N 9.299.280,99 m e E 235.172,50 m;\r\n" +
        "deste, segue com azimute de 270°00'00\" e distância de 30,00 m até o vértice P-04, de coordenadas N 9.299.280,99 m e E 235.142,50 m;\r\n" +
        "deste, segue com azimute de 0°00'00\" e distância de 12,00 m até o vértice P-01, ponto inicial.\r\n" +
        "Datum SIRGAS 2000, projeção UTM.\r\n" +
        "\r\n" +
        "(Também pode colar linhas de planilha: P-01; 235142,50; 9299292,99 – ou latitude e longitude.)";

    /// <param name="zone">Fuso UTM padrão (o da referência do projeto ou das Configurações).</param>
    /// <param name="height">Altitude do plano local do projeto (m).</param>
    public PerimeterWindow(bool hasReference, ValueFormatter fmt, int zone, bool south, double height)
    {
        InitializeComponent();
        _hasReference = hasReference;
        _fmt = fmt;
        _height = height;
        Grid.ItemsSource = _rows;
        CGrid.ItemsSource = _coords;
        CKind.SelectedIndex = 0;
        CDatum.SelectedIndex = 0;
        CZone.Text = zone > 0 ? zone.ToString(CultureInfo.InvariantCulture) : "23";
        CHemi.SelectedIndex = south ? 0 : 1;
        SetRef.IsChecked = !hasReference;
        // Texto interpretado enquanto se digita (meio segundo depois da última tecla).
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => { _timer.Stop(); ParseText(quiet: true); };
        _coordTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _coordTimer.Tick += (_, _) => { _coordTimer.Stop(); ParseCoordText(quiet: true); };
        Source.Text = TemplateText;
        CoordSource.Text = CoordTemplateText;
        _loading = false;
        ParseText(quiet: true);
        ParseCoordText(quiet: true);
        Recalc();
    }

    public PerimeterDrawingData? ResultData { get; private set; }
    public bool UseDetailLines => DetailLines.IsChecked == true;
    private bool CoordMode => Tabs.SelectedIndex == 1;
    private bool AdjustClosure => Adjust.IsChecked == true;
    private PerimeterCoordKind Kind => (PerimeterCoordKind)Math.Max(0, CKind.SelectedIndex);
    private DatumId Datum => CDatum.SelectedIndex switch { 1 => DatumId.Wgs84, 2 => DatumId.Sad69, 3 => DatumId.CorregoAlegre, _ => DatumId.Sirgas2000 };
    private int Zone => int.TryParse(CZone.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int z) ? z : 0;
    private bool South => CHemi.SelectedIndex != 1;

    private static int DatumIndex(DatumId d) => d switch { DatumId.Wgs84 => 1, DatumId.Sad69 => 2, DatumId.CorregoAlegre => 3, _ => 0 };
    private static string KindName(PerimeterCoordKind k) => k switch { PerimeterCoordKind.Geographic => "geográficas", PerimeterCoordKind.Local => "locais", _ => "UTM" };

    // ================================================================== azimutes e distâncias

    private void Source_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_timer is null || _loading) return;
        _timer.Stop();
        _timer.Start();
    }

    private void Template_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(Source.Text) && Source.Text != TemplateText &&
            MessageBox.Show(this, "Trocar o texto atual pelo modelo?", "GeoPoint", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Source.Text = TemplateText;
        ParseText(quiet: true);
    }

    private void Parse_Click(object sender, RoutedEventArgs e) => ParseText(quiet: false);

    private void ParseText(bool quiet)
    {
        string text = Source.Text ?? "";
        // Coordenadas no mesmo texto: ponto de partida georreferenciado e sugestão de desenhar pelas coordenadas.
        var c = PerimeterText.ParseCoordinates(text);
        _azCoords = c.Format == "memorial" && c.Vertices.Count >= 1 ? c : null;
        if (_azCoords is { Vertices.Count: >= 3 } ac)
        {
            CoordHintText.Text = $"O texto também traz as coordenadas de {ac.Vertices.Count} vértices ({KindName(ac.Kind)}). Desenhar pelas coordenadas é mais preciso e confere os azimutes e as distâncias do texto.";
            CoordHint.Visibility = Visibility.Visible;
        }
        else CoordHint.Visibility = Visibility.Collapsed;

        var list = MemorialTextParser.Parse(text);
        if (list.Count == 0)
        {
            if (!quiet)
                Fmt.Msg("Nenhum par azimute/rumo + distância foi encontrado no texto.\nUse expressões como \"azimute de 45°30'00\" e distância de 25,40 m\" ou \"rumo 30°00' NE medindo 12,00 m\"." +
                        (_azCoords is { Vertices.Count: >= 3 } ? "\n\nO texto tem coordenadas: use a aba Coordenadas." : ""), icon: MessageBoxImage.Warning);
            Recalc();
            return;
        }
        _rows.Clear();
        foreach (var co in list)
            _rows.Add(new CourseRow { Az = AngleText.FormatDms(co.AzimuthDeg, 2, AngleKind.Azimuth), Dist = Fmt.Raw(co.Distance, 4), Source = co.Source });
        Recalc();
    }

    private void UseCoords_Click(object sender, RoutedEventArgs e)
    {
        CoordSource.Text = Source.Text;
        Tabs.SelectedIndex = 1;
        ParseCoordText(quiet: false);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        _rows.Add(new CourseRow());
        Grid.SelectedIndex = _rows.Count - 1;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is CourseRow r) _rows.Remove(r);
        Recalc();
    }

    private bool TryCourses(out List<Course> courses, out string error)
    {
        courses = new();
        error = "";
        int i = 1;
        foreach (var r in _rows)
        {
            if (string.IsNullOrWhiteSpace(r.Az) && string.IsNullOrWhiteSpace(r.Dist)) { i++; continue; }
            if (!AngleText.TryParseAngle(r.Az, out double az) || az < 0 || az >= 360) { error = $"Trecho {i}: azimute inválido."; return false; }
            if (!Fmt.TryNum(r.Dist, out double d) || d <= 0) { error = $"Trecho {i}: distância inválida."; return false; }
            double? rad = null;
            if (!string.IsNullOrWhiteSpace(r.Radius))
            {
                if (!Fmt.TryNum(r.Radius, out double rr) || rr <= 0) { error = $"Trecho {i}: raio inválido."; return false; }
                if (rr * 2 < d) { error = $"Trecho {i}: o raio deve ser pelo menos metade da corda."; return false; }
                rad = rr;
            }
            courses.Add(new Course { AzimuthDeg = az, Distance = d, Radius = rad, ArcToRight = r.Right });
            i++;
        }
        if (courses.Count == 0) { error = "Nenhum trecho informado."; return false; }
        return true;
    }

    private void RecalcCourses()
    {
        _trav = null;
        if (!TryCourses(out var courses, out var err))
        {
            Result.Text = _rows.Count == 0 ? "Cole um texto e clique em Interpretar, ou adicione trechos." : err;
            Preview.SetItems(new List<PreviewItem>(), 0);
            return;
        }
        var raw = Traverse.Compute(courses, adjust: false);
        var res = Traverse.Compute(courses, AdjustClosure);
        _trav = res;
        // Saída no formato do projeto (azimute ou rumo, casas decimais configuradas).
        int k = 0;
        foreach (var r in _rows)
        {
            if (AngleText.TryParseAngle(r.Az, out double az) && k < courses.Count)
                r.Output = _fmt.Angle(az) + " – " + _fmt.Distance(courses[k].Distance);
            if (!string.IsNullOrWhiteSpace(r.Az) || !string.IsNullOrWhiteSpace(r.Dist)) k++;
        }
        ShowTraversePreview(raw, res);
        string prec = double.IsInfinity(raw.RelativePrecision) ? "fechamento exato" : $"1 : {Fmt.N(raw.RelativePrecision, 0)}";
        Result.Text = $"Trechos: {courses.Count}    Perímetro: {Fmt.N(raw.Perimeter, 3)} m\n" +
                      $"Erro de fechamento: {Fmt.N(raw.MisclosureLength, 3)} m  (ΔE {Fmt.N(raw.Misclosure.X, 3)}  ΔN {Fmt.N(raw.Misclosure.Y, 3)})    Precisão: {prec}\n" +
                      $"Área do polígono {(AdjustClosure ? "ajustado" : "(fechando a última linha)")}: {Fmt.N(res.Area, 2)} m²";
    }

    /// <summary>Desenho do caminhamento (pontos, nomes e erro de fechamento em vermelho).</summary>
    private void ShowTraversePreview(TraverseResult raw, TraverseResult res)
    {
        var items = new List<PreviewItem>();
        var pts = (AdjustClosure ? res.AdjustedPoints : res.Points).ToList();
        if (pts.Count >= 2)
        {
            var poly = res.Curves.SelectMany(c => c.Densify(0.2)).ToList();
            if (AdjustClosure && pts.Count >= 3)
                items.Add(new PreviewItem { Rings = { poly }, Fill = Color.FromArgb(70, 230, 150, 40), Hatch = true, Stroke = Color.FromRgb(40, 40, 40), StrokeWidth = 1.6 });
            else items.Add(new PreviewItem { Line = poly, Stroke = Color.FromRgb(40, 40, 40), StrokeWidth = 1.6 });
            for (int i = 0; i < pts.Count; i++)
            {
                if (AdjustClosure && i == pts.Count - 1 && pts[i].DistanceTo(pts[0]) < 1e-6) break;
                items.Add(new PreviewItem { Label = $"P-{i + 1:00}", LabelAt = pts[i] });
            }
            if (raw.MisclosureLength > 0.001 && raw.Points.Count > 1)
                items.Add(new PreviewItem { Line = new List<Vec2> { raw.Points[^1], raw.Points[0] }, Stroke = Color.FromRgb(220, 30, 30), StrokeWidth = 1.2, Dashed = true });
        }
        Preview.SetItems(items, 0);
    }

    // ================================================================== coordenadas

    private void CoordSource_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_coordTimer is null || _loading) return;
        _coordTimer.Stop();
        _coordTimer.Start();
    }

    private void CoordTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(CoordSource.Text) && CoordSource.Text != CoordTemplateText &&
            MessageBox.Show(this, "Trocar o texto atual pelo modelo?", "GeoPoint", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        CoordSource.Text = CoordTemplateText;
        ParseCoordText(quiet: true);
    }

    private void CoordParse_Click(object sender, RoutedEventArgs e) => ParseCoordText(quiet: false);

    private string FormatValue(double v, PerimeterCoordKind k) =>
        k == PerimeterCoordKind.Geographic ? v.ToString("0.00000000", Fmt.Pt) : v.ToString("0.000", Fmt.Pt);

    private void ParseCoordText(bool quiet)
    {
        var t = PerimeterText.ParseCoordinates(CoordSource.Text ?? "");
        if (t.Vertices.Count < 2)
        {
            if (!quiet)
                Fmt.Msg("Não foram encontradas coordenadas de pelo menos dois vértices.\n\nExemplos aceitos:\n" +
                        "• memorial: \"vértice P-01, de coordenadas N 9.299.292,99 m e E 235.142,50 m\"\n" +
                        "• SIGEF: \"Longitude: -47°23'39,086\", Latitude: -6°20'01,164\"\"\n" +
                        "• planilha: uma linha por vértice – P-01; 235142,50; 9299292,99", icon: MessageBoxImage.Warning);
            CDetected.Text = t.Vertices.Count == 1 ? "Só um vértice com coordenadas no texto." : "";
            return;
        }
        _loading = true;
        CKind.SelectedIndex = (int)t.Kind;
        if (t.Datum is DatumId d) CDatum.SelectedIndex = DatumIndex(d);
        _coords.Clear();
        foreach (var v in t.Vertices)
            _coords.Add(new CoordRow { Name = v.Name, A = FormatValue(v.A, t.Kind), B = FormatValue(v.B, t.Kind) });
        _stated = t.StatedCourses.ToList();
        _loading = false;
        _preferCoordPos = true;
        string datum = t.Kind == PerimeterCoordKind.Local ? "" : $", datum {GeoPoint.Core.Geodesy.Datum.Get(t.Datum ?? DatumId.Sirgas2000).Name}" +
                       (t.Datum is null ? " (não informado no texto: confira)" : " (do texto)");
        CDetected.Text = $"Lido do texto: {t.Vertices.Count} vértices ({(t.Format == "memorial" ? "memorial" : "tabela")}), coordenadas {KindName(t.Kind)}{datum}" +
                         (t.ClosedInText ? "; o texto volta ao primeiro vértice." : ".") +
                         (t.StatedCourses.Count > 0 ? $" {t.StatedCourses.Count} azimute(s) e distância(s) no texto para conferir." : "") +
                         (t.Warnings.Count > 0 ? "  ⚠ " + string.Join(" ", t.Warnings.Take(2)) : "");
        UpdateKindUi();
        Recalc();
    }

    private void UpdateKindUi()
    {
        var k = Kind;
        (ColA.Header, ColB.Header) = k switch
        {
            PerimeterCoordKind.Geographic => ("Latitude", "Longitude"),
            PerimeterCoordKind.Local => ("X – leste (m)", "Y – norte (m)"),
            _ => ("E (m)", "N (m)"),
        };
        CDatum.IsEnabled = k != PerimeterCoordKind.Local;
        CZone.IsEnabled = CHemi.IsEnabled = k == PerimeterCoordKind.Utm;
    }

    private void Coord_SettingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        UpdateKindUi();
        Recalc();
    }

    private void Coord_TextSettingChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        Recalc();
    }

    private void CoordAdd_Click(object sender, RoutedEventArgs e)
    {
        int n = _coords.Count + 1;
        while (_coords.Any(c => c.Name == $"P-{n:00}")) n++;
        _coords.Add(new CoordRow { Name = $"P-{n:00}" });
        CGrid.SelectedIndex = _coords.Count - 1;
    }

    private void CoordRemove_Click(object sender, RoutedEventArgs e)
    {
        if (CGrid.SelectedItem is CoordRow r) _coords.Remove(r);
        Recalc();
    }

    /// <summary>Percorre no sentido contrário: o primeiro vértice continua o primeiro; os arcos trocam de lado.</summary>
    private void CoordReverse_Click(object sender, RoutedEventArgs e)
    {
        int n = _coords.Count;
        if (n < 3) return;
        var old = _coords.ToList();
        var order = new List<int> { 0 };
        for (int i = n - 1; i >= 1; i--) order.Add(i);
        var rows = new List<CoordRow>();
        for (int k = 0; k < n; k++)
        {
            var src = old[order[k]];
            // O trecho novo k → k+1 é o trecho antigo (k+1) → k ao contrário.
            var seg = old[order[(k + 1) % n]];
            rows.Add(new CoordRow { Name = src.Name, A = src.A, B = src.B, Radius = seg.Radius, Right = !seg.Right });
        }
        _coords.Clear();
        foreach (var r in rows) _coords.Add(r);
        // Os azimutes do texto seguem o sentido original: não dá para conferir no sentido invertido.
        _stated = new List<Course>();
        Recalc();
    }

    private bool TryVertices(out List<PerimeterVertex> vs, out string error)
    {
        vs = new();
        error = "";
        var k = Kind;
        int i = 0;
        foreach (var r in _coords)
        {
            i++;
            if (string.IsNullOrWhiteSpace(r.A) && string.IsNullOrWhiteSpace(r.B)) continue;
            double? a = k == PerimeterCoordKind.Geographic ? CoordinateTableReader.ParseAngle(r.A) : (Fmt.TryNum(r.A, out double x) ? x : null);
            double? b = k == PerimeterCoordKind.Geographic ? CoordinateTableReader.ParseAngle(r.B) : (Fmt.TryNum(r.B, out double y) ? y : null);
            string name = string.IsNullOrWhiteSpace(r.Name) ? $"P-{i:00}" : r.Name.Trim();
            if (a is null || b is null) { error = $"{name}: coordenada inválida."; return false; }
            double? rad = null;
            if (!string.IsNullOrWhiteSpace(r.Radius))
            {
                if (!Fmt.TryNum(r.Radius, out double rr) || rr <= 0) { error = $"{name}: raio inválido."; return false; }
                rad = rr;
            }
            vs.Add(new PerimeterVertex { Name = name, A = a.Value, B = b.Value, Radius = rad, ArcToRight = r.Right });
        }
        if (vs.Count < 2) { error = "Informe as coordenadas de pelo menos dois vértices (cole o texto e clique em Interpretar)."; return false; }
        return true;
    }

    private void RecalcCoords()
    {
        _per = null;
        foreach (var r in _coords) { r.Output = ""; r.Check = ""; r.Bad = false; }
        if (!TryVertices(out var vs, out var err))
        {
            Result.Text = err;
            Preview.SetItems(new List<PreviewItem>(), 0);
            return;
        }
        PerimeterResult res;
        try { res = PerimeterText.Build(vs, Kind, Datum, Zone, South, CClose.IsChecked == true, _stated, _height); }
        catch (InvalidOperationException ex)
        {
            Result.Text = ex.Message;
            Preview.SetItems(new List<PreviewItem>(), 0);
            return;
        }
        _per = res;
        // Linha de cada vértice: o trecho até o próximo e a conferência com o texto.
        var filled = _coords.Where(r => !(string.IsNullOrWhiteSpace(r.A) && string.IsNullOrWhiteSpace(r.B))).ToList();
        foreach (var s in res.Segments)
        {
            var row = filled[s.From];
            row.Output = _fmt.Angle(s.Azimuth) + " – " + _fmt.Distance(s.Distance) + (s.Radius is double rr ? $" (arco R={Fmt.Raw(rr, 2)})" : "");
            if (s.Matches is bool ok && s.Stated is { } st)
            {
                row.Check = ok
                    ? $"✓ confere ({s.Basis})"
                    : $"⚠ texto: {AngleText.FormatDms(st.AzimuthDeg, 0, AngleKind.Azimuth)} – {Fmt.N(st.Distance, 2)} m  (dif. {AngleText.FormatDms(s.AzimuthDiffDeg ?? 0, 0)} e {Fmt.N(s.DistanceDiff ?? 0, 2)} m)";
                row.Bad = !ok;
            }
        }
        var sb = new System.Text.StringBuilder();
        sb.Append($"Vértices: {vs.Count}    Perímetro: {Fmt.N(res.Perimeter, 3)} m    ");
        sb.AppendLine(res.Closed ? $"Área: {Fmt.N(res.Area, 2)} m²" : "Polígono aberto (sem área)");
        if (res.Checked > 0)
        {
            string basis = res.Segments.Where(s => s.Matches is not null).GroupBy(s => s.Basis).OrderByDescending(g => g.Count()).First().Key;
            sb.AppendLine(res.CheckedOk == res.Checked
                ? $"Conferência com o texto: os {res.Checked} trecho(s) conferem (azimute {basis}; diferença máx. {AngleText.FormatDms(res.Segments.Where(s => s.Matches is not null).Max(s => s.AzimuthDiffDeg ?? 0), 0)} e {Fmt.N(res.Segments.Where(s => s.Matches is not null).Max(s => s.DistanceDiff ?? 0), 2)} m)."
                : $"⚠ Conferência com o texto: {res.Checked - res.CheckedOk} de {res.Checked} trecho(s) NÃO conferem (em vermelho na tabela e na prévia). Confira as coordenadas e o texto.");
        }
        else if (_stated.Count == 0) sb.AppendLine("O texto não traz azimutes e distâncias para conferir (o desenho segue as coordenadas).");
        foreach (var w in res.Warnings.Take(3)) sb.AppendLine("⚠ " + w);
        Result.Text = sb.ToString().TrimEnd();
        ShowCoordPreview(res);
    }

    private void ShowCoordPreview(PerimeterResult res)
    {
        var items = new List<PreviewItem>();
        if (res.Curves.Count > 0)
        {
            var poly = res.Curves.SelectMany(c => c.Densify(0.2)).ToList();
            if (res.Closed && res.Points.Count >= 3)
                items.Add(new PreviewItem { Rings = { poly }, Fill = Color.FromArgb(70, 60, 130, 220), Hatch = true, Stroke = Color.FromRgb(40, 40, 40), StrokeWidth = 1.6 });
            else items.Add(new PreviewItem { Line = poly, Stroke = Color.FromRgb(40, 40, 40), StrokeWidth = 1.6 });
            // Trechos que não conferem com o texto em vermelho.
            for (int i = 0; i < res.Segments.Count && i < res.Curves.Count; i++)
                if (res.Segments[i].Matches == false)
                    items.Add(new PreviewItem { Line = res.Curves[i].Densify(0.1).ToList(), Stroke = Color.FromRgb(210, 30, 30), StrokeWidth = 3 });
        }
        for (int i = 0; i < res.Points.Count; i++)
            items.Add(new PreviewItem { Label = res.Names[i], LabelAt = res.Points[i] });
        Preview.SetItems(items, 0);
    }

    // ================================================================== comum

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Tabs) || _loading) return;
        if (CoordMode && _coords.Count == 0) ParseCoordText(quiet: true);
        Recalc();
    }

    private void Recalc_Click(object sender, RoutedEventArgs e) => Recalc();

    private void Grid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(Recalc), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Pos_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _preferCoordPos = false;
        UpdatePositionOptions();
    }

    private void Recalc()
    {
        if (_loading || Tabs is null) return;
        if (CoordMode) RecalcCoords(); else RecalcCourses();
        UpdatePositionOptions();
    }

    /// <summary>Coordenada SIRGAS 2000 do primeiro vértice (null = sem coordenadas ou coordenadas locais).</summary>
    private GeodeticCoord? FirstGeodetic()
    {
        if (CoordMode) return _per is { Geodetic.Count: > 0 } p ? p.Geodetic[0] : null;
        if (_azCoords is not { Vertices.Count: > 0 } c || c.Kind == PerimeterCoordKind.Local) return null;
        try
        {
            var v = c.Vertices[0];
            var def = c.Kind == PerimeterCoordKind.Utm ? CrsDef.Utm(c.Datum ?? DatumId.Sirgas2000, Zone) : CrsDef.Geographic(c.Datum ?? DatumId.Sirgas2000);
            if (c.Kind == PerimeterCoordKind.Utm) { if (Zone < 1 || Zone > 60) return null; def.UtmSouth = South; }
            var g = CoordinateService.CrsToGeodetic(new CrsCoord(v.A, v.B, c.Kind == PerimeterCoordKind.Geographic), def, 0);
            return new GeodeticCoord(g.LatDeg, g.LonDeg, _height);
        }
        catch { return null; }
    }

    private void UpdatePositionOptions()
    {
        bool geo = FirstGeodetic() is not null;
        _loading = true;
        PosRef.Visibility = CoordMode ? Visibility.Collapsed : Visibility.Visible;
        PosRef.IsEnabled = _hasReference;
        PosCoord.Content = CoordMode ? "Na posição das coordenadas (usa a referência do projeto)" : "Começar na coordenada do primeiro vértice, escrita no texto";
        PosCoord.Visibility = CoordMode || _azCoords is not null ? Visibility.Visible : Visibility.Collapsed;
        PosCoord.IsEnabled = _hasReference && geo;
        if ((PosRef.IsChecked == true && (!PosRef.IsEnabled || PosRef.Visibility != Visibility.Visible)) ||
            (PosCoord.IsChecked == true && (!PosCoord.IsEnabled || PosCoord.Visibility != Visibility.Visible)))
            PosClick.IsChecked = true;
        // Coordenadas geográficas/UTM com a referência do projeto: o natural é desenhar na posição delas.
        if (CoordMode && _preferCoordPos && PosCoord.IsEnabled) { PosCoord.IsChecked = true; _preferCoordPos = false; }
        SetRef.Visibility = geo ? Visibility.Visible : Visibility.Collapsed;
        SetRef.IsEnabled = PosClick.IsChecked == true;
        _loading = false;
        string name = CoordMode ? (_per?.Names.FirstOrDefault() ?? "P-01") : (_azCoords?.Vertices.FirstOrDefault()?.Name ?? "P-01");
        PosNote.Text =
            CoordMode && Kind == PerimeterCoordKind.Local ? $"Coordenadas locais: clique onde fica o vértice {name}; X segue para leste e Y para norte (norte do projeto)." :
            geo && !_hasReference ? $"O projeto ainda não tem referência: clique onde fica o vértice {name} e deixe marcado \"Usar o primeiro vértice como ponto de referência\" para georreferenciar o projeto por estas coordenadas." :
            geo && PosCoord.IsChecked == true ? "O desenho fica exatamente na posição das coordenadas, junto dos outros lotes georreferenciados." :
            PosClick.IsChecked == true ? $"Depois de Desenhar, clique no Revit onde fica o vértice {name}." : "";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(DataGridEditingUnit.Row, true);
        CGrid.CommitEdit(DataGridEditingUnit.Row, true);
        Recalc();
        var d = new PerimeterDrawingData();
        if (CoordMode)
        {
            if (!TryVertices(out var vs, out var err)) { Fmt.Msg(err, icon: MessageBoxImage.Warning); return; }
            if (_per is null || _per.Curves.Count == 0) { Fmt.Msg(Result.Text, icon: MessageBoxImage.Warning); return; }
            if (_per.Checked > _per.CheckedOk &&
                MessageBox.Show(this, $"{_per.Checked - _per.CheckedOk} trecho(s) não conferem com os azimutes e distâncias do texto.\n\nDesenhar assim mesmo (pelas coordenadas)?",
                    "GeoPoint – Conferência", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            d.Curves.AddRange(_per.Curves);
            d.Vertices = vs.Count;
            d.StartName = vs[0].Name;
            d.InputKind = Kind == PerimeterCoordKind.Geographic ? CrsKind.Geographic : CrsKind.Utm;
            d.InputDatum = Datum;
            d.StartText = Kind == PerimeterCoordKind.Utm
                ? $"{vs[0].Name}: E {AngleText.FormatNumber(vs[0].A, 3)} N {AngleText.FormatNumber(vs[0].B, 3)} – UTM {Zone}{(South ? "S" : "N")} {GeoPoint.Core.Geodesy.Datum.Get(Datum).Name}"
                : $"{vs[0].Name}: {AngleText.FormatDms(vs[0].A, 4)} / {AngleText.FormatDms(vs[0].B, 4)} – {GeoPoint.Core.Geodesy.Datum.Get(Datum).Name}";
            d.Summary = Result.Text;
        }
        else
        {
            if (!TryCourses(out var courses, out var err)) { Fmt.Msg(err, icon: MessageBoxImage.Warning); return; }
            var res = _trav ?? Traverse.Compute(courses, AdjustClosure);
            d.Curves.AddRange(res.Curves);
            d.Vertices = courses.Count;
            if (_azCoords is { Vertices.Count: > 0 } c)
            {
                var v = c.Vertices[0];
                d.StartName = v.Name;
                d.InputKind = c.Kind == PerimeterCoordKind.Geographic ? CrsKind.Geographic : CrsKind.Utm;
                d.InputDatum = c.Datum ?? DatumId.Sirgas2000;
                d.StartText = $"{v.Name}: {Fmt.Raw(v.A, 3)} / {Fmt.Raw(v.B, 3)} ({KindName(c.Kind)})";
            }
            d.Summary = Result.Text;
        }
        if (d.Curves.Count == 0) { Fmt.Msg("Nada para desenhar.", icon: MessageBoxImage.Warning); return; }
        d.FirstGeodetic = FirstGeodetic();
        d.Placement = PosRef.IsChecked == true && PosRef.IsVisible ? PerimeterPlacement.ProjectReference
            : PosCoord.IsChecked == true && PosCoord.IsVisible ? PerimeterPlacement.Coordinates : PerimeterPlacement.Click;
        if (d.Placement != PerimeterPlacement.Click && !_hasReference) { Fmt.Msg("O projeto ainda não tem ponto de referência."); return; }
        if (d.Placement == PerimeterPlacement.Coordinates && d.FirstGeodetic is null) { Fmt.Msg("Sem a coordenada do primeiro vértice (confira o sistema e o fuso)."); return; }
        d.SetReference = d.Placement == PerimeterPlacement.Click && d.FirstGeodetic is not null && SetRef.IsChecked == true && SetRef.IsVisible;
        ResultData = d;
        DialogResult = true;
    }
}
