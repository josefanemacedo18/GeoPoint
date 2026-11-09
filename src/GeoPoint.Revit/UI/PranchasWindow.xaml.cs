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

public partial class PranchasWindow : Window
{
    private readonly ParcelProject _project;
    private readonly List<LotRow> _rows;
    private bool _loading = true;

    public PranchasWindow(ParcelProject project, SheetSettings st, IEnumerable<LotRow> rows, OperationRecord? preselect)
    {
        InitializeComponent();
        _project = project;
        _rows = rows.ToList();
        LotsGrid.ItemsSource = _rows;
        var sources = new List<SourceChoice> { new("Lotes marcados (sem operação)", null) };
        foreach (var op in project.Operations.OrderByDescending(o => o.Date))
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
        Vias.IsChecked = st.Vias;
        Norte.IsChecked = st.NorteParaCima;
        Orientation.SelectedIndex = st.Orientation == 1 ? 1 : 0;
        var scales = new List<string> { "Automática (cabe na folha)" };
        scales.AddRange(SheetLayout.StandardScales.Select(x => "1:" + x));
        Scale.ItemsSource = scales;
        Scale.SelectedIndex = st.AutoScale ? 0 : Math.Max(0, Array.IndexOf(SheetLayout.StandardScales, st.Scale) + 1);
        Prefix.Text = st.NumberPrefix;
        Image.Text = project.Info.ImagemLocalizacao;
        _loading = false;
        Refresh();
    }

    public OperationRecord? Operation => (Source.SelectedItem as SourceChoice)?.Operation;
    public List<LotRecord> SelectedLots => _rows.Where(r => r.Selected).Select(r => r.Lot).ToList();
    public List<SheetSpec> Specs { get; private set; } = new();

    public SheetRequest Request => new()
    {
        Atual = Atual.IsChecked == true, Proposta = Proposta.IsChecked == true, IndividualAtual = IndAtual.IsChecked == true,
        IndividualProposta = IndProposta.IsChecked == true, Quadro = Quadro.IsChecked == true, Hachuras = Hachuras.IsChecked == true,
    };

    public void Apply(SheetSettings st, ProjectInfo info)
    {
        st.SituacaoAtual = Atual.IsChecked == true; st.Proposta = Proposta.IsChecked == true;
        st.IndividualAtual = IndAtual.IsChecked == true; st.IndividualProposta = IndProposta.IsChecked == true;
        st.Anotacoes = Anotacoes.IsChecked == true; st.Hachuras = Hachuras.IsChecked == true; st.Quadro = Quadro.IsChecked == true;
        st.Vias = Vias.IsChecked == true; st.NorteParaCima = Norte.IsChecked == true;
        st.Orientation = Orientation.SelectedIndex == 1 ? 1 : 0;
        st.AutoScale = Scale.SelectedIndex <= 0;
        if (!st.AutoScale) st.Scale = SheetLayout.StandardScales[Scale.SelectedIndex - 1];
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
        else Specs = SheetPlanner.ForLots(SelectedLots, Request, _project.Info.Situacao);
        Summary.Text = Specs.Count == 0
            ? "Nenhuma prancha: marque as situações e os lotes."
            : $"{Specs.Count} prancha(s):\n" + string.Join("\n", Specs.Select((x, i) => $"{i + 1:00}. {x.Title}" + (x.LotIds.Count > 1 ? $" ({x.LotIds.Count} lotes)" : "")));
        OkButton.IsEnabled = Specs.Count > 0;
    }

    private void Image_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Imagem de localização", Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|Todos|*.*" };
        if (dlg.ShowDialog(this) == true) Image.Text = dlg.FileName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Refresh();
        if (Specs.Count == 0) return;
        DialogResult = true;
    }
}
