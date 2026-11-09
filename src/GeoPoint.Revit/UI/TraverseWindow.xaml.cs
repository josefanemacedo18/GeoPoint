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
}

public partial class TraverseWindow : Window
{
    private readonly ObservableCollection<CourseRow> _rows = new();
    private readonly bool _hasReference;

    public TraverseWindow(bool hasReference)
    {
        InitializeComponent();
        _hasReference = hasReference;
        StartRef.IsEnabled = hasReference;
        Grid.ItemsSource = _rows;
        Recalc();
    }

    public TraverseResult? ResultData { get; private set; }
    public bool UseDetailLines => DetailLines.IsChecked == true;
    public bool StartAtReference => StartRef.IsChecked == true;
    public bool AdjustClosure => Adjust.IsChecked == true;

    private void Parse_Click(object sender, RoutedEventArgs e)
    {
        var list = MemorialTextParser.Parse(Source.Text ?? "");
        if (list.Count == 0)
        {
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
        string prec = double.IsInfinity(raw.RelativePrecision) ? "fechamento exato" : $"1 : {Fmt.N(raw.RelativePrecision, 0)}";
        Result.Text = $"Trechos: {courses.Count}    Perímetro: {Fmt.N(raw.Perimeter, 3)} m\n" +
                      $"Erro de fechamento: {Fmt.N(raw.MisclosureLength, 3)} m  (ΔE {Fmt.N(raw.Misclosure.X, 3)}  ΔN {Fmt.N(raw.Misclosure.Y, 3)})    Precisão: {prec}\n" +
                      $"Área do polígono {(AdjustClosure ? "ajustado" : "(fechando a última linha)")}: {Fmt.N(res.Area, 2)} m²";
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
