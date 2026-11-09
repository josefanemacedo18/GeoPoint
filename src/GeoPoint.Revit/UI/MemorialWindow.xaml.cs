using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Settings;

namespace GeoPoint.Revit.UI;

public partial class MemorialWindow : Window
{
    private readonly ParcelProject _project;
    private readonly GeoPointSettings _settings;
    private readonly List<LotRow> _rows;
    private readonly List<CrsChoice> _primary;
    private readonly List<CrsChoice> _table;
    private readonly IntPtr _owner;

    public MemorialWindow(ParcelProject project, GeoPointSettings settings, ProjectContext ctx, ISet<string> preselect, IntPtr owner,
        MemorialKind? initialKind = null, OperationRecord? initialOperation = null)
    {
        InitializeComponent();
        _project = project;
        _settings = settings;
        _owner = owner;
        _rows = ProjectContext.SortLots(project.ActiveLots).Select(l =>
        {
            int missing = 0; double area = 0;
            try { var r = ctx.Report(l); area = r.AreaLocal; missing = r.Segments.Count(s => string.IsNullOrWhiteSpace(s.Confrontante)); } catch { }
            return new LotRow(l, area, missing > 0 ? $"{missing} divisa(s) sem confrontante" : "") { Selected = preselect.Contains(l.Id) };
        }).ToList();
        if (!_rows.Any(r => r.Selected) && _rows.Count == 1) _rows[0].Selected = true;
        LotsGrid.ItemsSource = _rows;

        var m = settings.Memorial;
        _primary = CrsChoice.Build(settings);
        _table = CrsChoice.Build(settings);
        PrimaryCrs.ItemsSource = _primary;
        PrimaryCrs.SelectedItem = (m.CoordinateSystems.Count > 0 ? CrsChoice.Match(_primary, m.CoordinateSystems[0]) : null) ?? _primary[0];
        foreach (var d in m.CoordinateSystems) { var c = CrsChoice.Match(_table, d); if (c is not null) c.Checked = true; }
        if (!_table.Any(c => c.Checked)) _table[0].Checked = true;
        TableCrs.ItemsSource = _table;

        AngleStyleBox.SelectedIndex = (int)settings.Format.AngleStyle;
        AzTypeBox.SelectedIndex = (int)m.AzimuthType;
        DistTypeBox.SelectedIndex = (int)m.DistanceType;
        AreaTypeBox.SelectedIndex = (int)m.AreaType;
        AreaUnitBox.SelectedIndex = (int)settings.Format.AreaUnit;
        DirectionBox.SelectedIndex = settings.Naming.Clockwise ? 0 : 1;
        Prefix.Text = settings.Naming.VertexPrefix;
        Digits.Text = settings.Naming.VertexDigits.ToString();
        Numbering.SelectedIndex = (int)settings.Naming.Numbering;
        Tables.IsChecked = m.IncludeVertexTable;
        Notes.IsChecked = m.IncludeSystemDeclaration;
        Altitudes.IsChecked = m.IncludeAltitude;
        OnePerLot.IsChecked = m.OneFilePerLot;
        OpenAfter.IsChecked = m.OpenAfterGenerate;
        Date.SelectedDate = DateTime.Today;
        TemplateInfo.Text = string.IsNullOrWhiteSpace(m.TemplatePath) || !System.IO.File.Exists(m.TemplatePath)
            ? "Modelo: padrão do GeoPoint (altere em Configurações)"
            : "Modelo: " + System.IO.Path.GetFileName(m.TemplatePath);
        if (initialKind == MemorialKind.Desmembramento) KindSplit.IsChecked = true;
        else if (initialKind == MemorialKind.Remembramento) KindMerge.IsChecked = true;
        else if (initialKind == MemorialKind.Loteamento) KindPlat.IsChecked = true;
        Kind_Changed(this, new RoutedEventArgs());
        if (initialOperation is not null && Operations.ItemsSource is IEnumerable<OperationRecord> ops)
            Operations.SelectedItem = ops.FirstOrDefault(o => o.Id == initialOperation.Id);
    }

    public MemorialKind Kind { get; private set; }
    public List<LotRecord> Lots { get; private set; } = new();
    public OperationRecord? Operation { get; private set; }
    /// <summary>Desmembramento: true = memoriais individuais dos lotes resultantes.</summary>
    public bool SplitIndividual { get; private set; }
    /// <summary>Desmembramento: true = memorial geral.</summary>
    public bool SplitGeneral { get; private set; }
    public DateTime MemorialDate { get; private set; } = DateTime.Today;
    public bool InfoChanged { get; private set; }

    private void Kind_Changed(object sender, RoutedEventArgs e)
    {
        if (LotsPanel is null) return;
        bool lots = KindLots.IsChecked == true;
        bool op = KindSplit.IsChecked == true || KindMerge.IsChecked == true;
        LotsPanel.Visibility = lots ? Visibility.Visible : Visibility.Collapsed;
        OperationPanel.Visibility = op ? Visibility.Visible : Visibility.Collapsed;
        PlatHint.Visibility = KindPlat.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SplitModePanel.Visibility = KindSplit.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (op)
        {
            var type = KindSplit.IsChecked == true ? OperationType.Desmembramento : OperationType.Remembramento;
            var ops = _project.PhaseOperations.Where(o => o.Type == type).OrderByDescending(o => o.Date).ToList();
            Operations.ItemsSource = ops;
            Operations.SelectedIndex = ops.Count > 0 ? 0 : -1;
            OperationHint.Text = ops.Count == 0
                ? $"Nenhum {(type == OperationType.Desmembramento ? "desmembramento" : "remembramento")} registrado. Use o botão correspondente no painel Operações."
                : "";
        }
    }

    private void All_Click(object sender, RoutedEventArgs e) { foreach (var r in _rows) r.Selected = true; }
    private void None_Click(object sender, RoutedEventArgs e) { foreach (var r in _rows) r.Selected = false; }

    private void Info_Click(object sender, RoutedEventArgs e)
    {
        var w = new ProjectInfoWindow(_project.Info, _settings) { Owner = this };
        if (w.ShowDialog() == true) InfoChanged = true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SplitIndividual = SplitMode.SelectedIndex != 1;
        SplitGeneral = SplitMode.SelectedIndex != 0;
        if (!int.TryParse(Digits.Text.Trim(), out int digits) || digits < 1 || digits > 5)
        {
            Fmt.Msg("Dígitos dos vértices: informe de 1 a 5.", icon: MessageBoxImage.Warning);
            return;
        }
        var checkedCrs = _table.Where(c => c.Checked).ToList();
        if (checkedCrs.Count == 0) { Fmt.Msg("Marque pelo menos um sistema de coordenadas para os quadros."); return; }
        if (checkedCrs.Count > 3) { Fmt.Msg("Marque no máximo 3 sistemas de coordenadas para os quadros."); return; }

        if (KindLots.IsChecked == true)
        {
            Kind = MemorialKind.Lotes;
            Lots = _rows.Where(r => r.Selected).Select(r => r.Lot).ToList();
            if (Lots.Count == 0) { Fmt.Msg("Selecione pelo menos um lote."); return; }
        }
        else if (KindSplit.IsChecked == true || KindMerge.IsChecked == true)
        {
            Kind = KindSplit.IsChecked == true ? MemorialKind.Desmembramento : MemorialKind.Remembramento;
            Operation = Operations.SelectedItem as OperationRecord;
            if (Operation is null) { Fmt.Msg("Escolha a operação registrada."); return; }
            var ids = Kind == MemorialKind.Desmembramento ? Operation.ParentIds.Concat(Operation.ChildIds) : Operation.ChildIds.Concat(Operation.ParentIds);
            Lots = ids.Select(id => _project.FindLot(id)).OfType<LotRecord>().ToList();
            if (Lots.Count < 2) { Fmt.Msg("Os lotes desta operação não existem mais no projeto."); return; }
        }
        else
        {
            Kind = MemorialKind.Loteamento;
            Lots = _project.PhaseLots.Where(l => l.Status == LotStatus.Ativo || l.Uso == LotUse.Gleba).ToList();
            if (Lots.Count == 0) { Fmt.Msg("Não há lotes no projeto."); return; }
        }

        // Grava as escolhas nas configurações.
        var primary = (PrimaryCrs.SelectedItem as CrsChoice) ?? _primary[0];
        var list = new List<CrsDef> { primary.Def.Clone() };
        foreach (var c in checkedCrs)
            if (c.Key != primary.Key) list.Add(c.Def.Clone());
        var m = _settings.Memorial;
        m.CoordinateSystems = list;
        // A tabela usa a lista inteira; quando o principal não foi marcado nos quadros, ele entra mesmo assim.
        _settings.Format.AngleStyle = (AngleStyle)Math.Max(0, AngleStyleBox.SelectedIndex);
        m.AzimuthType = (AzimuthType)Math.Max(0, AzTypeBox.SelectedIndex);
        m.DistanceType = (DistanceType)Math.Max(0, DistTypeBox.SelectedIndex);
        m.AreaType = (AreaType)Math.Max(0, AreaTypeBox.SelectedIndex);
        _settings.Format.AreaUnit = (AreaUnit)Math.Max(0, AreaUnitBox.SelectedIndex);
        _settings.Naming.Clockwise = DirectionBox.SelectedIndex != 1;
        _settings.Naming.VertexPrefix = Prefix.Text;
        _settings.Naming.VertexDigits = digits;
        _settings.Naming.Numbering = (VertexNumbering)Math.Max(0, Numbering.SelectedIndex);
        m.IncludeVertexTable = Tables.IsChecked == true;
        m.IncludeSystemDeclaration = Notes.IsChecked == true;
        m.IncludeAltitude = Altitudes.IsChecked == true;
        m.OneFilePerLot = OnePerLot.IsChecked == true;
        m.OpenAfterGenerate = OpenAfter.IsChecked == true;
        MemorialDate = Date.SelectedDate ?? DateTime.Today;
        DialogResult = true;
    }
}
