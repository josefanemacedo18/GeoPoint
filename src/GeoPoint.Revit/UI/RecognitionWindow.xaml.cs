using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GeoPoint.Core.Model;

namespace GeoPoint.Revit.UI;

public enum RecognitionAction { Cancel, Continue, ShowIssues }

public partial class RecognitionWindow : Window
{
    public RecognitionWindow(string summary, IEnumerable<ReportRow> diagnostics, bool canCreate, string startNumber, string quadra, bool includeHoles, bool mergeCollinear)
    {
        InitializeComponent();
        Summary.Text = summary;
        var list = diagnostics.ToList();
        Grid.ItemsSource = list;
        ShowIssuesButton.IsEnabled = list.Any(r => r.Tags is { Count: > 0 });
        Uso.ItemsSource = UseOptions.All;
        Uso.SelectedValue = LotUse.Lote;
        StartNumber.Text = startNumber;
        Quadra.Text = quadra;
        IncludeHoles.IsChecked = includeHoles;
        MergeCollinear.IsChecked = mergeCollinear;
        ContinueButton.IsEnabled = canCreate;
    }

    public RecognitionAction Action { get; private set; } = RecognitionAction.Cancel;
    public bool ClickMode => ModeClick.IsChecked == true;
    public string StartNumberText => StartNumber.Text.Trim();
    public string QuadraText => Quadra.Text.Trim();
    public LotUse SelectedUse => Uso.SelectedValue is LotUse u ? u : LotUse.Lote;
    public bool IncludeHolesValue => IncludeHoles.IsChecked == true;
    public bool MergeCollinearValue => MergeCollinear.IsChecked == true;
    public List<int> SelectedTags { get; private set; } = new();

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        Action = RecognitionAction.Continue;
        DialogResult = true;
    }

    private void ShowIssues_Click(object sender, RoutedEventArgs e)
    {
        var rows = Grid.SelectedItems.Cast<ReportRow>().ToList();
        if (rows.Count == 0) rows = ((IEnumerable<ReportRow>)Grid.ItemsSource).Where(r => r.Tags is { Count: > 0 }).ToList();
        SelectedTags = rows.Where(r => r.Tags is not null).SelectMany(r => r.Tags!).Distinct().ToList();
        if (SelectedTags.Count == 0) { Fmt.Msg("A ocorrência selecionada não está associada a linhas."); return; }
        Action = RecognitionAction.ShowIssues;
        DialogResult = true;
    }
}
