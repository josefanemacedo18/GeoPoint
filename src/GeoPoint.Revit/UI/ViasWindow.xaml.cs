using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Settings;

namespace GeoPoint.Revit.UI;

public sealed class ViaRow : Notify
{
    private string _nome;
    public ViaRow(ViaRecord v) { Via = v; _nome = v.Nome; Largura = v.Largura; Calcada = v.Calcada; Prolongamento = v.Prolongamento; }
    public ViaRecord Via { get; }
    public string Nome { get => _nome; set => Set(ref _nome, value ?? ""); }
    public double Largura { get; set; }
    public double Calcada { get; set; }
    public double Prolongamento { get; set; }
}

public sealed class FrontRow : Notify
{
    private string _viaId;
    private bool _testada;
    public FrontRow(LotRecord lot, SegmentReport s, ValueFormatter fmt)
    {
        Lot = lot;
        Mid = s.Curve.Mid;
        Lote = lot.DisplayName;
        Divisa = $"{s.From.Name} – {s.To.Name}";
        Comprimento = fmt.DistanceValue(s.LengthHorizontal);
        Atual = s.Confrontante;
        var info = ProjectContext.FindInfo(lot, Mid);
        _viaId = info?.Tipo == ConfrontanteTipo.Via ? info.ViaId : "";
        _testada = info?.Testada == true;
        External = s.ConfrontanteLotId.Length == 0;
    }
    public LotRecord Lot { get; }
    public Vec2 Mid { get; }
    public string Lote { get; }
    public string Divisa { get; }
    public string Comprimento { get; }
    public string Atual { get; }
    public bool External { get; }
    public bool Relevant => _testada || _viaId.Length > 0;
    public string ViaId
    {
        get => _viaId;
        set { if (Set(ref _viaId, value ?? "") && _viaId.Length > 0 && !_testada) Testada = true; }
    }
    public bool Testada { get => _testada; set => Set(ref _testada, value); }
}

public sealed class CrossRow : Notify
{
    public CrossRow(CruzamentoRecord c)
    {
        Record = c;
        string V(int i) => i < c.ViaIds.Count ? c.ViaIds[i] : "";
        V1 = V(0); V2 = V(1); V3 = V(2); V4 = V(3);
        A1 = c.Atravessam.Contains(V1) && V1.Length > 0; A2 = c.Atravessam.Contains(V2) && V2.Length > 0;
        A3 = c.Atravessam.Contains(V3) && V3.Length > 0; A4 = c.Atravessam.Contains(V4) && V4.Length > 0;
        Raio = c.RaioEsquina;
        Continuacao = c.Continuacao;
    }
    public CruzamentoRecord Record { get; }
    public string V1 { get; set; } public string V2 { get; set; } public string V3 { get; set; } public string V4 { get; set; }
    public bool A1 { get; set; } public bool A2 { get; set; } public bool A3 { get; set; } public bool A4 { get; set; }
    public double Raio { get; set; }
    public double Continuacao { get; set; }
}

public partial class ViasWindow : Window
{
    private readonly ParcelProject _project;
    private readonly StreetSettings _st;
    private readonly ObservableCollection<ViaRow> _vias;
    private readonly ObservableCollection<CrossRow> _cross;
    private readonly List<FrontRow> _fronts = new();

    public ViasWindow(ParcelProject project, ProjectContext ctx, StreetSettings st)
    {
        _project = project;
        _st = st;
        _vias = new ObservableCollection<ViaRow>(project.Vias.Select(v => new ViaRow(v)));
        _cross = new ObservableCollection<CrossRow>(project.Cruzamentos.Select(c => new CrossRow(c)));
        var fmt = new ValueFormatter(ctx.Settings.Format);
        foreach (var lot in ProjectContext.SortLots(project.ActiveLots))
        {
            try { foreach (var s in ctx.Report(lot).Segments) _fronts.Add(new FrontRow(lot, s, fmt)); }
            catch { }
        }
        RefreshViaOptions();
        InitializeComponent();
        ViasGrid.ItemsSource = _vias;
        CrossGrid.ItemsSource = _cross;
        AllEdges.IsChecked = !_fronts.Any(f => f.Relevant);
        RefreshFronts();
        foreach (var v in _vias) v.PropertyChanged += (_, _) => RefreshViaOptions();
    }

    public List<IdText> ViaOptions { get; private set; } = new();

    public bool DrawNow => Draw.IsChecked == true;

    private void RefreshViaOptions()
    {
        var list = new List<IdText> { new("", "(nenhuma)") };
        list.AddRange(_vias.Select(v => new IdText(v.Via.Id, string.IsNullOrWhiteSpace(v.Nome) ? "(sem nome)" : v.Nome)));
        ViaOptions = list;
        if (FrontGrid is not null)
        {
            // Recarrega as listas das colunas de via.
            FrontGrid.Items.Refresh();
            CrossGrid.Items.Refresh();
        }
    }

    private void RefreshFronts() =>
        FrontGrid.ItemsSource = _fronts.Where(f => f.Relevant || (AllEdges.IsChecked == true && f.External)).ToList();

    private void AllEdges_Click(object sender, RoutedEventArgs e) => RefreshFronts();

    private void AddVia_Click(object sender, RoutedEventArgs e)
    {
        var v = new ViaRecord { Nome = $"Rua {_vias.Count + 1}", Largura = _st.Largura, Calcada = _st.Calcada, Prolongamento = 6 };
        var row = new ViaRow(v);
        row.PropertyChanged += (_, _) => RefreshViaOptions();
        _vias.Add(row);
        RefreshViaOptions();
        ViasGrid.SelectedItem = row;
    }

    private void RemoveVia_Click(object sender, RoutedEventArgs e)
    {
        if (ViasGrid.SelectedItem is not ViaRow r) return;
        _vias.Remove(r);
        foreach (var f in _fronts.Where(f => f.ViaId == r.Via.Id)) f.ViaId = "";
        RefreshViaOptions();
    }

    private void AddCross_Click(object sender, RoutedEventArgs e)
    {
        var c = new CruzamentoRecord { RaioEsquina = _st.RaioEsquina };
        if (_vias.Count >= 2) { c.ViaIds.Add(_vias[0].Via.Id); c.ViaIds.Add(_vias[1].Via.Id); }
        _cross.Add(new CrossRow(c));
    }

    private void RemoveCross_Click(object sender, RoutedEventArgs e)
    {
        if (CrossGrid.SelectedItem is CrossRow r) _cross.Remove(r);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ViasGrid.CommitEdit(); ViasGrid.CommitEdit();
        CrossGrid.CommitEdit(); CrossGrid.CommitEdit();
        FrontGrid.CommitEdit(); FrontGrid.CommitEdit();
        foreach (var v in _vias)
        {
            if (string.IsNullOrWhiteSpace(v.Nome)) { Fmt.Msg("Informe o nome de todas as vias."); return; }
            if (v.Largura <= 0 || v.Largura > 200 || v.Calcada < 0 || v.Calcada > 50 || v.Prolongamento < 0) { Fmt.Msg($"Medidas inválidas na via \"{v.Nome}\"."); return; }
        }
        var crossings = new List<CruzamentoRecord>();
        foreach (var c in _cross)
        {
            var ids = new[] { (c.V1, c.A1), (c.V2, c.A2), (c.V3, c.A3), (c.V4, c.A4) }.Where(x => !string.IsNullOrEmpty(x.Item1)).ToList();
            if (ids.Count < 2) continue;
            if (ids.Select(x => x.Item1).Distinct().Count() != ids.Count) { Fmt.Msg("Um cruzamento tem a mesma via repetida."); return; }
            c.Record.ViaIds = ids.Select(x => x.Item1).ToList();
            c.Record.Atravessam = ids.Where(x => x.Item2).Select(x => x.Item1).ToList();
            c.Record.RaioEsquina = Math.Clamp(c.Raio, 0, 30);
            c.Record.Continuacao = Math.Clamp(c.Continuacao, 0, 500);
            crossings.Add(c.Record);
        }
        foreach (var v in _vias)
        {
            v.Via.Nome = v.Nome.Trim(); v.Via.Largura = v.Largura; v.Via.Calcada = v.Calcada; v.Via.Prolongamento = v.Prolongamento;
        }
        _project.Vias = _vias.Select(v => v.Via).ToList();
        _project.Cruzamentos = crossings;
        foreach (var f in _fronts)
        {
            var info = ProjectContext.FindInfo(f.Lot, f.Mid);
            if (info is null)
            {
                if (f.ViaId.Length == 0 && !f.Testada) continue;
                info = new EdgeInfo { MX = f.Mid.X, MY = f.Mid.Y };
                f.Lot.EdgeInfos.Add(info);
            }
            info.Testada = f.Testada;
            if (f.ViaId.Length > 0) { info.Tipo = ConfrontanteTipo.Via; info.ViaId = f.ViaId; }
            else if (info.Tipo == ConfrontanteTipo.Via) { info.Tipo = ConfrontanteTipo.Texto; info.ViaId = ""; }
        }
        foreach (var l in _project.Lots) l.EdgeInfos.RemoveAll(i => i.IsEmpty);
        DialogResult = true;
    }
}
