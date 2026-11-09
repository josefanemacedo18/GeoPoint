using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;

namespace GeoPoint.Revit.UI;

public enum FaseAction { None, Create, Activate, CreateView, Refresh, Delete, Save }

public sealed class FaseRow
{
    public FaseRow(FaseRecord f, ParcelProject p, Func<string, string> viewName)
    {
        Fase = f;
        Numero = f.Numero.ToString();
        Nome = f.Nome;
        Situacao = f.Situacao;
        Notes = f.Notes;
        Ativa = f.Id == p.Fase.Id ? "●" : "";
        Base = p.FindFase(f.BaseFaseId)?.Label ?? "–";
        Lotes = p.Lots.Count(l => l.FaseId == f.Id && l.Status == LotStatus.Ativo);
        Operacoes = p.Operations.Count(o => o.FaseId == f.Id);
        Vistas = f.ViewIds.Count == 0 ? "(nenhuma)" : string.Join(", ", f.ViewIds.Select(viewName).Where(n => n.Length > 0));
    }
    public FaseRecord Fase { get; }
    public string Ativa { get; }
    public string Numero { get; set; }
    public string Nome { get; set; }
    public string Situacao { get; set; }
    public string Notes { get; set; }
    public string Base { get; }
    public int Lotes { get; }
    public int Operacoes { get; }
    public string Vistas { get; }
}

public partial class FasesWindow : Window
{
    private readonly ParcelProject _project;
    private readonly List<FaseRow> _rows;
    private List<LotRow> _lots = new();
    private bool _loading = true;

    public FasesWindow(ParcelProject p, Func<string, string> viewName)
    {
        InitializeComponent();
        _project = p;
        _rows = p.Fases.OrderBy(f => f.Numero).Select(f => new FaseRow(f, p, viewName)).ToList();
        Grid.ItemsSource = _rows;
        Grid.SelectedItem = _rows.FirstOrDefault(r => r.Fase.Id == p.Fase.Id);
        NewSituacao.ItemsSource = ProjectInfo.Situacoes;
        NewSituacao.Text = "Desmembramento";
        var bases = p.Fases.OrderBy(f => f.Numero).Select(f => new IdText(f.Id, f.Label)).ToList();
        bases.Add(new IdText("", "(nenhuma – fase vazia, sem lotes)"));
        BaseBox.ItemsSource = bases;
        _loading = false;
        BaseBox.SelectedItem = bases.FirstOrDefault(b => b.Id == p.Fase.Id) ?? bases[0];
    }

    public FaseAction Action { get; private set; }
    public FaseRecord? Selected => (Grid.SelectedItem as FaseRow)?.Fase;
    public string NewNameText => NewName.Text.Trim();
    public string NewSituacaoText => NewSituacao.Text.Trim();
    public bool CreateViewValue => NewView.IsChecked == true;
    /// <summary>Fase de partida ("" = fase vazia).</summary>
    public string BaseId => (BaseBox.SelectedItem as IdText)?.Id ?? "";
    public List<string> LotIds => _lots.Where(l => l.Selected).Select(l => l.Lot.Id).ToList();

    private void Base_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var id = BaseId;
        _lots = id.Length == 0 ? new List<LotRow>() :
            GeoPoint.Core.Calc.ProjectContext.SortLots(_project.Lots.Where(l => l.FaseId == id && l.Status == LotStatus.Ativo))
                .Select(l => new LotRow(l, SafeArea(l)) { Selected = true }).ToList();
        LotsGrid.ItemsSource = _lots;
        LotsGrid.IsEnabled = id.Length > 0;
        LotsTitle.Text = id.Length == 0 ? "Fase vazia: desenhe, gere ou reconheça os lotes depois" : "Lotes que entram na nova fase (cópias; a fase de partida não muda)";
    }

    private static double SafeArea(LotRecord l) { try { return l.ToRegion().Area; } catch { return 0; } }

    private void All_Click(object sender, RoutedEventArgs e) { foreach (var l in _lots) l.Selected = true; }
    private void None_Click(object sender, RoutedEventArgs e) { foreach (var l in _lots) l.Selected = false; }

    /// <summary>Grava as edições da tabela. Devolve false se houver número inválido ou repetido.</summary>
    private bool Commit()
    {
        Grid.CommitEdit(); Grid.CommitEdit();
        var nums = new Dictionary<FaseRow, int>();
        foreach (var r in _rows)
        {
            if (!int.TryParse((r.Numero ?? "").Trim(), out int n) || n < 0) { Fmt.Msg($"Número de fase inválido: \"{r.Numero}\"."); return false; }
            nums[r] = n;
        }
        if (nums.Values.Distinct().Count() != nums.Count) { Fmt.Msg("Duas fases com o mesmo número."); return false; }
        foreach (var r in _rows)
        {
            r.Fase.Numero = nums[r];
            if (!string.IsNullOrWhiteSpace(r.Nome)) r.Fase.Nome = r.Nome.Trim();
            r.Fase.Situacao = (r.Situacao ?? "").Trim();
            r.Fase.Notes = (r.Notes ?? "").Trim();
        }
        return true;
    }

    private void Done(FaseAction a)
    {
        if (!Commit()) return;
        Action = a;
        DialogResult = true;
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewNameText)) { Fmt.Msg("Informe o nome da nova fase."); return; }
        LotsGrid.CommitEdit(); LotsGrid.CommitEdit();
        if (BaseId.Length > 0 && LotIds.Count == 0 &&
            MessageBox.Show("Nenhum lote marcado: a fase será criada vazia. Continuar?", "GeoPoint", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Done(FaseAction.Create);
    }

    private void Activate_Click(object sender, RoutedEventArgs e) { if (Selected is not null) Done(FaseAction.Activate); }
    private void CreateView_Click(object sender, RoutedEventArgs e) { if (Selected is not null) Done(FaseAction.CreateView); }
    private void Refresh_Click(object sender, RoutedEventArgs e) => Done(FaseAction.Refresh);
    private void Save_Click(object sender, RoutedEventArgs e) => Done(FaseAction.Save);

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        if (_project.Fases.Count <= 1) { Fmt.Msg("O projeto precisa ter pelo menos uma fase. Para mudar o que ela é, edite o nome e o tipo na tabela."); return; }
        var deps = _project.Fases.Where(f => f.BaseFaseId == Selected.Id).ToList();
        if (deps.Count > 0) { Fmt.Msg($"Estas fases partem da {Selected.Label}: {string.Join(", ", deps.Select(d => d.Label))}. Exclua-as primeiro."); return; }
        if (MessageBox.Show($"Excluir a {Selected.Label}?\n\nOs lotes, operações, anotações e pranchas desta fase são removidos do GeoPoint. As linhas do desenho não são apagadas.",
                "GeoPoint", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Done(FaseAction.Delete);
    }
}
