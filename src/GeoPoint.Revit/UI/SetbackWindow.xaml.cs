using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;

namespace GeoPoint.Revit.UI;

public sealed class SetbackRow : Notify
{
    private string _recuo;
    public SetbackRow(SegmentReport s, ValueFormatter fmt, string tipo, double? recuo)
    {
        Curve = s.Curve;
        Mid = s.Curve.Mid;
        Segmento = $"{s.From.Name} – {s.To.Name}";
        Distancia = fmt.DistanceValue(s.LengthHorizontal);
        Confrontante = s.Confrontante;
        Tipo = tipo;
        _recuo = recuo is double r ? Fmt.Raw(r, 3) : "";
    }
    public Curve2 Curve { get; }
    public Vec2 Mid { get; }
    public string Segmento { get; }
    public string Distancia { get; }
    public string Confrontante { get; }
    public string Tipo { get; set; }
    public string Recuo { get => _recuo; set => Set(ref _recuo, value ?? ""); }
    public void SetTipo(string t) { Tipo = t; OnChanged(nameof(Tipo)); }
}

public partial class SetbackWindow : Window
{
    private readonly ProjectContext _ctx;
    private readonly ValueFormatter _fmt;
    private List<SetbackRow> _rows = new();
    private bool _loading = true;

    public SetbackWindow(ProjectContext ctx, IEnumerable<LotRow> lots, string? preselectId)
    {
        InitializeComponent();
        _ctx = ctx;
        _fmt = new ValueFormatter(ctx.Settings.Format);
        var list = lots.ToList();
        LotBox.ItemsSource = list;
        LotBox.SelectedItem = list.FirstOrDefault(r => r.Lot.Id == preselectId) ?? list.FirstOrDefault();
        _loading = false;
        Load();
    }

    public LotRecord? Lot => (LotBox.SelectedItem as LotRow)?.Lot;
    public SetbackResult? ResultData { get; private set; }
    public bool DrawOutline => Draw.IsChecked == true;
    public List<(Vec2 Mid, double? Recuo)> Recuos => _rows.Select(r => (r.Mid, Fmt.TryNum(r.Recuo, out double v) ? (double?)v : null)).ToList();

    private void Lot_Changed(object sender, SelectionChangedEventArgs e) { if (!_loading) Load(); }

    private static string Classify(LotReport rep, SegmentReport s)
    {
        if (s.Testada) return "Frente";
        var fronts = rep.Segments.Where(x => x.Testada).ToList();
        if (fronts.Count == 0) return "Lateral";
        // Fundos: divisa aproximadamente paralela e oposta à frente.
        var f = fronts.OrderByDescending(x => x.LengthHorizontal).First();
        var fd = (f.Curve.End - f.Curve.Start).Normalized();
        var sd = (s.Curve.End - s.Curve.Start).Normalized();
        double par = Math.Abs(fd.Cross(sd));
        double dist = Math.Abs((s.Curve.Mid - f.Curve.Mid).Cross(fd));
        double depth = rep.Segments.Max(x => Math.Abs((x.Curve.Mid - f.Curve.Mid).Cross(fd)));
        return par < 0.5 && dist >= depth * 0.6 ? "Fundos" : "Lateral";
    }

    private void Load()
    {
        var lot = Lot;
        if (lot is null) return;
        try
        {
            _ctx.Invalidate();
            var rep = _ctx.Report(lot);
            _rows = rep.Segments.Select(s => new SetbackRow(s, _fmt, Classify(rep, s), s.Recuo)).ToList();
            Grid.ItemsSource = _rows;
            Recalc();
        }
        catch (Exception ex) { Result.Text = "Não foi possível ler o lote: " + ex.Message; }
    }

    private void Profile_Click(object sender, RoutedEventArgs e)
    {
        var rules = _ctx.Rules;
        if (rules is null || (rules.RecuoFrontal <= 0 && rules.RecuoLateral <= 0 && rules.RecuoFundos <= 0))
        {
            Fmt.Msg("O perfil urbanístico do projeto não tem recuos definidos (Configurações › Perfis urbanísticos).");
            return;
        }
        foreach (var r in _rows)
            r.Recuo = Fmt.Raw(r.Tipo switch { "Frente" => rules.RecuoFrontal, "Fundos" => rules.RecuoFundos, _ => rules.RecuoLateral }, 3);
        Grid.Items.Refresh();
        Recalc();
    }

    private void Grid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(Recalc), System.Windows.Threading.DispatcherPriority.Background);

    private void Recalc()
    {
        var lot = Lot;
        if (lot is null) return;
        var edges = new List<(Curve2, double)>();
        foreach (var r in _rows)
        {
            double v = 0;
            if (!string.IsNullOrWhiteSpace(r.Recuo) && (!Fmt.TryNum(r.Recuo, out v) || v < 0))
            {
                Result.Text = $"Recuo inválido na divisa {r.Segmento}.";
                ResultData = null;
                return;
            }
            edges.Add((r.Curve, v));
        }
        try
        {
            var region = lot.ToRegion();
            var res = Setbacks.Compute(region, edges);
            ResultData = res;
            double pct = region.Area > 0 ? res.Area / region.Area * 100 : 0;
            string to = "";
            var rules = _ctx.Rules;
            if (rules is { TaxaOcupacaoMax: > 0 })
            {
                double maxProj = region.Area * rules.TaxaOcupacaoMax / 100;
                to = $"\nProjeção máxima pela taxa de ocupação ({Fmt.N(rules.TaxaOcupacaoMax, 1)}%): {Fmt.N(maxProj, 2)} m² → limite efetivo: {Fmt.N(Math.Min(maxProj, res.Area), 2)} m².";
            }
            Result.Text = $"Área do lote: {Fmt.N(region.Area, 2)} m²   •   Área edificável: {Fmt.N(res.Area, 2)} m² ({Fmt.N(pct, 1)}%)" + to +
                          (res.Warnings.Count > 0 ? "\n⚠ " + string.Join("\n⚠ ", res.Warnings) : "");
        }
        catch (Exception ex)
        {
            Result.Text = "Falha no cálculo: " + ex.Message;
            ResultData = null;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(DataGridEditingUnit.Row, true);
        Recalc();
        if (ResultData is null) return;
        DialogResult = true;
    }
}
