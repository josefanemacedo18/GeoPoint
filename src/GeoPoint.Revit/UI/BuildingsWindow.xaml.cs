using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Urban;

namespace GeoPoint.Revit.UI;

public enum BuildingsAction { Cancel, Save, Add, Update }

public sealed class BuildingRow : Notify
{
    private readonly ProjectContext _ctx;
    private readonly ValueFormatter _fmt;
    public BuildingRow(BuildingRecord b, ProjectContext ctx, ValueFormatter fmt) { Building = b; _ctx = ctx; _fmt = fmt; Recalc(); }
    public BuildingRecord Building { get; }
    public BuildingReport? Report { get; private set; }
    public LotRecord? Lot { get; private set; }

    public void Recalc()
    {
        Lot = BuildingAnalyzer.OwnerLot(_ctx.Project, Building);
        try { Report = Lot is null ? null : BuildingAnalyzer.Analyze(_ctx.Report(Lot), Building); } catch { Report = null; }
        foreach (var p in new[] { nameof(LotName), nameof(AreaText), nameof(ToText), nameof(MinText), nameof(Situacao), nameof(Construida) }) OnChanged(p);
    }

    public string LotName => Lot?.DisplayName ?? "(fora dos lotes)";
    public string Nome { get => Building.Nome; set { Building.Nome = value ?? ""; OnChanged(); } }
    public string Uso { get => Building.Uso; set { Building.Uso = value ?? ""; OnChanged(); } }
    public string Pavimentos
    {
        get => Building.Pavimentos.ToString();
        set { if (int.TryParse(value, out int n) && n >= 1 && n <= 200) { Building.Pavimentos = n; Recalc(); } }
    }
    public string Construida
    {
        get => Building.AreaConstruidaInformada is double v ? Fmt.N(v, 2) : (Report is null ? "" : Fmt.N(Report.AreaConstruida, 2) + " (calc.)");
        set
        {
            var t = (value ?? "").Replace("(calc.)", "").Trim();
            if (t.Length == 0) Building.AreaConstruidaInformada = null;
            else if (Fmt.TryNum(t, out double v) && v > 0 && Report is not null && Math.Abs(v - Report.Area * Building.Pavimentos) > 0.005) Building.AreaConstruidaInformada = v;
            Recalc();
        }
    }
    public string AreaText => Report is null ? "–" : Fmt.N(Report.Area, 2);
    public string ToText => Report is null ? "–" : Fmt.N(Report.TaxaOcupacao, 2);
    public string MinText => Report is null || Report.Afastamentos.Count == 0 ? "–" : Fmt.N(Report.MenorAfastamento, 2) + " m";
    public string Situacao => Report is null ? "Sem lote" : Report.AreaFora > 0 ? $"⚠ {Fmt.N(Report.AreaFora, 2)} m² fora" : "OK";
}

public sealed class BuildingSetbackRow
{
    public string Divisa { get; init; } = "";
    public string Lado { get; init; } = "";
    public string Confrontante { get; init; } = "";
    public string Distancia { get; init; } = "";
}

public partial class BuildingsWindow : Window
{
    private readonly ProjectContext _ctx;
    private readonly ValueFormatter _fmt;
    private readonly List<BuildingRow> _rows;
    private readonly HashSet<string> _removed;

    public BuildingsWindow(ProjectContext ctx, IEnumerable<BuildingRecord> buildings, ISet<string> removed, string? selectId, bool setbacks)
    {
        InitializeComponent();
        _ctx = ctx;
        _fmt = new ValueFormatter(ctx.Settings.Format);
        _removed = new HashSet<string>(removed);
        _rows = buildings.Where(b => !_removed.Contains(b.Id)).Select(b => new BuildingRow(b, ctx, _fmt)).ToList();
        Grid.ItemsSource = _rows;
        Setbacks.IsChecked = setbacks;
        Grid.SelectedItem = _rows.FirstOrDefault(r => r.Building.Id == selectId) ?? _rows.FirstOrDefault();
        if (Grid.SelectedItem is null) ShowPreview(null);
    }

    public BuildingsAction Action { get; private set; }
    public IReadOnlyCollection<string> Removed => _removed;
    public string? SelectedId => (Grid.SelectedItem as BuildingRow)?.Building.Id;
    public bool DrawSetbacks => Setbacks.IsChecked == true;

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowPreview(Grid.SelectedItem as BuildingRow);

    private void Grid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(() => ShowPreview(Grid.SelectedItem as BuildingRow)));

    private void ShowPreview(BuildingRow? row)
    {
        var items = new List<PreviewItem>();
        var setRows = new List<BuildingSetbackRow>();
        if (row?.Lot is { } lot)
        {
            try
            {
                items.Add(PreviewItem.FromRegion(lot.ToRegion(), Color.FromArgb(70, 230, 150, 40), lot.DisplayName, true));
                foreach (var other in _rows.Where(r => r.Lot?.Id == lot.Id))
                    items.Add(PreviewItem.FromRegion(other.Building.ToRegion(), other == row ? Color.FromArgb(160, 120, 120, 120) : Color.FromArgb(90, 150, 150, 150),
                        other.Building.Nome, true, Color.FromRgb(50, 50, 50), other == row ? 2 : 1));
                if (row.Report is { } rep)
                    foreach (var s in rep.Afastamentos)
                    {
                        if (s.Distance > 0.05)
                            items.Add(new PreviewItem { Line = new List<Vec2> { s.AtBuilding, s.AtBoundary }, Stroke = Color.FromRgb(0, 90, 170), StrokeWidth = 1, Label = Fmt.N(s.Distance, 2), LabelAt = (s.AtBuilding + s.AtBoundary) * 0.5 });
                        setRows.Add(new BuildingSetbackRow
                        {
                            Divisa = $"{s.From}–{s.To}", Lado = s.Lado is LadoLote.Automatico ? "–" : LadoInfo.Label(s.Lado),
                            Confrontante = s.Confrontante, Distancia = Fmt.N(s.Distance, 2),
                        });
                    }
            }
            catch { }
        }
        Preview.SetItems(items, 0);
        SetbackGrid.ItemsSource = setRows;
    }

    private void Add_Click(object sender, RoutedEventArgs e) { Commit(); Action = BuildingsAction.Add; DialogResult = true; }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not BuildingRow) { Fmt.Msg("Selecione a edificação."); return; }
        Commit();
        Action = BuildingsAction.Update;
        DialogResult = true;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not BuildingRow row) return;
        _removed.Add(row.Building.Id);
        _rows.Remove(row);
        Grid.ItemsSource = null;
        Grid.ItemsSource = _rows;
        Grid.SelectedItem = _rows.FirstOrDefault();
        if (_rows.Count == 0) ShowPreview(null);
    }

    private void Commit() { Grid.CommitEdit(); Grid.CommitEdit(); }

    private void Ok_Click(object sender, RoutedEventArgs e) { Commit(); Action = BuildingsAction.Save; DialogResult = true; }
}
