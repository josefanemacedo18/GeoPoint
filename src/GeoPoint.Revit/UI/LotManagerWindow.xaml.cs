using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;

namespace GeoPoint.Revit.UI;

public sealed class SegmentRow : Notify
{
    private readonly LotRecord _lot;
    private string _confrontante;
    private bool _testada;

    public SegmentRow(LotRecord lot, SegmentReport s, ValueFormatter fmt)
    {
        _lot = lot;
        Mid = s.Curve.Mid;
        Segmento = $"{s.From.Name} – {s.To.Name}";
        Tipo = s.IsArc ? "Arco" : "Reta";
        Comprimento = fmt.DistanceValue(s.LengthHorizontal);
        Azimute = fmt.Angle(s.AzimuthTrue);
        var info = ProjectContext.FindInfo(lot, Mid);
        _confrontante = info?.Confrontante ?? "";
        _testada = info?.Testada == true;
        Sugerido = s.ConfrontanteAutomatico ? s.Confrontante : "";
    }

    public Vec2 Mid { get; }
    public string Segmento { get; }
    public string Tipo { get; }
    public string Comprimento { get; }
    public string Azimute { get; }
    public string Sugerido { get; }

    public string Confrontante
    {
        get => _confrontante;
        set
        {
            if (!Set(ref _confrontante, value ?? "")) return;
            Info().Confrontante = _confrontante.Trim();
            Cleanup();
        }
    }

    public bool Testada
    {
        get => _testada;
        set
        {
            if (!Set(ref _testada, value)) return;
            Info().Testada = value;
            Cleanup();
        }
    }

    private EdgeInfo Info()
    {
        var i = ProjectContext.FindInfo(_lot, Mid);
        if (i is null)
        {
            i = new EdgeInfo { MX = Mid.X, MY = Mid.Y };
            _lot.EdgeInfos.Add(i);
        }
        return i;
    }

    private void Cleanup() => _lot.EdgeInfos.RemoveAll(e => string.IsNullOrWhiteSpace(e.Confrontante) && !e.Testada && e.Recuo is null);
}

public sealed class VertexChoice
{
    public VertexChoice(string text, Vec2 p) { Text = text; P = p; }
    public string Text { get; }
    public Vec2 P { get; }
}

public partial class LotManagerWindow : Window
{
    private readonly ParcelProject _project;
    private readonly ProjectContext _ctx;
    private readonly ValueFormatter _fmt;
    private List<LotRow> _rows = new();
    private LotRecord? _current;
    private bool _loading;

    public LotManagerWindow(ParcelProject project, ProjectContext ctx, IEnumerable<string>? focusIds = null)
    {
        InitializeComponent();
        _project = project;
        _ctx = ctx;
        _fmt = new ValueFormatter(ctx.Settings.Format);
        Uso.ItemsSource = UseOptions.All;
        RefreshList(focusIds?.FirstOrDefault());
    }

    public List<LotRecord> DeletedLots { get; } = new();
    public bool ShowInRevit { get; private set; }
    public LotRecord? CurrentLot => _current;

    private void RefreshList(string? selectId = null)
    {
        bool arch = ShowArchived.IsChecked == true;
        _ctx.Invalidate();
        _rows = ProjectContext.SortLots(_project.Lots.Where(l => arch || l.Status == LotStatus.Ativo))
            .Select(l => new LotRow(l, SafeArea(l))).ToList();
        LotsGrid.ItemsSource = _rows;
        var sel = _rows.FirstOrDefault(r => r.Lot.Id == (selectId ?? _current?.Id)) ?? _rows.FirstOrDefault();
        LotsGrid.SelectedItem = sel;
        if (sel is null) ShowLot(null);
        Status.Text = $"{_project.ActiveLots.Count()} lote(s) ativo(s), {_project.Lots.Count(l => l.Status == LotStatus.Arquivado)} arquivado(s).";
    }

    private static double SafeArea(LotRecord l)
    {
        try { return l.ToRegion().Area; } catch { return 0; }
    }

    private void Lots_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        CommitFields();
        ShowLot((LotsGrid.SelectedItem as LotRow)?.Lot);
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        CommitFields();
        RefreshList();
    }

    private void ShowLot(LotRecord? lot)
    {
        _loading = true;
        try
        {
            _current = lot;
            Detail.IsEnabled = lot is not null;
            if (lot is null)
            {
                LotTitle.Text = "Nenhum lote";
                SegGrid.ItemsSource = null;
                return;
            }
            LotTitle.Text = lot.DisplayName + (lot.Status == LotStatus.Arquivado ? "  (arquivado)" : "");
            Nome.Text = lot.Nome;
            Quadra.Text = lot.Quadra;
            Uso.SelectedValue = lot.Uso;
            Matricula.Text = lot.Matricula;
            Proprietario.Text = lot.Proprietario;
            Documento.Text = lot.Documento;
            Endereco.Text = lot.Endereco;
            AreaTitulo.Text = lot.AreaTitulo is double t ? Fmt.Raw(t, 4) : "";
            Observacoes.Text = lot.Observacoes;
            Sentido.SelectedIndex = lot.Clockwise is null ? 0 : lot.Clockwise == true ? 1 : 2;
            History.Text = string.Join(Environment.NewLine, lot.History.OrderByDescending(h => h.Date).Take(12)
                .Select(h => $"{h.Date:dd/MM/yyyy HH:mm} – {h.Description} (área {Fmt.N(h.Area, 2)} m²)"));
            RefreshReport();
        }
        finally { _loading = false; }
    }

    private void RefreshReport()
    {
        if (_current is null) return;
        _ctx.Invalidate();
        LotReport rep;
        try { rep = _ctx.Report(_current); }
        catch (Exception ex)
        {
            Measures.Text = "Não foi possível calcular este lote: " + ex.Message;
            SegGrid.ItemsSource = null;
            return;
        }
        bool old = _loading;
        _loading = true;
        var choices = rep.Vertices.Select(v => new VertexChoice($"{v.Name}  ({Fmt.Raw(v.Enu.X, 2)}; {Fmt.Raw(v.Enu.Y, 2)})", v.Internal)).ToList();
        StartVertex.ItemsSource = choices;
        StartVertex.SelectedIndex = 0;
        _loading = old;

        var parts = new List<string>
        {
            $"Área: {_fmt.Area(rep.AreaLocal)}",
            $"Perímetro: {_fmt.Distance(rep.PerimeterLocal)}",
        };
        if (rep.AreaUtm is double au) parts.Add($"Área UTM: {_fmt.AreaM2(au)}");
        if (rep.Testada > 0) parts.Add($"Testada: {_fmt.Distance(rep.Testada)}");
        if (rep.AreaDeducoes > 0) parts.Add($"Área líquida: {_fmt.AreaM2(rep.AreaLiquida)}");
        if (_current.AreaEdificavel is double ae) parts.Add($"Área edificável: {_fmt.AreaM2(ae)}");
        if (_current.AreaSuperficial is double asup) parts.Add($"Área superficial: {_fmt.AreaM2(asup)}");
        if (_current.AreaTitulo is double t && t > 0) parts.Add($"Diferença p/ título: {Fmt.N(rep.AreaLocal - t, 2)} m² ({Fmt.N((rep.AreaLocal - t) / t * 100, 2)}%)");
        Measures.Text = string.Join("   •   ", parts);
        SegGrid.ItemsSource = rep.Segments.Select(s => new SegmentRow(_current, s, _fmt)).ToList();
    }

    private void Description_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _current is null) return;
        if (sender == Sentido)
        {
            _current.Clockwise = Sentido.SelectedIndex switch { 1 => true, 2 => false, _ => null };
        }
        else if (sender == StartVertex && StartVertex.SelectedItem is VertexChoice vc)
        {
            _current.StartX = vc.P.X;
            _current.StartY = vc.P.Y;
        }
        RefreshReport();
    }

    private void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        _current.StartX = null;
        _current.StartY = null;
        RefreshReport();
    }

    private void Testada_Click(object sender, RoutedEventArgs e)
    {
        // A testada influencia o vértice inicial automático.
        Dispatcher.BeginInvoke(new Action(RefreshReport), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void SegGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) { }

    private void UseSuggestions_Click(object sender, RoutedEventArgs e)
    {
        if (SegGrid.ItemsSource is not IEnumerable<SegmentRow> rows) return;
        foreach (var r in rows)
            if (string.IsNullOrWhiteSpace(r.Confrontante) && !string.IsNullOrWhiteSpace(r.Sugerido)) r.Confrontante = r.Sugerido;
    }

    /// <summary>Aplica os campos editados ao lote atual.</summary>
    private bool CommitFields()
    {
        if (_current is null) return true;
        SegGrid.CommitEdit(DataGridEditingUnit.Row, true);
        double? area = null;
        if (!string.IsNullOrWhiteSpace(AreaTitulo.Text))
        {
            if (!Fmt.TryNum(AreaTitulo.Text, out double a) || a <= 0)
            {
                Fmt.Msg("Área do título inválida.", icon: MessageBoxImage.Warning);
                return false;
            }
            area = a;
        }
        var l = _current;
        l.Nome = Nome.Text.Trim();
        l.Numero = l.Nome;
        l.Quadra = Quadra.Text.Trim();
        if (Uso.SelectedValue is LotUse u) l.Uso = u;
        l.Matricula = Matricula.Text.Trim();
        l.Proprietario = Proprietario.Text.Trim();
        l.Documento = Documento.Text.Trim();
        l.Endereco = Endereco.Text.Trim();
        l.AreaTitulo = area;
        l.Observacoes = Observacoes.Text.Trim();
        l.Modified = DateTime.Now;
        var row = _rows.FirstOrDefault(r => r.Lot == l);
        return true;
    }

    private void Archive_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || !CommitFields()) return;
        _current.Status = _current.Status == LotStatus.Ativo ? LotStatus.Arquivado : LotStatus.Ativo;
        _current.History.Add(new HistoryEntry { Description = _current.Status == LotStatus.Ativo ? "Reativado" : "Arquivado", Area = SafeArea(_current) });
        RefreshList(_current.Id);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        if (MessageBox.Show($"Excluir {_current.DisplayName} do GeoPoint?\n\nAs linhas do desenho não são apagadas; anotações, tabela, linha de propriedade e regiões criadas pelo GeoPoint para este lote serão removidas.", "GeoPoint",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        DeletedLots.Add(_current);
        _project.Lots.Remove(_current);
        _current = null;
        RefreshList();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!CommitFields()) return;
        DialogResult = true;
    }

    private void Show_Click(object sender, RoutedEventArgs e)
    {
        if (!CommitFields()) return;
        ShowInRevit = true;
        DialogResult = true;
    }
}
