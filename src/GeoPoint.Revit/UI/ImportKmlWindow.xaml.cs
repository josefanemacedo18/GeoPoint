using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GeoPoint.Core.Export;
using GeoPoint.Core.Geodesy;

namespace GeoPoint.Revit.UI;

public sealed class KmlRow : Notify
{
    private bool _selected = true;
    public KmlRow(KmlPoint p) { Point = p; Nome = p.Name; }
    public KmlPoint Point { get; }
    public bool Selected { get => _selected; set => Set(ref _selected, value); }
    public string Nome { get; set; }
    public string LatText => AngleText.FormatDms(Point.Lat, 3, AngleKind.Latitude);
    public string LonText => AngleText.FormatDms(Point.Lon, 3, AngleKind.Longitude);
    public string AltText => Fmt.N(Point.Alt, 1);
}

public partial class ImportKmlWindow : Window
{
    private readonly List<KmlRow> _rows;
    private readonly bool _hasRef;

    public ImportKmlWindow(string file, List<KmlPoint> points, bool hasReference)
    {
        InitializeComponent();
        _hasRef = hasReference;
        _rows = points.Select(p => new KmlRow(p)).ToList();
        Grid.ItemsSource = _rows;
        RefPoint.ItemsSource = _rows;
        RefPoint.SelectedIndex = 0;
        FileInfo.Text = $"{file} – {points.Count} ponto(s). Os pontos ficam sempre visíveis no desenho (cruz laranja com o nome); o cruzamento da cruz serve de snap para desenhar as divisas.";
        ModeRef.IsEnabled = hasReference;
        if (hasReference) ModeRef.IsChecked = true; else ModeClick.IsChecked = true;
    }

    public List<KmlRow> Selected => _rows.Where(r => r.Selected).ToList();
    public bool ClickMode => ModeClick.IsChecked == true;
    public KmlRow? ClickPoint => RefPoint.SelectedItem as KmlRow;

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (ModeHint is null) return;
        RefPoint.IsEnabled = ModeClick.IsChecked == true;
        ModeHint.Text = ModeClick.IsChecked == true && _hasRef
            ? "Atenção: o ponto de referência do projeto será redefinido para este ponto; as coordenadas dos lotes passam a partir dele."
            : "";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(); Grid.CommitEdit();
        if (Selected.Count == 0) { Fmt.Msg("Marque pelo menos um ponto."); return; }
        if (ClickMode && ClickPoint is null) { Fmt.Msg("Escolha o ponto que você vai clicar no desenho."); return; }
        DialogResult = true;
    }
}
