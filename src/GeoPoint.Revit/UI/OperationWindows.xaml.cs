using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;

namespace GeoPoint.Revit.UI;

/// <summary>Janela de desmembramento (merge = false) ou remembramento (merge = true).</summary>
public partial class SubdivisionWindow : Window
{
    private readonly ParcelProject _project;
    private readonly bool _merge;
    private readonly List<LotRow> _all;
    private List<LotRow> _visible = new();
    private bool _ok;

    public SubdivisionWindow(ParcelProject project, bool merge, ISet<string> preselect)
    {
        InitializeComponent();
        _project = project;
        _merge = merge;
        _all = ProjectContext.SortLots(project.ActiveLots).Select(l => new LotRow(l, SafeArea(l))).ToList();
        ResultUse.ItemsSource = UseOptions.All;
        ResultUse.SelectedValue = LotUse.Lote;
        if (merge)
        {
            Title = "GeoPoint – Remembramento";
            Header.Text = "Remembramento (unificação de lotes)";
            Intro.Text = "Marque os lotes contíguos que serão unidos. O contorno da área unificada é calculado com as divisas originais (as linhas internas deixam de ser divisas).";
            ParentPanel.Visibility = Visibility.Collapsed;
            ListTitle.Text = "Lotes a remembrar";
            Archive.Content = "Arquivar os lotes originais (continuam disponíveis para o memorial da operação)";
            OkButton.Content = "Registrar remembramento";
            foreach (var r in _all) r.Selected = preselect.Contains(r.Lot.Id);
            _visible = _all;
            Grid.ItemsSource = _visible;
            var first = _all.FirstOrDefault(r => r.Selected)?.Lot;
            ResultQuadra.Text = first?.Quadra ?? "";
            ResultName.Text = first is null ? "" : first.Nome + "-R";
        }
        else
        {
            Title = "GeoPoint – Desmembramento";
            Header.Text = "Desmembramento";
            Intro.Text = "1) Desenhe as novas divisas no Revit e reconheça os lotes resultantes. 2) Aqui, escolha o lote original e confira os lotes resultantes (sugeridos automaticamente: os que estão dentro dele).";
            ResultPanel.Visibility = Visibility.Collapsed;
            ListTitle.Text = "Lotes resultantes";
            Archive.Content = "Arquivar o lote original (continua disponível para o memorial da operação)";
            OkButton.Content = "Registrar desmembramento";
            ParentBox.ItemsSource = _all;
            var pre = _all.FirstOrDefault(r => preselect.Contains(r.Lot.Id) && _all.Count(x => x != r && Inside(r.Lot, x.Lot)) >= 2)
                      ?? _all.OrderByDescending(r => r.Area).FirstOrDefault();
            ParentBox.SelectedItem = pre;
        }
        Validate();
    }

    public List<LotRecord> SelectedLots => _visible.Where(r => r.Selected).Select(r => r.Lot).ToList();
    public LotRecord? ParentLot => (ParentBox.SelectedItem as LotRow)?.Lot;
    public UnionResult? Union { get; private set; }
    public string ResultNameText => ResultName.Text.Trim();
    public string ResultQuadraText => ResultQuadra.Text.Trim();
    public LotUse ResultUseValue => ResultUse.SelectedValue is LotUse u ? u : LotUse.Lote;
    public string ResultMatriculaText => ResultMatricula.Text.Trim();
    public bool ArchiveOriginals => Archive.IsChecked == true;
    public string NotesText => Notes.Text.Trim();

    private static double SafeArea(LotRecord l) { try { return l.ToRegion().Area; } catch { return 0; } }

    private static bool Inside(LotRecord parent, LotRecord child)
    {
        try
        {
            var pr = parent.ToRegion();
            var cr = child.ToRegion();
            if (!pr.Bounds.Expand(0.01).Intersects(cr.Bounds)) return false;
            return pr.Contains(cr.InteriorPoint()) && cr.Area < pr.Area;
        }
        catch { return false; }
    }

    private void Parent_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_merge) return;
        var parent = ParentLot;
        _visible = _all.Where(r => parent is null || r.Lot.Id != parent.Id).ToList();
        foreach (var r in _visible) r.Selected = parent is not null && Inside(parent, r.Lot);
        Grid.ItemsSource = _visible;
        Validate();
    }

    private void Check_Click(object sender, RoutedEventArgs e) => Validate();

    private void Validate()
    {
        if (Check is null) return;
        var sel = SelectedLots;
        OperationCheck chk;
        try
        {
            if (_merge)
            {
                if (sel.Count < 2) { Check.Text = "Marque pelo menos dois lotes."; _ok = false; OkButton.IsEnabled = false; return; }
                chk = ParcelOperations.CheckMerge(sel, out var u);
                Union = u;
            }
            else
            {
                var parent = ParentLot;
                if (parent is null) { Check.Text = "Escolha o lote original."; _ok = false; OkButton.IsEnabled = false; return; }
                chk = ParcelOperations.CheckSubdivision(parent, sel);
            }
        }
        catch (Exception ex)
        {
            Check.Text = "Não foi possível conferir: " + ex.Message;
            _ok = false;
            OkButton.IsEnabled = false;
            return;
        }
        var sb = new StringBuilder();
        if (_merge) sb.AppendLine($"Soma das áreas originais: {Fmt.N(chk.ParentArea, 2)} m²   •   Área unificada: {Fmt.N(chk.ChildrenArea, 2)} m²");
        else sb.AppendLine($"Área original: {Fmt.N(chk.ParentArea, 2)} m²   •   Soma das áreas resultantes: {Fmt.N(chk.ChildrenArea, 2)} m²   •   Diferença: {Fmt.N(chk.Difference, 2)} m²");
        foreach (var x in chk.Errors) sb.AppendLine("✖ " + x);
        foreach (var x in chk.Warnings) sb.AppendLine("⚠ " + x);
        foreach (var x in chk.Infos) sb.AppendLine("✔ " + x);
        Check.Text = sb.ToString().TrimEnd();
        _ok = chk.Ok;
        OkButton.IsEnabled = chk.Ok;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Validate();
        if (!_ok) return;
        if (_merge && string.IsNullOrWhiteSpace(ResultNameText)) { Fmt.Msg("Informe o nome do lote resultante."); return; }
        DialogResult = true;
    }
}
