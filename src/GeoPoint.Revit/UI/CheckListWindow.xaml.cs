using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace GeoPoint.Revit.UI;

public sealed class CheckItem : Notify
{
    private bool _checked;
    public CheckItem(string text, bool isChecked) { Text = text; _checked = isChecked; }
    public string Text { get; }
    public bool Checked { get => _checked; set => Set(ref _checked, value); }
}

public partial class CheckListWindow : Window
{
    private readonly List<CheckItem> _items;

    public CheckListWindow(string header, string intro, IEnumerable<string> items, bool allChecked = true)
    {
        InitializeComponent();
        Header.Text = header;
        Intro.Text = intro;
        _items = items.Select(i => new CheckItem(i, allChecked)).ToList();
        List.ItemsSource = _items;
    }

    public List<string> Checked => _items.Where(i => i.Checked).Select(i => i.Text).ToList();

    private void All_Click(object sender, RoutedEventArgs e) { foreach (var i in _items) i.Checked = true; }
    private void None_Click(object sender, RoutedEventArgs e) { foreach (var i in _items) i.Checked = false; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Checked.Count == 0) { Fmt.Msg("Marque pelo menos um item."); return; }
        DialogResult = true;
    }
}
