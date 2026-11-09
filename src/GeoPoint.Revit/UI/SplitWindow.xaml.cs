using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;
using GeoPoint.Core.Urban;

namespace GeoPoint.Revit.UI;

public sealed class EdgeChoice
{
    public EdgeChoice(string text, Curve2 curve, bool testada) { Text = text; Curve = curve; Testada = testada; }
    public string Text { get; }
    public Curve2 Curve { get; }
    public bool Testada { get; }
}

public partial class SplitWindow : Window
{
    private readonly ProjectContext _ctx;
    private readonly ValueFormatter _fmt;
    private bool _loading = true;

    public SplitWindow(ProjectContext ctx, IEnumerable<LotRow> lots, string? preselectId)
    {
        InitializeComponent();
        _ctx = ctx;
        _fmt = new ValueFormatter(ctx.Settings.Format);
        var list = lots.ToList();
        LotBox.ItemsSource = list;
        LotBox.SelectedItem = list.FirstOrDefault(r => r.Lot.Id == preselectId) ?? list.FirstOrDefault();
        var rules = ctx.Rules;
        if (rules is { AreaMinimaLote: > 0 })
        {
            MinRemainder.Text = Fmt.Raw(rules.AreaMinimaLote, 2);
            TargetArea.Text = Fmt.Raw(Math.Max(rules.AreaMinimaLote, 250), 2);
        }
        if (rules is { TestadaMinima: > 0 }) FrontWidth.Text = Fmt.Raw(Math.Max(rules.TestadaMinima, 10), 2);
        _loading = false;
        LoadEdges();
    }

    public LotRecord? Lot => (LotBox.SelectedItem as LotRow)?.Lot;
    public SplitResult? Result { get; private set; }
    public bool CreateLotsValue => CreateLots.IsChecked == true;
    public bool RegisterValue => Register.IsChecked == true && CreateLots.IsChecked == true;
    public string StartNumberText => StartNumber.Text.Trim();
    public string QuadraText => Quadra.Text.Trim();

    private void Lot_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        LoadEdges();
    }

    private void LoadEdges()
    {
        var lot = Lot;
        if (lot is null) return;
        _loading = true;
        try
        {
            var rep = _ctx.Report(lot);
            var edges = rep.Segments.Select(s => new EdgeChoice(
                $"{s.From.Name} – {s.To.Name} · {_fmt.Distance(s.LengthHorizontal)} · {_fmt.Angle(s.AzimuthTrue)}{(s.Testada ? " · testada" : "")}", s.Curve, s.Testada)).ToList();
            FrontBox.ItemsSource = edges;
            FrontBox.SelectedItem = edges.FirstOrDefault(e => e.Testada) ?? edges.OrderByDescending(e => e.Curve.Length).FirstOrDefault();
            ParallelBox.ItemsSource = edges;
            ParallelBox.SelectedIndex = edges.Count > 1 ? 1 : 0;
            Quadra.Text = lot.Quadra;
        }
        catch (Exception ex)
        {
            Summary.Text = "Não foi possível ler o lote: " + ex.Message;
        }
        finally { _loading = false; }
        Recalc();
    }

    private void Any_Changed(object sender, SelectionChangedEventArgs e) { if (!_loading) Recalc(); }
    private void Any_Checked(object sender, RoutedEventArgs e) { if (!_loading && IsLoaded) Recalc(); }
    private void Any_TextChanged(object sender, TextChangedEventArgs e) { if (!_loading && IsLoaded) Recalc(); }

    private bool TryRequest(out SplitRequest? req, out string error)
    {
        req = null;
        error = "";
        var lot = Lot;
        if (lot is null) { error = "Escolha o lote."; return false; }
        if (FrontBox.SelectedItem is not EdgeChoice front) { error = "Escolha a frente."; return false; }
        var fdir = (front.Curve.End - front.Curve.Start).Normalized();
        Vec2 cutDir;
        if (DirPerp.IsChecked == true) cutDir = fdir.PerpLeft;
        else if (DirParallel.IsChecked == true)
        {
            if (ParallelBox.SelectedItem is not EdgeChoice pe) { error = "Escolha a divisa de referência."; return false; }
            cutDir = (pe.Curve.End - pe.Curve.Start).Normalized();
        }
        else
        {
            if (!AngleText.TryParseAngle(DirAzText.Text, out double az)) { error = "Azimute inválido."; return false; }
            cutDir = _ctx.Coords.DirectionFromAzimuth(az);
        }
        if (Math.Abs(cutDir.Cross(fdir)) < 0.05) { error = "As divisas não podem ser paralelas à frente."; return false; }

        var mode = ModeEqual.IsChecked == true ? SplitMode.EqualParts : ModeArea.IsChecked == true ? SplitMode.TargetArea : SplitMode.FixedFrontage;
        int parts = 2; double target = 0, minRem = 0, width = 0;
        if (mode == SplitMode.EqualParts && (!int.TryParse(Parts.Text.Trim(), out parts) || parts < 2 || parts > 500)) { error = "Número de partes inválido (2 a 500)."; return false; }
        if (mode == SplitMode.TargetArea)
        {
            if (!Fmt.TryNum(TargetArea.Text, out target) || target <= 0) { error = "Área inválida."; return false; }
            if (!Fmt.TryNum(MinRemainder.Text, out minRem) || minRem < 0) minRem = 0;
        }
        if (mode == SplitMode.FixedFrontage && (!Fmt.TryNum(FrontWidth.Text, out width) || width <= 0)) { error = "Testada inválida."; return false; }

        req = new SplitRequest
        {
            Region = lot.ToRegion(),
            CutDirection = cutDir,
            Frontage = front.Curve,
            FromFrontageStart = FromStart.IsChecked == true,
            Mode = mode,
            Parts = parts,
            TargetArea = target,
            MinRemainderArea = minRem,
            FrontageWidth = width,
        };
        return true;
    }

    private void Recalc()
    {
        if (Summary is null) return;
        Result = null;
        if (!TryRequest(out var req, out var err)) { Summary.Text = err; Preview.ItemsSource = null; OkButton.IsEnabled = false; return; }
        try
        {
            var res = AreaSplitter.Split(req!);
            if (res.Pieces.Count > 300) { Summary.Text = "Divisão em mais de 300 partes – revise os valores."; OkButton.IsEnabled = false; return; }
            Result = res;
            var rules = _ctx.Rules;
            var rows = res.Pieces.Select(p =>
            {
                var probs = new List<string>();
                if (rules is not null)
                {
                    if (rules.AreaMinimaLote > 0 && p.Area + 1e-9 < rules.AreaMinimaLote) probs.Add($"área < {Fmt.N(rules.AreaMinimaLote, 2)} m²");
                    if (rules.TestadaMinima > 0 && p.Frontage + 1e-9 < rules.TestadaMinima) probs.Add($"testada < {Fmt.N(rules.TestadaMinima, 2)} m");
                }
                return new { Parte = p.Index, Area = Fmt.N(p.Area, 2), Testada = Fmt.N(p.Frontage, 2), Situacao = probs.Count == 0 ? "OK" : string.Join("; ", probs) };
            }).ToList();
            Preview.ItemsSource = rows;
            Summary.Text = $"{res.Pieces.Count} parte(s), {res.Cuts.Count} segmento(s) de divisa. Área total: {Fmt.N(res.Pieces.Sum(p => p.Area), 2)} m²." +
                           (res.Warnings.Count > 0 ? "\n⚠ " + string.Join("\n⚠ ", res.Warnings) : "");
            OkButton.IsEnabled = res.Cuts.Count > 0;
        }
        catch (Exception ex)
        {
            Summary.Text = "Não foi possível dividir: " + ex.Message;
            Preview.ItemsSource = null;
            OkButton.IsEnabled = false;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Recalc();
        if (Result is null || Result.Cuts.Count == 0) return;
        DialogResult = true;
    }
}
