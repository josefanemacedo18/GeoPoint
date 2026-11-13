using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geodesy;

namespace GeoPoint.Revit.UI;

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

public partial class TraverseWindow : Window
{
    private readonly ObservableCollection<CourseRow> _rows = new();
    private readonly bool _hasReference;
    private readonly GeoPoint.Core.Memorial.ValueFormatter? _fmt;
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private bool _typing;

    public const string TemplateText =
        "Inicia-se no vértice P-01; deste, segue com azimute de 90°00'00\" e distância de 30,00 m até o vértice P-02;\r\n" +
        "deste, segue com azimute de 180°00'00\" e distância de 12,00 m até o vértice P-03;\r\n" +
        "deste, segue com azimute de 270°00'00\" e distância de 30,00 m até o vértice P-04;\r\n" +
        "deste, segue com azimute de 0°00'00\" e distância de 12,00 m até o vértice P-01, ponto inicial.";

    public TraverseWindow(bool hasReference, GeoPoint.Core.Memorial.ValueFormatter? fmt = null)
    {
        InitializeComponent();
        _hasReference = hasReference;
        _fmt = fmt;
        StartRef.IsEnabled = hasReference;
        Grid.ItemsSource = _rows;
        // Texto interpretado enquanto se digita (meio segundo depois da última tecla).
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => { _timer.Stop(); ParseText(quiet: true); };
        Source.Text = TemplateText;
        ParseText(quiet: true);
    }

    private void Source_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_timer is null || _typing) return;
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

    public TraverseResult? ResultData { get; private set; }
    public bool UseDetailLines => DetailLines.IsChecked == true;
    public bool StartAtReference => StartRef.IsChecked == true;
    public bool AdjustClosure => Adjust.IsChecked == true;

    private void Parse_Click(object sender, RoutedEventArgs e) => ParseText(quiet: false);

    private void ParseText(bool quiet)
    {
        var list = MemorialTextParser.Parse(Source.Text ?? "");
        if (list.Count == 0)
        {
            if (quiet) { Recalc(); return; }
            Fmt.Msg("Nenhum par azimute/rumo + distância foi encontrado no texto.\nUse expressões como \"azimute de 45°30'00\" e distância de 25,40 m\" ou \"rumo 30°00' NE medindo 12,00 m\".", icon: MessageBoxImage.Warning);
            return;
        }
        _rows.Clear();
        foreach (var c in list)
            _rows.Add(new CourseRow { Az = AngleText.FormatDms(c.AzimuthDeg, 2, AngleKind.Azimuth), Dist = Fmt.Raw(c.Distance, 4), Source = c.Source });
        Recalc();
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

    private void Recalc_Click(object sender, RoutedEventArgs e) => Recalc();

    private void Grid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(Recalc), System.Windows.Threading.DispatcherPriority.Background);

    private bool TryCourses(out System.Collections.Generic.List<Course> courses, out string error)
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

    private void Recalc()
    {
        if (!TryCourses(out var courses, out var err))
        {
            Result.Text = _rows.Count == 0 ? "Cole um texto e clique em Interpretar, ou adicione trechos." : err;
            ResultData = null;
            return;
        }
        var raw = Traverse.Compute(courses, adjust: false);
        var res = Traverse.Compute(courses, AdjustClosure);
        ResultData = res;
        // Saída no formato do projeto (azimute ou rumo, casas decimais configuradas).
        int k = 0;
        foreach (var r in _rows)
        {
            if (AngleText.TryParseAngle(r.Az, out double az) && k < courses.Count)
                r.Output = _fmt is null ? AngleText.FormatDms(az, 2, AngleKind.Azimuth) : _fmt.Angle(az) + " – " + _fmt.Distance(courses[k].Distance);
            if (!string.IsNullOrWhiteSpace(r.Az) || !string.IsNullOrWhiteSpace(r.Dist)) k++;
        }
        ShowPreview(raw, res);
        string prec = double.IsInfinity(raw.RelativePrecision) ? "fechamento exato" : $"1 : {Fmt.N(raw.RelativePrecision, 0)}";
        Result.Text = $"Trechos: {courses.Count}    Perímetro: {Fmt.N(raw.Perimeter, 3)} m\n" +
                      $"Erro de fechamento: {Fmt.N(raw.MisclosureLength, 3)} m  (ΔE {Fmt.N(raw.Misclosure.X, 3)}  ΔN {Fmt.N(raw.Misclosure.Y, 3)})    Precisão: {prec}\n" +
                      $"Área do polígono {(AdjustClosure ? "ajustado" : "(fechando a última linha)")}: {Fmt.N(res.Area, 2)} m²";
    }

    /// <summary>Desenho do caminhamento (pontos, nomes e erro de fechamento em vermelho).</summary>
    private void ShowPreview(TraverseResult raw, TraverseResult res)
    {
        var items = new System.Collections.Generic.List<PreviewItem>();
        var pts = (AdjustClosure ? res.AdjustedPoints : res.Points).ToList();
        if (pts.Count >= 2)
        {
            if (AdjustClosure && pts.Count >= 3)
                items.Add(new PreviewItem { Rings = { pts }, Fill = System.Windows.Media.Color.FromArgb(70, 230, 150, 40), Hatch = true, Stroke = System.Windows.Media.Color.FromRgb(40, 40, 40), StrokeWidth = 1.6 });
            else items.Add(new PreviewItem { Line = pts, Stroke = System.Windows.Media.Color.FromRgb(40, 40, 40), StrokeWidth = 1.6 });
            for (int i = 0; i < pts.Count; i++)
            {
                if (AdjustClosure && i == pts.Count - 1 && pts[i].DistanceTo(pts[0]) < 1e-6) break;
                items.Add(new PreviewItem { Label = $"P-{i + 1:00}", LabelAt = pts[i] });
            }
            if (raw.MisclosureLength > 0.001 && raw.Points.Count > 1)
                items.Add(new PreviewItem { Line = new System.Collections.Generic.List<GeoPoint.Core.Geometry.Vec2> { raw.Points[^1], raw.Points[0] }, Stroke = System.Windows.Media.Color.FromRgb(220, 30, 30), StrokeWidth = 1.2, Dashed = true });
        }
        Preview.SetItems(items, 0);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(DataGridEditingUnit.Row, true);
        Recalc();
        if (!TryCourses(out _, out var err)) { Fmt.Msg(err, icon: MessageBoxImage.Warning); return; }
        if (ResultData is null) return;
        if (StartAtReference && !_hasReference) { Fmt.Msg("O projeto ainda não tem ponto de referência."); return; }
        DialogResult = true;
    }
}
