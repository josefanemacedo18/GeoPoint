using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GeoPoint.Core.Model;

namespace GeoPoint.Revit.UI;

/// <summary>Janela genérica: escolha de lotes e opções.</summary>
public partial class LotPickerWindow : Window
{
    private readonly List<LotRow> _all;
    private readonly List<OptionItem> _options;
    private readonly Dictionary<string, Control> _optionControls = new();
    private readonly bool _single;

    public LotPickerWindow(string header, string intro, IEnumerable<LotRow> rows, IEnumerable<OptionItem>? options = null,
        string okText = "OK", bool showArchivedByDefault = false, bool single = false)
    {
        InitializeComponent();
        Header.Text = header;
        Intro.Text = intro;
        Intro.Visibility = string.IsNullOrWhiteSpace(intro) ? Visibility.Collapsed : Visibility.Visible;
        OkButton.Content = okText;
        _all = rows.ToList();
        _options = options?.ToList() ?? new List<OptionItem>();
        _single = single;
        ShowArchived.IsChecked = showArchivedByDefault;
        BuildOptions();
        Refresh();
    }

    public List<LotRecord> SelectedLots { get; private set; } = new();

    public bool Option(string key) => _options.FirstOrDefault(o => o.Key == key)?.Bool ?? false;
    public double Number(string key) => _options.FirstOrDefault(o => o.Key == key)?.Number ?? 0;

    private void BuildOptions()
    {
        OptionsBox.Visibility = _options.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var o in _options)
        {
            if (o.IsNumber)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var lbl = new Label { Content = o.Label, Width = 260, ToolTip = o.Tooltip };
                var tb = new TextBox { Text = Fmt.Raw(o.Number), Width = 90, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = o.Tooltip };
                row.Children.Add(lbl);
                row.Children.Add(tb);
                OptionsPanel.Children.Add(row);
                _optionControls[o.Key] = tb;
            }
            else
            {
                var cb = new CheckBox { Content = o.Label, IsChecked = o.Bool, ToolTip = string.IsNullOrWhiteSpace(o.Tooltip) ? null : o.Tooltip };
                OptionsPanel.Children.Add(cb);
                _optionControls[o.Key] = cb;
            }
        }
    }

    private void Refresh()
    {
        bool arch = ShowArchived.IsChecked == true;
        var visible = _all.Where(r => arch || r.Lot.Status == LotStatus.Ativo).ToList();
        Grid.ItemsSource = visible;
        UpdateCount();
    }

    private void UpdateCount() => Count.Text = $"{_all.Count(r => r.Selected)} selecionado(s)";

    private void All_Click(object sender, RoutedEventArgs e)
    {
        if (_single) return;
        foreach (var r in (IEnumerable<LotRow>)Grid.ItemsSource) r.Selected = true;
        UpdateCount();
    }

    private void None_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _all) r.Selected = false;
        UpdateCount();
    }

    private void Check_Click(object sender, RoutedEventArgs e)
    {
        if (_single && sender is CheckBox cb && cb.DataContext is LotRow row && row.Selected)
            foreach (var r in _all.Where(r => r != row)) r.Selected = false;
        UpdateCount();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) Refresh();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        // Linhas destacadas na grade também contam como selecionadas.
        if (!_all.Any(r => r.Selected))
            foreach (LotRow r in Grid.SelectedItems) r.Selected = true;
        SelectedLots = _all.Where(r => r.Selected).Select(r => r.Lot).ToList();
        if (SelectedLots.Count == 0)
        {
            Fmt.Msg("Selecione pelo menos um lote.");
            return;
        }
        if (_single && SelectedLots.Count > 1) SelectedLots = SelectedLots.Take(1).ToList();
        foreach (var o in _options)
        {
            var c = _optionControls[o.Key];
            if (o.IsNumber)
            {
                if (!Fmt.ReadNum((TextBox)c, o.Label, out double v)) return;
                o.Number = v;
            }
            else o.Bool = ((CheckBox)c).IsChecked == true;
        }
        DialogResult = true;
    }
}
