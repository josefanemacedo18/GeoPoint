using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GeoPoint.Core.Model;

namespace GeoPoint.Revit.UI;

public enum FaseAction { None, Create, Activate, CreateView, Refresh, Delete, Save }

public sealed class FaseRow
{
    public FaseRow(FaseRecord f, ParcelProject p, Func<string, string> viewName)
    {
        Fase = f;
        Nome = f.Nome;
        Situacao = f.Situacao;
        Ativa = f.Id == p.Fase.Id ? "●" : "";
        Numero = f.Numero;
        Base = p.FindFase(f.BaseFaseId)?.Label ?? "–";
        Lotes = p.Lots.Count(l => l.FaseId == f.Id && l.Status == LotStatus.Ativo);
        Operacoes = p.Operations.Count(o => o.FaseId == f.Id);
        Vistas = f.ViewIds.Count == 0 ? "(nenhuma)" : string.Join(", ", f.ViewIds.Select(viewName).Where(n => n.Length > 0));
    }
    public FaseRecord Fase { get; }
    public string Ativa { get; }
    public int Numero { get; }
    public string Nome { get; set; }
    public string Situacao { get; set; }
    public string Base { get; }
    public int Lotes { get; }
    public int Operacoes { get; }
    public string Vistas { get; }
}

public partial class FasesWindow : Window
{
    private readonly List<FaseRow> _rows;

    public FasesWindow(ParcelProject p, Func<string, string> viewName)
    {
        InitializeComponent();
        _rows = p.Fases.OrderBy(f => f.Numero).Select(f => new FaseRow(f, p, viewName)).ToList();
        Grid.ItemsSource = _rows;
        Grid.SelectedItem = _rows.FirstOrDefault(r => r.Fase.Id == p.Fase.Id);
        NewSituacao.ItemsSource = ProjectInfo.Situacoes;
        NewSituacao.Text = "Desmembramento";
    }

    public FaseAction Action { get; private set; }
    public FaseRecord? Selected => (Grid.SelectedItem as FaseRow)?.Fase;
    public string NewNameText => NewName.Text.Trim();
    public string NewSituacaoText => NewSituacao.Text.Trim();
    public bool CreateViewValue => NewView.IsChecked == true;

    private void Commit()
    {
        Grid.CommitEdit(); Grid.CommitEdit();
        foreach (var r in _rows)
        {
            if (!string.IsNullOrWhiteSpace(r.Nome)) r.Fase.Nome = r.Nome.Trim();
            r.Fase.Situacao = (r.Situacao ?? "").Trim();
        }
    }

    private void Done(FaseAction a)
    {
        Commit();
        Action = a;
        DialogResult = true;
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) { Fmt.Msg("Selecione a fase de partida na tabela."); return; }
        if (string.IsNullOrWhiteSpace(NewNameText)) { Fmt.Msg("Informe o nome da nova fase."); return; }
        Done(FaseAction.Create);
    }

    private void Activate_Click(object sender, RoutedEventArgs e) { if (Selected is not null) Done(FaseAction.Activate); }
    private void CreateView_Click(object sender, RoutedEventArgs e) { if (Selected is not null) Done(FaseAction.CreateView); }
    private void Refresh_Click(object sender, RoutedEventArgs e) => Done(FaseAction.Refresh);
    private void Save_Click(object sender, RoutedEventArgs e) => Done(FaseAction.Save);

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        if (Selected.Numero == 0) { Fmt.Msg("A fase inicial não pode ser excluída."); return; }
        if (MessageBox.Show($"Excluir a {Selected.Label}?\n\nOs lotes, operações, anotações e pranchas desta fase são removidos do GeoPoint. As linhas do desenho não são apagadas.",
                "GeoPoint", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Done(FaseAction.Delete);
    }
}
