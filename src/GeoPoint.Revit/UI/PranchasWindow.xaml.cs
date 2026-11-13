using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GeoPoint.Core.Model;
using GeoPoint.Core.Settings;
using GeoPoint.Core.Sheets;

namespace GeoPoint.Revit.UI;

public sealed class SourceChoice
{
    public SourceChoice(string text, OperationRecord? op) { Text = text; Operation = op; }
    public string Text { get; }
    public OperationRecord? Operation { get; }
}

public enum PranchasAction { Cancel, Generate, PickRef }

/// <summary>Escolhas da janela de pranchas mantidas entre aberturas (ex.: depois de clicar no desenho).</summary>
public sealed class PranchasState
{
    public string? OpId { get; set; }
    public bool UseOp { get; set; } = true;
    public HashSet<string> LotIds { get; } = new();
    public Dictionary<string, string> RefByKey { get; } = new();
    public Dictionary<string, SheetReplace> ReplaceByKey { get; } = new();
    public string? PickKey { get; set; }
}

public sealed class SheetRow : Notify
{
    private string _refKey;
    private SheetReplace _replace;
    public static IReadOnlyList<KeyValuePair<SheetReplace, string>> ReplaceOptions { get; } = new[]
    {
        new KeyValuePair<SheetReplace, string>(SheetReplace.Substituir, "Substituir"),
        new KeyValuePair<SheetReplace, string>(SheetReplace.Manter, "Manter a atual"),
        new KeyValuePair<SheetReplace, string>(SheetReplace.NovaVersao, "Nova versão ao lado"),
    };
    public SheetRow(SheetSpec spec, List<IdText> refs, string refKey, bool exists, bool edited, SheetReplace replace)
    {
        Spec = spec; RefOptions = refs; _refKey = refKey; Exists = exists; Edited = edited; _replace = replace;
        RefEditable = spec.Detailed;
        Title = spec.Title;
        Conteudo = spec.Detailed ? "1 lote – vértices e azimutes" : $"{spec.LotIds.Count} lotes – visualização";
        Status = !exists ? "–" : edited ? "Sim – EDITADA por você" : "Sim (sem alterações)";
    }
    public SheetSpec Spec { get; }
    public string Title { get; }
    public string Conteudo { get; }
    public List<IdText> RefOptions { get; }
    /// <summary>Só nas pranchas de um lote (nas gerais cada lote usa o seu ponto).</summary>
    public bool RefEditable { get; }
    public string RefKey { get => _refKey; set => Set(ref _refKey, value ?? ""); }
    public bool Exists { get; }
    public bool Edited { get; }
    public string Status { get; }
    public SheetReplace Replace { get => _replace; set => Set(ref _replace, value); }
}

public partial class PranchasWindow : Window
{
    private readonly ParcelProject _project;
    private readonly List<LotRow> _rows;
    private readonly PranchasState _state;
    private readonly Func<SheetSpec, List<IdText>> _refOptions;
    private readonly Func<SheetSpec, (bool exists, bool edited)> _existing;
    private List<SheetRow> _sheetRows = new();
    private bool _loading = true;
    private readonly SheetSettings _st;

    private readonly Func<SheetSpec, SheetTemplate, int, SheetComposition?>? _preview;

    public PranchasWindow(ParcelProject project, SheetSettings st, IEnumerable<LotRow> rows, OperationRecord? preselect, PranchasState state,
        Func<SheetSpec, List<IdText>> refOptions, Func<SheetSpec, (bool exists, bool edited)> existing,
        Func<SheetSpec, SheetTemplate, int, SheetComposition?>? preview = null)
    {
        InitializeComponent();
        _preview = preview;
        _project = project;
        _state = state;
        _refOptions = refOptions;
        _existing = existing;
        _rows = rows.ToList();
        if (state.LotIds.Count > 0) foreach (var r in _rows) r.Selected = state.LotIds.Contains(r.Lot.Id);
        if (state.OpId is not null || !state.UseOp) preselect = state.UseOp ? project.Operations.FirstOrDefault(o => o.Id == state.OpId) : null;
        LotsGrid.ItemsSource = _rows;
        var sources = new List<SourceChoice> { new("Lotes marcados (sem operação)", null) };
        foreach (var op in project.PhaseOperations.OrderByDescending(o => o.Date))
        {
            var parents = string.Join(", ", op.ParentIds.Select(project.FindLot).Where(l => l is not null).Select(l => l!.DisplayName));
            sources.Add(new SourceChoice($"{op.Label} – {parents}", op));
        }
        Source.ItemsSource = sources;
        Source.SelectedItem = preselect is null ? sources[0] : sources.FirstOrDefault(x => x.Operation == preselect) ?? sources[0];
        Atual.IsChecked = st.SituacaoAtual;
        Proposta.IsChecked = st.Proposta;
        IndAtual.IsChecked = st.IndividualAtual;
        IndProposta.IsChecked = st.IndividualProposta;
        Anotacoes.IsChecked = st.Anotacoes;
        Hachuras.IsChecked = st.Hachuras;
        Quadro.IsChecked = st.Quadro;
        Coordenadas.IsChecked = st.Coordenadas;
        Vias.IsChecked = st.Vias;
        Norte.IsChecked = st.NorteParaCima;
        _st = st;
        LoadTemplates(st.Template, st.Orientation == 1 ? SheetOrientation.Paisagem : SheetOrientation.Retrato);
        CroquiAuto.IsChecked = st.CroquiAuto;
        Prefix.Text = st.NumberPrefix;
        Image.Text = project.Info.ImagemLocalizacao;
        _loading = false;
        Refresh();
    }

    public PranchasAction Action { get; private set; }
    public OperationRecord? Operation => (Source.SelectedItem as SourceChoice)?.Operation;
    public List<LotRecord> SelectedLots => _rows.Where(r => r.Selected).Select(r => r.Lot).ToList();
    public List<SheetSpec> Specs { get; private set; } = new();

    public SheetRequest Request => new()
    {
        Atual = Atual.IsChecked == true, Proposta = Proposta.IsChecked == true, IndividualAtual = IndAtual.IsChecked == true,
        IndividualProposta = IndProposta.IsChecked == true, Quadro = Quadro.IsChecked == true, Hachuras = Hachuras.IsChecked == true,
        Coordenadas = Coordenadas.IsChecked == true,
    };

    public void Apply(SheetSettings st, ProjectInfo info)
    {
        st.SituacaoAtual = Atual.IsChecked == true; st.Proposta = Proposta.IsChecked == true;
        st.IndividualAtual = IndAtual.IsChecked == true; st.IndividualProposta = IndProposta.IsChecked == true;
        st.Anotacoes = Anotacoes.IsChecked == true; st.Hachuras = Hachuras.IsChecked == true; st.Quadro = Quadro.IsChecked == true;
        st.Coordenadas = Coordenadas.IsChecked == true;
        st.Vias = Vias.IsChecked == true; st.NorteParaCima = Norte.IsChecked == true;
        if (SelectedTemplate is { } t) { st.Template = t.Name; st.Orientation = t.Orientation == SheetOrientation.Paisagem ? 1 : 0; }
        st.AutoScale = Scale.SelectedIndex <= 0;
        if (!st.AutoScale && SelectedTemplate is { } t2) st.Scale = t2.ScaleList[Math.Min(Scale.SelectedIndex - 1, t2.ScaleList.Count - 1)];
        st.CroquiAuto = CroquiAuto.IsChecked == true;
        st.NumberPrefix = Prefix.Text.Trim();
        info.ImagemLocalizacao = Image.Text.Trim();
    }

    private void Changed(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (_loading) return;
        bool op = Operation is not null;
        LotsGrid.IsEnabled = !op;
        LotsTitle.Text = op ? "Lotes da operação (definidos pela operação)" : "Lotes (situação sem operação: regularização, levantamento...)";
        Proposta.IsEnabled = IndProposta.IsEnabled = op;
        if (op)
        {
            foreach (var r in _rows) r.Selected = Operation!.ParentIds.Contains(r.Lot.Id) || Operation.ChildIds.Contains(r.Lot.Id);
            Specs = SheetPlanner.ForOperation(_project, Operation!, Request);
        }
        else Specs = SheetPlanner.ForLots(SelectedLots, Request, _project.Situacao);
        SaveRows();
        _sheetRows = Specs.Select(sp =>
        {
            var (exists, edited) = _existing(sp);
            var refs = _refOptions(sp);
            string rk = _state.RefByKey.TryGetValue(sp.Key, out var k) && refs.Any(r => r.Id == k) ? k : "";
            var rep = _state.ReplaceByKey.TryGetValue(sp.Key, out var rr) ? rr : edited ? SheetReplace.Manter : SheetReplace.Substituir;
            return new SheetRow(sp, refs, rk, exists, edited, rep);
        }).ToList();
        SheetsGrid.ItemsSource = _sheetRows;
        if (_sheetRows.Count > 0 && SheetsGrid.SelectedItem is null) SheetsGrid.SelectedIndex = 0;
        ShowPreview();
        int edits = _sheetRows.Count(r => r.Edited);
        Summary.Text = Specs.Count == 0
            ? "Nenhuma prancha: marque as situações e os lotes."
            : $"{Specs.Count} prancha(s)." + (edits > 0 ? $"  ⚠ {edits} já existe(m) e foi(ram) EDITADA(S) por você: escolha em 'Se já existir' (padrão: manter a atual)." : "");
        OkButton.IsEnabled = Specs.Count > 0;
    }

    public SheetTemplate? SelectedTemplate => TemplateBox.SelectedItem as SheetTemplate;

    private void LoadTemplates(string? name, SheetOrientation fallback)
    {
        var all = SheetTemplateLibrary.LoadAll();
        TemplateBox.ItemsSource = all;
        TemplateBox.SelectedItem = all.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                                ?? all.FirstOrDefault(t => t.Orientation == fallback) ?? all.FirstOrDefault();
        FillScales();
    }

    private void FillScales()
    {
        var t = SelectedTemplate;
        var list = t?.ScaleList ?? SheetLayout.StandardScales;
        int keep = Scale.SelectedIndex > 0 && Scale.SelectedItem is string cur && int.TryParse(cur.Replace("1:", ""), out int v) ? v : (_st.AutoScale ? 0 : _st.Scale);
        var scales = new List<string> { "Automática (a que melhor cabe na folha)" };
        scales.AddRange(list.Select(x => "1:" + x));
        Scale.ItemsSource = scales;
        int idx = keep > 0 ? list.ToList().IndexOf(keep) : -1;
        Scale.SelectedIndex = idx >= 0 ? idx + 1 : 0;
    }

    private void Template_Changed(object sender, SelectionChangedEventArgs e) { if (!_loading) { FillScales(); ShowPreview(); } }

    private void SheetsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowPreview();

    /// <summary>Prévia da prancha selecionada (mesmo encaixe, carimbo e anotações que o Revit vai gerar).</summary>
    private void ShowPreview()
    {
        if (_preview is null || SheetPreview is null) return;
        var row = SheetsGrid.SelectedItem as SheetRow ?? _sheetRows.FirstOrDefault();
        var tpl = SelectedTemplate;
        if (row is null || tpl is null) { SheetPreview.Show(tpl ?? SheetTemplate.Default(), new List<SheetPrim>()); return; }
        try
        {
            int scale = Scale.SelectedIndex > 0 ? tpl.ScaleList[Math.Min(Scale.SelectedIndex - 1, tpl.ScaleList.Count - 1)] : 0;
            var comp = _preview(row.Spec, tpl, scale);
            if (comp is null) return;
            SheetPreview.Show(tpl, comp.All.ToList());
            PreviewInfo.Text = $"{row.Title} – escala 1:{comp.Fit.Scale}." + (comp.Warnings.Count > 0 ? "  ⚠ " + string.Join(" ", comp.Warnings) : "");
        }
        catch (Exception ex) { PreviewInfo.Text = "Prévia indisponível: " + ex.Message; }
    }

    private void Templates_Click(object sender, RoutedEventArgs e)
    {
        var w = new SheetTemplatesWindow(SelectedTemplate?.Name) { Owner = this };
        w.ShowDialog();
        LoadTemplates(w.ChosenName ?? SelectedTemplate?.Name, SheetOrientation.Retrato);
    }

    private void Image_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Imagem de localização", Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|Todos|*.*" };
        if (dlg.ShowDialog(this) == true) Image.Text = dlg.FileName;
    }

    /// <summary>Guarda as escolhas da tabela (sobrevivem a mudanças de opção e a cliques no desenho).</summary>
    private void SaveRows()
    {
        if (SheetsGrid is null) return;
        SheetsGrid.CommitEdit(); SheetsGrid.CommitEdit();
        foreach (var r in _sheetRows)
        {
            _state.RefByKey[r.Spec.Key] = r.RefKey;
            if (r.Exists) _state.ReplaceByKey[r.Spec.Key] = r.Replace;
        }
        _state.UseOp = Operation is not null;
        _state.OpId = Operation?.Id;
        _state.LotIds.Clear();
        foreach (var r in _rows.Where(x => x.Selected)) _state.LotIds.Add(r.Lot.Id);
    }

    private void PickRef_Click(object sender, RoutedEventArgs e)
    {
        if (SheetsGrid.SelectedItem is not SheetRow row) { Fmt.Msg("Selecione a prancha na tabela."); return; }
        SaveRows();
        _state.PickKey = row.Spec.Key;
        Action = PranchasAction.PickRef;
        DialogResult = true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SaveRows();
        if (Specs.Count == 0) return;
        var replacing = _sheetRows.Where(r => r.Exists && r.Edited && r.Replace == SheetReplace.Substituir).ToList();
        if (replacing.Count > 0 && MessageBox.Show(
                "Estas pranchas foram editadas por você e serão SUBSTITUÍDAS (as edições se perdem):\n\n" + string.Join("\n", replacing.Select(r => "• " + r.Title)) + "\n\nContinuar?",
                "GeoPoint", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        foreach (var r in _sheetRows) r.Spec.Replace = r.Exists ? r.Replace : SheetReplace.Substituir;
        Action = PranchasAction.Generate;
        DialogResult = true;
    }
}
