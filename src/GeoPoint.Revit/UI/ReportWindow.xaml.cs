using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;

namespace GeoPoint.Revit.UI;

public enum ReportAction { None, Select, Accept }

/// <summary>Janela genérica de relatório (verificações, diagnósticos, alterações).</summary>
public partial class ReportWindow : Window
{
    public ReportWindow(string header, string summary, IEnumerable<ReportRow> rows, string? acceptText = null, bool allowSelect = true)
    {
        InitializeComponent();
        Header.Text = header;
        Summary.Text = summary;
        Summary.Visibility = string.IsNullOrWhiteSpace(summary) ? Visibility.Collapsed : Visibility.Visible;
        Grid.ItemsSource = rows.ToList();
        if (acceptText is not null)
        {
            AcceptButton.Content = acceptText;
            AcceptButton.Visibility = Visibility.Visible;
            CloseButton.Content = "Cancelar";
        }
        SelectButton.Visibility = allowSelect ? Visibility.Visible : Visibility.Collapsed;
    }

    public ReportAction Action { get; private set; }
    public List<ReportRow> SelectedRows { get; private set; } = new();

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        SelectedRows = Grid.SelectedItems.Cast<ReportRow>().ToList();
        if (SelectedRows.Count == 0)
        {
            Fmt.Msg("Selecione uma ou mais linhas da lista.");
            return;
        }
        Action = ReportAction.Select;
        DialogResult = true;
    }

    private void Grid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SelectButton.Visibility == Visibility.Visible && Grid.SelectedItem is ReportRow) Select_Click(sender, e);
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        Action = ReportAction.Accept;
        DialogResult = true;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder("Nível\tCategoria\tLote\tMensagem\n");
        foreach (ReportRow r in Grid.ItemsSource)
            sb.Append(r.Nivel).Append('\t').Append(r.Categoria).Append('\t').Append(r.Lote).Append('\t').Append(r.Mensagem.Replace('\n', ' ')).Append('\n');
        try { Clipboard.SetText(sb.ToString()); } catch { }
    }
}
