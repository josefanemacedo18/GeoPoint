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
    public string MeioFioText => Via.MeioFio.Count == 0 ? "–" : $"{Via.MeioFio.Count} linha(s)";
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
    private readonly ProjectContext _ctx;

    public ViasWindow(ParcelProject project, ProjectContext ctx, StreetSettings st)
    {
        _project = project;
        _st = st;
        _ctx = ctx;
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
        Loaded += (_, _) => ShowPreview();
    }

    private void Preview_Click(object sender, RoutedEventArgs e) => ShowPreview();

    /// <summary>Desenho esquemático com as edições atuais (ainda não gravadas: Cancelar descarta).</summary>
    private void ShowPreview()
    {
        var items = new List<PreviewItem>();
        try
        {
            if (!Commit(quiet: true)) return;
            _ctx.Invalidate();
            var lots = _project.ActiveLots.ToList();
            var regions = lots.Select(l => { try { return l.ToRegion(); } catch { return null; } }).Where(r => r is not null).Select(r => r!).ToList();
            foreach (var r in regions) items.Add(PreviewItem.FromRegion(r, System.Windows.Media.Color.FromRgb(230, 150, 30), null, true, System.Windows.Media.Color.FromRgb(30, 30, 30), 1.2));
            var fronts = GeoPoint.Revit.Infrastructure.StreetDrawer.Frontages(_ctx, lots);
            var vias = _project.Vias.Where(v => fronts.Any(f => f.ViaId == v.Id) || v.MeioFio.Count > 0).ToList();
            if (vias.Count == 0) { PreviewInfo.Text = "Associe as testadas às vias (aba Testadas) para ver o desenho."; Preview.SetItems(items, _ctx.Coords.Rotation); return; }
            var crossings = _project.Cruzamentos.Where(c => c.ViaIds.Count(id => vias.Any(v => v.Id == id)) >= 2).ToList();
            var d = GeoPoint.Core.Urban.StreetSchematic.Build(vias, crossings, fronts, regions);
            foreach (var c in d.Curbs) items.Add(new PreviewItem { Line = c, Stroke = System.Windows.Media.Color.FromRgb(60, 60, 60), StrokeWidth = 1.6 });
            foreach (var a in d.Alignments) items.Add(new PreviewItem { Line = a, Stroke = System.Windows.Media.Color.FromRgb(140, 140, 140), StrokeWidth = 1, Dashed = true });
            double h = _st.NomeMm / 1000.0 * 500;   // nome da rua numa escala de referência de 1:500
            foreach (var l in d.Labels) items.Add(new PreviewItem { Label = l.Text.ToUpperInvariant(), LabelAt = l.Position, LabelAngle = l.AngleRad, LabelSizeM = h, LabelBoldFirst = true });
            PreviewInfo.Text = $"{vias.Count} via(s), {crossings.Count} cruzamento(s)." + (d.Warnings.Count > 0 ? "  ⚠ " + string.Join(" ", d.Warnings.Take(3)) : "");
        }
        catch (Exception ex) { PreviewInfo.Text = "Prévia indisponível: " + ex.Message; }
        Preview.SetItems(items, _ctx.Coords.Rotation);
    }

    public List<IdText> ViaOptions { get; private set; } = new();

    public bool DrawNow => Draw.IsChecked == true;
    /// <summary>Via para a seleção no desenho (testadas e meio-fio); null = fechar normalmente.</summary>
    public ViaRecord? PickVia { get; private set; }

    private void Pick_Click(object sender, RoutedEventArgs e)
    {
        if (ViasGrid.SelectedItem is not ViaRow row)
        {
            AddVia_Click(sender, e);
            row = (ViaRow)ViasGrid.SelectedItem;
            row.Prolongamento = 0;
        }
        if (!Commit()) return;
        PickVia = row.Via;
        DialogResult = true;
    }

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
        if (Commit()) DialogResult = true;
    }

    /// <summary>Valida e grava as edições no projeto (em memória).</summary>
    private bool Commit(bool quiet = false)
    {
        ViasGrid.CommitEdit(); ViasGrid.CommitEdit();
        CrossGrid.CommitEdit(); CrossGrid.CommitEdit();
        FrontGrid.CommitEdit(); FrontGrid.CommitEdit();
        foreach (var v in _vias)
        {
            if (string.IsNullOrWhiteSpace(v.Nome)) { if (!quiet) Fmt.Msg("Informe o nome de todas as vias."); else PreviewInfo.Text = "Informe o nome de todas as vias."; return false; }
            if (v.Largura <= 0 || v.Largura > 200 || v.Calcada < 0 || v.Calcada > 50 || v.Prolongamento < 0) { if (!quiet) Fmt.Msg($"Medidas inválidas na via \"{v.Nome}\"."); else PreviewInfo.Text = $"Medidas inválidas na via \"{v.Nome}\"."; return false; }
        }
        var crossings = new List<CruzamentoRecord>();
        foreach (var c in _cross)
        {
            var ids = new[] { (c.V1, c.A1), (c.V2, c.A2), (c.V3, c.A3), (c.V4, c.A4) }.Where(x => !string.IsNullOrEmpty(x.Item1)).ToList();
            if (ids.Count < 2) continue;
            if (ids.Select(x => x.Item1).Distinct().Count() != ids.Count) { if (!quiet) Fmt.Msg("Um cruzamento tem a mesma via repetida."); else PreviewInfo.Text = "Um cruzamento tem a mesma via repetida."; return false; }
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
        return true;
    }
}
