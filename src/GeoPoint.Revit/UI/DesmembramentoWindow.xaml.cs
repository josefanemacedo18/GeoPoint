using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Memorial;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;

namespace GeoPoint.Revit.UI;

public enum DesmModo { Automatico = 0, Linhas = 1, Lotes = 2 }
public enum DesmAction { Cancel, PickLines, Register }

/// <summary>Edição de uma parte resultante (preservada entre recálculos).</summary>
public sealed class PieceEdit
{
    public string Nome { get; set; } = "";
    public string Proprietario { get; set; } = "";
    public string AnexarA { get; set; } = "";
    public bool Remanescente { get; set; }
}

/// <summary>Estado da janela (mantido quando ela fecha para selecionar linhas no Revit).</summary>
public sealed class DesmembramentoState
{
    public string? ParentId { get; set; }
    public DesmModo Modo { get; set; }
    public int FrontIndex { get; set; } = -1;
    public bool FromStart { get; set; } = true;
    public int Criterio { get; set; }
    public string Parts { get; set; } = "2";
    public string Target { get; set; } = "250";
    public string Areas { get; set; } = "300; 250";
    public string Width { get; set; } = "10";
    public string Fronts { get; set; } = "12; 12; 6";
    public string MinRem { get; set; } = "125";
    public string Snap { get; set; } = "0,30";
    public int Direcao { get; set; }
    public int ParIndex { get; set; } = -1;
    public string Az { get; set; } = "0°00'00\"";
    public List<(Curve2 Curve, string Uid)> LineCuts { get; set; } = new();
    public HashSet<string> ChildLotIds { get; set; } = new();
    public Dictionary<int, PieceEdit> Edits { get; } = new();
    public string? Quadra { get; set; }
    public string StartNumber { get; set; } = "01";
    public bool Sheets { get; set; } = true;
    public string Notes { get; set; } = "";
}

public sealed class PieceRow : Notify
{
    private readonly PieceEdit _e;
    public PieceRow(int index, PlannedPiece piece, PieceEdit e, string situacao)
    {
        Index = index; Piece = piece; _e = e; Situacao = situacao;
    }
    public int Index { get; }
    public PlannedPiece Piece { get; }
    public LotRecord? ExistingLot { get; init; }
    public string AreaText => Fmt.N(Piece.Area, 2);
    public string TestadaText => Piece.Frontage > 0 ? Fmt.N(Piece.Frontage, 2) : "–";
    public string Situacao { get; }
    public string Nome { get => _e.Nome; set { _e.Nome = value ?? ""; OnChanged(); } }
    public string Proprietario { get => _e.Proprietario; set { _e.Proprietario = value ?? ""; OnChanged(); } }
    public string AnexarA { get => _e.AnexarA; set { _e.AnexarA = value ?? ""; OnChanged(); } }
    public bool Remanescente { get => _e.Remanescente; set { _e.Remanescente = value; OnChanged(); } }
}

public sealed record IdText(string Id, string Text);

public partial class DesmembramentoWindow : Window
{
    private readonly ParcelProject _project;
    private readonly ProjectContext _ctx;
    private readonly ValueFormatter _fmt;
    private readonly DesmembramentoState _st;
    private bool _loading = true;
    private List<LotRow> _childRows = new();

    public DesmembramentoWindow(ParcelProject project, ProjectContext ctx, DesmembramentoState state)
    {
        InitializeComponent();
        _project = project;
        _ctx = ctx;
        _fmt = new ValueFormatter(ctx.Settings.Format);
        _st = state;
        var parents = ProjectContext.SortLots(project.ActiveLots.Where(l => !LotUseInfo.IsDeduction(l.Uso))).Select(l => new LotRow(l, SafeArea(l))).ToList();
        ParentBox.ItemsSource = parents;
        ParentBox.SelectedItem = parents.FirstOrDefault(p => p.Lot.Id == state.ParentId) ?? parents.OrderByDescending(p => p.Area).FirstOrDefault();
        ModeAuto.IsChecked = state.Modo == DesmModo.Automatico;
        ModeLines.IsChecked = state.Modo == DesmModo.Linhas;
        ModeLots.IsChecked = state.Modo == DesmModo.Lotes;
        FromStart.IsChecked = state.FromStart; FromEnd.IsChecked = !state.FromStart;
        (state.Criterio switch { 1 => CArea, 2 => CAreas, 3 => CFront, 4 => CFronts, _ => CEqual }).IsChecked = true;
        Parts.Text = state.Parts; Target.Text = state.Target; Areas.Text = state.Areas; Width1.Text = state.Width; Fronts.Text = state.Fronts; MinRem.Text = state.MinRem; SnapTol.Text = state.Snap;
        (state.Direcao switch { 1 => DirPar, 2 => DirAz, _ => DirPerp }).IsChecked = true;
        AzText.Text = state.Az;
        StartNumber.Text = state.StartNumber;
        Sheets.IsChecked = state.Sheets;
        Notes.Text = state.Notes;
        var rules = ctx.Rules;
        if (rules is { AreaMinimaLote: > 0 } && state.MinRem == "125") MinRem.Text = Fmt.Raw(rules.AreaMinimaLote, 2);
        _loading = false;
        LoadParent(keepIndexes: true);
    }

    public DesmAction Action { get; private set; } = DesmAction.Cancel;
    public LotRecord? ParentLot => (ParentBox.SelectedItem as LotRow)?.Lot;
    public List<PieceRow> Rows { get; private set; } = new();
    public SplitResult? AutoResult { get; private set; }
    public DesmModo Modo => ModeLines.IsChecked == true ? DesmModo.Linhas : ModeLots.IsChecked == true ? DesmModo.Lotes : DesmModo.Automatico;
    public List<IdText> AnexarOptions { get; private set; } = new();

    private static double SafeArea(LotRecord l) { try { return l.ToRegion().Area; } catch { return 0; } }

    private void Parent_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _st.Edits.Clear();
        LoadParent(keepIndexes: false);
    }

    private void LoadParent(bool keepIndexes)
    {
        var parent = ParentLot;
        if (parent is null) { Status.Text = "Não há lotes ativos para desmembrar."; OkButton.IsEnabled = false; return; }
        _loading = true;
        try
        {
            var rep = _ctx.Report(parent);
            var edges = rep.Segments.Select(s => new EdgeChoice($"{s.From.Name} – {s.To.Name} · {_fmt.Distance(s.LengthHorizontal)}{(s.Testada ? " · testada" : "")}", s.Curve, s.Testada)).ToList();
            FrontBox.ItemsSource = edges;
            ParBox.ItemsSource = edges;
            FrontBox.SelectedIndex = keepIndexes && _st.FrontIndex >= 0 && _st.FrontIndex < edges.Count ? _st.FrontIndex
                : Math.Max(0, edges.FindIndex(x => x.Testada) is int i && i >= 0 ? i : edges.IndexOf(edges.OrderByDescending(x => x.Curve.Length).First()));
            ParBox.SelectedIndex = keepIndexes && _st.ParIndex >= 0 && _st.ParIndex < edges.Count ? _st.ParIndex : (edges.Count > 1 ? 1 : 0);
            if (string.IsNullOrWhiteSpace(Quadra.Text)) Quadra.Text = _st.Quadra ?? parent.Quadra;
            var region = parent.ToRegion();
            _childRows = ProjectContext.SortLots(_project.ActiveLots.Where(l => l.Id != parent.Id)).Where(l =>
            {
                try { var r = l.ToRegion(); return region.Bounds.Expand(0.01).Intersects(r.Bounds) && region.Contains(r.InteriorPoint()) && r.Area < region.Area; }
                catch { return false; }
            }).Select(l => new LotRow(l, SafeArea(l)) { Selected = _st.ChildLotIds.Count == 0 || _st.ChildLotIds.Contains(l.Id) }).ToList();
            LotsList.ItemsSource = _childRows;
            LinesInfo.Text = _st.LineCuts.Count == 0 ? "Nenhuma linha selecionada." : $"{_st.LineCuts.Select(c => c.Uid).Distinct().Count()} linha(s) selecionada(s).";
        }
        finally { _loading = false; }
        Recalc();
    }

    private void Any_Changed(object sender, SelectionChangedEventArgs e) { if (!_loading) Recalc(); }
    private void Any_Checked(object sender, RoutedEventArgs e) { if (!_loading && IsLoaded) Recalc(); }
    private void Any_Text(object sender, TextChangedEventArgs e) { if (!_loading && IsLoaded) Recalc(); }
    private void LotCheck_Click(object sender, RoutedEventArgs e) => Recalc();

    private void Rem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is PieceRow row && row.Remanescente)
            foreach (var r in Rows.Where(r => r != row)) r.Remanescente = false;
    }

    private void PickLines_Click(object sender, RoutedEventArgs e)
    {
        SaveState();
        Action = DesmAction.PickLines;
        DialogResult = true;
    }

    private static List<double>? ParseList(string text)
    {
        var res = new List<double>();
        foreach (var part in text.Split(new[] { ';', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Fmt.TryNum(part.Trim(), out double v) || v <= 0) return null;
            res.Add(v);
        }
        return res.Count == 0 ? null : res;
    }

    private bool TryAuto(LotRecord parent, out SplitRequest? req, out Vec2 along, out string error)
    {
        req = null; along = new Vec2(1, 0); error = "";
        if (FrontBox.SelectedItem is not EdgeChoice front) { error = "Escolha a frente."; return false; }
        var fdir = (front.Curve.End - front.Curve.Start).Normalized();
        along = FromStart.IsChecked == true ? fdir : -fdir;
        Vec2 cutDir;
        if (DirPerp.IsChecked == true) cutDir = fdir.PerpLeft;
        else if (DirPar.IsChecked == true)
        {
            if (ParBox.SelectedItem is not EdgeChoice pe) { error = "Escolha a divisa de referência."; return false; }
            cutDir = (pe.Curve.End - pe.Curve.Start).Normalized();
        }
        else
        {
            if (!AngleText.TryParseAngle(AzText.Text, out double az)) { error = "Azimute inválido."; return false; }
            cutDir = _ctx.Coords.DirectionFromAzimuth(az);
        }
        if (Math.Abs(cutDir.Cross(fdir)) < 0.05) { error = "As divisas não podem ser paralelas à frente."; return false; }
        SplitMode mode = CArea.IsChecked == true ? SplitMode.TargetArea : CAreas.IsChecked == true ? SplitMode.CustomAreas
            : CFront.IsChecked == true ? SplitMode.FixedFrontage : CFronts.IsChecked == true ? SplitMode.CustomFrontages : SplitMode.EqualParts;
        int parts = 2; double target = 0, width = 0, minRem = 0;
        IReadOnlyList<double> values = Array.Empty<double>();
        if (mode == SplitMode.EqualParts && (!int.TryParse(Parts.Text.Trim(), out parts) || parts < 2 || parts > 500)) { error = "Número de partes inválido (2 a 500)."; return false; }
        if (mode == SplitMode.TargetArea && (!Fmt.TryNum(Target.Text, out target) || target <= 0)) { error = "Área inválida."; return false; }
        if (mode == SplitMode.FixedFrontage && (!Fmt.TryNum(Width1.Text, out width) || width <= 0)) { error = "Testada inválida."; return false; }
        if (mode == SplitMode.CustomAreas && ParseList(Areas.Text) is not { } la) { error = "Áreas inválidas (ex.: 300; 250; 250)."; return false; }
        if (mode == SplitMode.CustomFrontages && ParseList(Fronts.Text) is not { } lf) { error = "Testadas inválidas (ex.: 12; 12; 6)."; return false; }
        if (mode == SplitMode.CustomAreas) values = ParseList(Areas.Text)!;
        if (mode == SplitMode.CustomFrontages) values = ParseList(Fronts.Text)!;
        if (!Fmt.TryNum(MinRem.Text, out minRem) || minRem < 0) minRem = 0;
        req = new SplitRequest
        {
            Region = parent.ToRegion(), CutDirection = cutDir, Frontage = front.Curve, FromFrontageStart = FromStart.IsChecked == true,
            Mode = mode, Parts = parts, TargetArea = target, FrontageWidth = width, MinRemainderArea = minRem, Values = values,
        };
        return true;
    }

    private void Recalc()
    {
        if (Summary is null) return;
        var parent = ParentLot;
        AutoPanel.Visibility = Modo == DesmModo.Automatico ? Visibility.Visible : Visibility.Collapsed;
        LinesPanel.Visibility = Modo == DesmModo.Linhas ? Visibility.Visible : Visibility.Collapsed;
        LotsPanel.Visibility = Modo == DesmModo.Lotes ? Visibility.Visible : Visibility.Collapsed;
        Rows = new();
        AutoResult = null;
        if (parent is null) return;
        var parentRegion = parent.ToRegion();
        var items = new List<PreviewItem> { PreviewItem.FromRegion(parentRegion, Colors.Transparent, null, false, Color.FromRgb(90, 90, 90), 2) };
        var pieces = new List<PlannedPiece>();
        var existing = new List<LotRecord?>();
        string msg = "";
        try
        {
            switch (Modo)
            {
                case DesmModo.Automatico:
                {
                    if (!TryAuto(parent, out var req, out var along, out var err)) { msg = err; break; }
                    var res = AreaSplitter.Split(req!);
                    if (res.Pieces.Count > 300) { msg = "Mais de 300 partes – revise os valores."; break; }
                    AutoResult = res;
                    if (Fmt.TryNum(SnapTol.Text, out double snap) && snap > 0)
                    {
                        int moved = SubdivisionPlanner.SnapCuts(parent, res.Cuts, Math.Min(snap, 5));
                        if (moved > 0) res.Warnings.Add($"{moved} ponta(s) de divisa encaixada(s) em vértice existente (áreas da tabela já ajustadas).");
                    }
                    pieces = SubdivisionPlanner.OrderAlong(SubdivisionPlanner.FromCuts(parent, res.Cuts.Select(c => ((Curve2)c, (string?)null)).ToList()), along);
                    foreach (var c in res.Cuts) items.Add(new PreviewItem { Line = new List<Vec2> { c.Start, c.End }, Stroke = Color.FromRgb(220, 40, 30), StrokeWidth = 2, Dashed = true });
                    if (res.Warnings.Count > 0) msg = string.Join(" ", res.Warnings);
                    break;
                }
                case DesmModo.Linhas:
                {
                    if (_st.LineCuts.Count == 0) { msg = "Clique em \"Selecionar linhas no Revit…\"."; break; }
                    pieces = SubdivisionPlanner.FromCuts(parent, _st.LineCuts.Select(c => (c.Curve, (string?)c.Uid)).ToList());
                    var fronts = parent.EdgeInfos.Any(i => i.Testada);
                    pieces = SubdivisionPlanner.OrderReading(pieces);
                    foreach (var (c, _) in _st.LineCuts) items.Add(new PreviewItem { Line = c.Densify(0.05), Stroke = Color.FromRgb(220, 40, 30), StrokeWidth = 2, Dashed = true });
                    if (pieces.Count < 2) msg = "As linhas selecionadas não dividem o lote (devem cruzar o lote de um lado a outro ou formar áreas fechadas com o contorno).";
                    _ = fronts;
                    break;
                }
                case DesmModo.Lotes:
                {
                    foreach (var r in _childRows.Where(r => r.Selected))
                    {
                        var reg = r.Lot.ToRegion();
                        pieces.Add(new PlannedPiece { Region = reg, Outer = r.Lot.Outer, Holes = r.Lot.Holes, Frontage = SubdivisionPlanner.FrontageOn(parent, reg) });
                        existing.Add(r.Lot);
                    }
                    if (_childRows.Count == 0) msg = "Não há lotes reconhecidos dentro do lote original. Desenhe as divisas e use \"Reconhecer Lotes\", ou escolha outro modo.";
                    break;
                }
            }
        }
        catch (Exception ex) { msg = "Não foi possível dividir: " + ex.Message; }

        // Linhas da tabela (edições preservadas por posição).
        var rules = _ctx.Rules;
        var numberer = new Commands.NumbererPublic(StartNumber.Text.Trim());
        string parentOwner = string.IsNullOrWhiteSpace(parent.Proprietario) ? _project.Info.Proprietario : parent.Proprietario;
        for (int i = 0; i < pieces.Count; i++)
        {
            var p = pieces[i];
            if (!_st.Edits.TryGetValue(i, out var ed))
            {
                ed = new PieceEdit { Proprietario = parentOwner };
                var ex = i < existing.Count ? existing[i] : null;
                ed.Nome = ex?.Nome ?? numberer.Peek(i);
                if (ex is not null) { ed.Remanescente = ex.IsRemanescente; if (!string.IsNullOrWhiteSpace(ex.Proprietario)) ed.Proprietario = ex.Proprietario; }
                _st.Edits[i] = ed;
            }
            var probs = new List<string>();
            if (rules is not null)
            {
                if (rules.AreaMinimaLote > 0 && p.Area + 1e-9 < rules.AreaMinimaLote) probs.Add($"área < {Fmt.N(rules.AreaMinimaLote, 0)} m²");
                if (rules.TestadaMinima > 0 && p.Frontage > 0 && p.Frontage + 1e-9 < rules.TestadaMinima) probs.Add($"testada < {Fmt.N(rules.TestadaMinima, 2)} m");
            }
            Rows.Add(new PieceRow(i + 1, p, ed, probs.Count == 0 ? "OK" : string.Join("; ", probs)) { ExistingLot = i < existing.Count ? existing[i] : null });
            var color = PreviewCanvas.Palette[i % PreviewCanvas.Palette.Length];
            items.Add(PreviewItem.FromRegion(p.Region, color, $"{(ed.Remanescente ? "REM. " : "")}{i + 1}\n{Fmt.N(p.Area, 2)} m²", true, color, 1.2));
        }
        Grid.ItemsSource = Rows;
        Preview.SetItems(items, _ctx.Coords.Rotation);

        // Lotes que podem receber uma parte por anexação (vizinhos ativos).
        var involved = new HashSet<string>(existing.Where(x => x is not null).Select(x => x!.Id)) { parent.Id };
        AnexarOptions = new List<IdText> { new("", "(não anexar)") };
        AnexarOptions.AddRange(ProjectContext.SortLots(_project.ActiveLots.Where(l => !involved.Contains(l.Id) && !LotUseInfo.IsDeduction(l.Uso) && l.Uso != LotUse.Gleba))
            .Select(l => new IdText(l.Id, l.DisplayName)));

        double sum = pieces.Sum(p => p.Area);
        double diff = sum - parentRegion.Area;
        Summary.Text = pieces.Count == 0 ? msg :
            $"{pieces.Count} parte(s). Área original {Fmt.N(parentRegion.Area, 2)} m² • soma das partes {Fmt.N(sum, 2)} m² • diferença {Fmt.N(diff, 2)} m²" +
            (Math.Abs(diff) <= ParcelOperations.AreaTolerance ? " (confere)." : " – há área fora das partes.") + (msg.Length > 0 ? "  ⚠ " + msg : "");
        bool ok = pieces.Count >= 2 && (Modo != DesmModo.Lotes || Math.Abs(diff) <= ParcelOperations.AreaTolerance || true);
        OkButton.IsEnabled = ok;
        Status.Text = "Marque o lote remanescente e informe de quem é cada parte. Para unir uma parte a um lote vizinho, escolha em \"Anexar a\".";
    }

    private void SaveState()
    {
        _st.ParentId = ParentLot?.Id;
        _st.Modo = Modo;
        _st.FrontIndex = FrontBox.SelectedIndex;
        _st.ParIndex = ParBox.SelectedIndex;
        _st.FromStart = FromStart.IsChecked == true;
        _st.Criterio = CArea.IsChecked == true ? 1 : CAreas.IsChecked == true ? 2 : CFront.IsChecked == true ? 3 : CFronts.IsChecked == true ? 4 : 0;
        _st.Parts = Parts.Text; _st.Target = Target.Text; _st.Areas = Areas.Text; _st.Width = Width1.Text; _st.Fronts = Fronts.Text; _st.MinRem = MinRem.Text; _st.Snap = SnapTol.Text;
        _st.Direcao = DirPar.IsChecked == true ? 1 : DirAz.IsChecked == true ? 2 : 0;
        _st.Az = AzText.Text;
        _st.ChildLotIds = _childRows.Where(r => r.Selected).Select(r => r.Lot.Id).ToHashSet();
        _st.Quadra = Quadra.Text.Trim();
        _st.StartNumber = StartNumber.Text.Trim();
        _st.Sheets = Sheets.IsChecked == true;
        _st.Notes = Notes.Text.Trim();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(DataGridEditingUnit.Row, true);
        Recalc();
        if (Rows.Count < 2) { Fmt.Msg("O desmembramento precisa de pelo menos duas partes."); return; }
        if (!Rows.Any(r => r.Remanescente))
        {
            if (MessageBox.Show("Nenhuma parte foi marcada como Lote Remanescente.\n\nContinuar mesmo assim?", "GeoPoint", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        }
        if (Rows.Any(r => string.IsNullOrWhiteSpace(r.Nome))) { Fmt.Msg("Informe o nome/número de todas as partes."); return; }
        if (Rows.Count(r => !string.IsNullOrWhiteSpace(r.AnexarA)) == Rows.Count) { Fmt.Msg("Pelo menos uma parte deve continuar como lote próprio (não anexada)."); return; }
        if (Modo == DesmModo.Lotes && ParentLot is { } parent)
        {
            var chk = ParcelOperations.CheckSubdivision(parent, Rows.Select(r => r.ExistingLot!).ToList());
            if (!chk.Ok) { Fmt.Msg(string.Join("\n", chk.Errors), icon: MessageBoxImage.Warning); return; }
        }
        SaveState();
        Action = DesmAction.Register;
        DialogResult = true;
    }
}
