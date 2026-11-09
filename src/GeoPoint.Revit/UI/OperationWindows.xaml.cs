using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;

namespace GeoPoint.Revit.UI;

/// <summary>Janela de remembramento com prévia do lote unificado.</summary>
public partial class SubdivisionWindow : Window
{
    private readonly List<LotRow> _all;
    private readonly double _rotation;
    private bool _ok;
    private bool _nameEdited;
    private bool _settingName;

    public SubdivisionWindow(ParcelProject project, bool merge, ISet<string> preselect, double rotationRad)
    {
        InitializeComponent();
        _rotation = rotationRad;
        _all = ProjectContext.SortLots(project.ActiveLots).Select(l => new LotRow(l, SafeArea(l))).ToList();
        ResultUse.ItemsSource = UseOptions.All;
        ResultUse.SelectedValue = LotUse.Lote;
        foreach (var r in _all) r.Selected = preselect.Contains(r.Lot.Id);
        Grid.ItemsSource = _all;
        Loaded += (_, _) => Validate();
    }

    public List<LotRecord> SelectedLots => _all.Where(r => r.Selected).Select(r => r.Lot).ToList();
    public UnionResult? Union { get; private set; }
    public string ResultNameText => ResultName.Text.Trim();
    public string ResultQuadraText => ResultQuadra.Text.Trim();
    public LotUse ResultUseValue => ResultUse.SelectedValue is LotUse u ? u : LotUse.Lote;
    public string ResultMatriculaText => ResultMatricula.Text.Trim();
    public string ResultOwnerText => ResultOwner.Text.Trim();
    public bool SheetsValue => Sheets.IsChecked == true;
    public string NotesText => Notes.Text.Trim();

    private static double SafeArea(LotRecord l) { try { return l.ToRegion().Area; } catch { return 0; } }

    private void Check_Click(object sender, RoutedEventArgs e) => Validate();

    private void Name_Changed(object sender, TextChangedEventArgs e) { if (!_settingName) _nameEdited = true; }

    private void SuggestName()
    {
        var sel = SelectedLots;
        if (_nameEdited || sel.Count == 0) return;
        _settingName = true;
        // Sugestão: menor número dos lotes unidos (ex.: lotes 03 e 04 → "03-04").
        ResultName.Text = sel.Count == 1 ? sel[0].Nome : string.Join("-", sel.Select(l => l.Nome));
        ResultQuadra.Text = sel[0].Quadra;
        if (string.IsNullOrWhiteSpace(ResultMatricula.Text)) ResultMatricula.Text = sel[0].Matricula;
        _settingName = false;
    }

    private void Validate()
    {
        if (Check is null) return;
        SuggestName();
        var sel = SelectedLots;
        Union = null;
        OperationCheck? chk = null;
        string? error = null;
        try
        {
            if (sel.Count < 2) error = "Marque pelo menos dois lotes.";
            else { chk = ParcelOperations.CheckMerge(sel, out var u); Union = u; }
        }
        catch (Exception ex) { error = "Não foi possível conferir: " + ex.Message; }
        DrawPreview();
        if (error is not null || chk is null)
        {
            Check.Text = error ?? "";
            _ok = false;
            OkButton.IsEnabled = false;
            return;
        }
        var sb = new StringBuilder();
        sb.AppendLine($"Soma das áreas originais: {Fmt.N(chk.ParentArea, 2)} m²   •   Área unificada: {Fmt.N(chk.ChildrenArea, 2)} m²");
        foreach (var x in chk.Errors) sb.AppendLine("✖ " + x);
        foreach (var x in chk.Warnings) sb.AppendLine("⚠ " + x);
        foreach (var x in chk.Infos) sb.AppendLine("✔ " + x);
        Check.Text = sb.ToString().TrimEnd();
        _ok = chk.Ok;
        OkButton.IsEnabled = chk.Ok;
    }

    private void DrawPreview()
    {
        var items = new List<PreviewItem>();
        var sel = SelectedLots.Select(l => l.Id).ToHashSet();
        // Contexto: lotes vizinhos próximos dos selecionados.
        BBox? focus = null;
        foreach (var r in _all.Where(r => r.Selected))
            try { var b = r.Lot.ToRegion().Bounds; focus = focus is null ? b : focus.Value.Union(b); } catch { }
        int ci = 0;
        foreach (var r in _all)
        {
            Region reg;
            try { reg = r.Lot.ToRegion(); } catch { continue; }
            if (sel.Contains(r.Lot.Id))
            {
                var c = PreviewCanvas.Palette[ci++ % PreviewCanvas.Palette.Length];
                items.Add(PreviewItem.FromRegion(reg, c, $"{r.Lot.Nome}\n{Fmt.N(r.Area, 2)} m²", true, Color.FromRgb(110, 110, 110), 0.8));
            }
            else if (focus is null || focus.Value.Expand(Math.Max(focus.Value.Width, focus.Value.Height) * 0.6 + 5).Intersects(reg.Bounds))
                items.Add(PreviewItem.FromRegion(reg, Color.FromRgb(200, 200, 200), r.Lot.Nome, false, Color.FromRgb(170, 170, 170), 0.8));
        }
        if (Union is { Ok: true } u)
        {
            try
            {
                var reg = new LotRecord { Outer = u.Outer, Holes = u.Holes }.ToRegion();
                items.Add(PreviewItem.FromRegion(reg, Colors.Transparent, null, false, Color.FromRgb(210, 30, 30), 2.6));
                items.Add(new PreviewItem { Label = $"{ResultNameText}\n{Fmt.N(u.Area, 2)} m²", LabelAt = reg.InteriorPoint() });
            }
            catch { }
        }
        Preview.SetItems(items, _rotation);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Validate();
        if (!_ok) return;
        if (string.IsNullOrWhiteSpace(ResultNameText)) { Fmt.Msg("Informe o nome do lote resultante."); return; }
        DialogResult = true;
    }
}
