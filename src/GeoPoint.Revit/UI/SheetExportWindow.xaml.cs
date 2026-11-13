using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace GeoPoint.Revit.UI;

public sealed class SheetPickRow : Notify
{
    private bool _selected;
    public bool Selected { get => _selected; set => Set(ref _selected, value); }
    public string Number { get; init; } = "";
    public string Name { get; init; } = "";
    public string PaperText { get; init; } = "";
    public bool IsGeoPoint { get; init; }
    public object? Tag { get; init; }
}

public partial class SheetExportWindow : Window
{
    private readonly List<SheetPickRow> _all;

    public SheetExportWindow(IEnumerable<SheetPickRow> rows, string folder, string fileName)
    {
        InitializeComponent();
        _all = rows.ToList();
        Folder.Text = folder;
        FileName.Text = fileName;
        Refresh();
    }

    public List<SheetPickRow> Selected => _all.Where(r => r.Selected).ToList();
    private bool _picker;

    /// <summary>Usa a janela como lista de escolha (cópias de segurança): esconde as opções de arquivo.</summary>
    public void ConfigureAsPicker(string header, string hint, string okText)
    {
        _picker = true;
        HeaderText.Text = header;
        HintText.Text = hint;
        FileBox.Visibility = Visibility.Collapsed;
        OnlyGp.Visibility = Visibility.Collapsed;
        OkButton.Content = okText;
        ColNumber.Header = "Data";
        ColName.Header = "Cópia";
        ColPaper.Header = "Lotes";
    }
    public string OutFolder => Folder.Text.Trim();
    public string OutName => string.IsNullOrWhiteSpace(FileName.Text) ? "Pranchas" : FileName.Text.Trim();
    public bool CombineAll => Combine.IsChecked == true;
    public bool OpenFolder => Open.IsChecked == true;

    private void Refresh() => Grid.ItemsSource = OnlyGp.IsChecked == true ? _all.Where(r => r.IsGeoPoint).ToList() : _all;
    private void Filter_Click(object sender, RoutedEventArgs e) => Refresh();
    private void All_Click(object sender, RoutedEventArgs e) { foreach (var r in (IEnumerable<SheetPickRow>)Grid.ItemsSource) r.Selected = true; }
    private void None_Click(object sender, RoutedEventArgs e) { foreach (var r in _all) r.Selected = false; }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Pasta dos PDFs", InitialDirectory = Directory.Exists(Folder.Text) ? Folder.Text : null };
        if (dlg.ShowDialog(this) == true) Folder.Text = dlg.FolderName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(); Grid.CommitEdit();
        if (Selected.Count == 0) { Fmt.Msg(_picker ? "Marque uma cópia." : "Marque pelo menos uma prancha."); return; }
        if (!_picker && string.IsNullOrWhiteSpace(OutFolder)) { Fmt.Msg("Escolha a pasta."); return; }
        DialogResult = true;
    }
}
