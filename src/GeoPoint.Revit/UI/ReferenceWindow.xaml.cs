using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GeoPoint.Core.Calc;
using GeoPoint.Core.Export;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Model;
using GeoPoint.Revit.Infrastructure;

namespace GeoPoint.Revit.UI;

public partial class ReferenceWindow : Window
{
    private sealed record DatumChoice(DatumId Id, string Text) { public override string ToString() => Text; }
    private sealed record PrecisionChoice(PrecisionClass Id, string Text) { public override string ToString() => Text; }
    private sealed record KmlChoice(KmlPoint P, string Text);

    private readonly bool _orientation;

    public ReferenceWindow(string header, string pointInfo, bool showOrientation, string projectNorthInfo, GeoReferenceRecord? existing, string viewNorthInfo = "")
    {
        InitializeComponent();
        Header.Text = header;
        PointInfo.Text = pointInfo;
        _orientation = showOrientation;
        OrientationBox.Visibility = showOrientation ? Visibility.Visible : Visibility.Collapsed;
        ProjectNorthInfo.Text = projectNorthInfo;
        ViewNorthInfo.Text = viewNorthInfo;
        ViewNorthInfo.Visibility = string.IsNullOrWhiteSpace(viewNorthInfo) ? Visibility.Collapsed : Visibility.Visible;
        DatumBox.ItemsSource = new List<DatumChoice>
        {
            new(DatumId.Wgs84, "WGS 84 – GPS, Google Maps/Earth (≈ SIRGAS 2000)"),
            new(DatumId.Sirgas2000, "SIRGAS 2000 – oficial (QGIS, levantamentos)"),
            new(DatumId.Sad69, "SAD 69 – documentos antigos"),
            new(DatumId.CorregoAlegre, "Córrego Alegre – documentos antigos"),
        };
        DatumBox.SelectedIndex = 0;
        PrecisionBox.ItemsSource = Enum.GetValues<PrecisionClass>().Select(p => new PrecisionChoice(p, PrecisionInfo.Label(p))).ToList();
        PrecisionBox.SelectedIndex = 0;
        if (existing is { IsSet: true } && showOrientation)
        {
            LatLonText.Text = AngleText.FormatNumber(existing.Lat, 8, false).Replace(',', '.') + ", " + AngleText.FormatNumber(existing.Lon, 8, false).Replace(',', '.');
            DatumBox.SelectedIndex = 1;
            Altitude.Text = Fmt.Raw(existing.Height, 2);
            PrecisionBox.SelectedIndex = (int)existing.Precision;
            OriProject.IsChecked = existing.Mode == OrientationMode.ProjectTrueNorth;
            OriEdge.IsChecked = existing.Mode == OrientationMode.EdgeAzimuth;
            OriTwo.IsChecked = existing.Mode == OrientationMode.TwoPoints;
            OriView.IsChecked = existing.Mode == OrientationMode.ViewUp;
            if (existing.EdgeAzimuthDeg is double az) EdgeAz.Text = AngleText.FormatDms(az, 2, AngleKind.Azimuth);
        }
        UpdatePreview();
    }

    public GeodeticCoord Sirgas { get; private set; }
    public string InputText { get; private set; } = "";
    public CrsKind InputKind { get; private set; }
    public DatumId InputDatum { get; private set; }
    public double AltitudeValue { get; private set; }
    public PrecisionClass Precision { get; private set; }
    public OrientationMode Mode { get; private set; }
    public double? EdgeAzimuth { get; private set; }

    private DatumId SelectedDatum => (DatumBox.SelectedItem as DatumChoice)?.Id ?? DatumId.Wgs84;

    private void Kind_Changed(object sender, RoutedEventArgs e)
    {
        if (PanelLatLon is null) return;
        PanelLatLon.Visibility = KindLatLon.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelUtm.Visibility = KindUtm.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelKml.Visibility = KindKml.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (KindKml.IsChecked == true) DatumBox.SelectedIndex = 0;
        UpdatePreview();
    }

    private void Input_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();
    private void Input_Changed2(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private void OpenKml_Click(object sender, RoutedEventArgs e)
    {
        var path = Ui.OpenFile("Abrir marcadores do Google Earth / QGIS", "Google Earth (*.kmz;*.kml)|*.kmz;*.kml|Todos (*.*)|*.*");
        if (path is null) return;
        try
        {
            var pts = KmlReader.ReadPoints(path);
            KmlFile.Text = System.IO.Path.GetFileName(path) + $" – {pts.Count} ponto(s)";
            KmlList.ItemsSource = pts.Select(p => new KmlChoice(p, $"{p.Name}   ({AngleText.FormatNumber(p.Lat, 7, false)}; {AngleText.FormatNumber(p.Lon, 7, false)})")).ToList();
            if (pts.Count > 0) KmlList.SelectedIndex = 0;
            else Fmt.Msg("O arquivo não contém marcadores de ponto (Placemark/Point).");
        }
        catch (Exception ex)
        {
            Fmt.Msg("Não foi possível ler o arquivo: " + ex.Message, icon: MessageBoxImage.Warning);
        }
    }

    private bool TryInput(out GeodeticCoord sirgas, out string error)
    {
        sirgas = default;
        error = "";
        if (!Fmt.TryNum(Altitude.Text, out double h)) h = 0;
        try
        {
            if (KindLatLon.IsChecked == true)
            {
                if (!AngleText.TryParseLatLon(LatLonText.Text, out double lat, out double lon))
                {
                    error = string.IsNullOrWhiteSpace(LatLonText.Text) ? "Cole ou digite a latitude e a longitude." : "Não reconheci o par latitude/longitude.";
                    return false;
                }
                sirgas = CoordinateService.CrsToGeodetic(new CrsCoord(lat, lon, true), CrsDef.Geographic(SelectedDatum), h);
                InputKind = CrsKind.Geographic;
                InputText = LatLonText.Text.Trim();
                return true;
            }
            if (KindUtm.IsChecked == true)
            {
                if (!int.TryParse(UtmZone.Text.Trim(), out int zone) || zone < 1 || zone > 60) { error = "Fuso UTM inválido (1 a 60; Brasil: 18 a 25)."; return false; }
                if (!Fmt.TryNum(UtmE.Text, out double e) || !Fmt.TryNum(UtmN.Text, out double n)) { error = "Informe E e N em metros."; return false; }
                if (e < 100000 || e > 900000) { error = "E fora da faixa UTM (100.000 a 900.000 m)."; return false; }
                if (n < 0 || n > 10000000) { error = "N fora da faixa UTM."; return false; }
                var def = new CrsDef { Kind = CrsKind.Utm, Datum = SelectedDatum, UtmZone = zone, UtmSouth = UtmHemi.SelectedIndex == 0 };
                sirgas = CoordinateService.CrsToGeodetic(new CrsCoord(e, n, false), def, h);
                InputKind = CrsKind.Utm;
                InputText = $"UTM {zone}{(UtmHemi.SelectedIndex == 0 ? "S" : "N")} E {UtmE.Text.Trim()} N {UtmN.Text.Trim()}";
                return true;
            }
            if (KmlList.SelectedItem is KmlChoice kc)
            {
                sirgas = CoordinateService.CrsToGeodetic(new CrsCoord(kc.P.Lat, kc.P.Lon, true), CrsDef.Geographic(DatumId.Wgs84), h);
                InputKind = CrsKind.Geographic;
                InputText = $"KML: {kc.P.Name}";
                return true;
            }
            error = "Abra um arquivo KML/KMZ e escolha o marcador.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void UpdatePreview()
    {
        if (Preview is null) return;
        if (!TryInput(out var g, out var err))
        {
            Preview.Text = err;
            return;
        }
        var zone = Core.Geodesy.UtmZone.FromLonLat(g.LonDeg, g.LatDeg);
        var tm = zone.Projection(Ellipsoid.GRS80);
        var p = tm.Forward(g.LatDeg, g.LonDeg);
        Preview.Text =
            $"SIRGAS 2000:  {AngleText.FormatDms(g.LatDeg, 4, AngleKind.Latitude)}   {AngleText.FormatDms(g.LonDeg, 4, AngleKind.Longitude)}\n" +
            $"Decimal:      {AngleText.FormatNumber(g.LatDeg, 8, false)}   {AngleText.FormatNumber(g.LonDeg, 8, false)}\n" +
            $"UTM {zone.Number}{Core.Geodesy.UtmZone.LatitudeBand(g.LatDeg)}:      E {AngleText.FormatNumber(p.E, 3)}   N {AngleText.FormatNumber(p.N, 3)}\n" +
            $"Convergência meridiana: {AngleText.FormatDms(p.ConvergenceDeg, 2)}   Fator de escala: {p.Scale:0.0000000}";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!TryInput(out var g, out var err))
        {
            Fmt.Msg(err, icon: MessageBoxImage.Warning);
            return;
        }
        if (!Fmt.TryNum(Altitude.Text, out double h) || h < -500 || h > 9000)
        {
            Fmt.Msg("Altitude inválida.", icon: MessageBoxImage.Warning);
            return;
        }
        Sirgas = g;
        AltitudeValue = h;
        InputDatum = KindKml.IsChecked == true ? DatumId.Wgs84 : SelectedDatum;
        Precision = (PrecisionBox.SelectedItem as PrecisionChoice)?.Id ?? PrecisionClass.Desconhecida;
        Mode = !_orientation || OriProject.IsChecked == true ? OrientationMode.ProjectTrueNorth
            : OriView.IsChecked == true ? OrientationMode.ViewUp
            : OriEdge.IsChecked == true ? OrientationMode.EdgeAzimuth : OrientationMode.TwoPoints;
        if (Mode == OrientationMode.EdgeAzimuth)
        {
            if (!AngleText.TryParseAngle(EdgeAz.Text, out double az) || az < 0 || az >= 360)
            {
                Fmt.Msg("Azimute inválido. Use, por exemplo, 299°36'55\" ou 299,6153.", icon: MessageBoxImage.Warning);
                EdgeAz.Focus();
                return;
            }
            EdgeAzimuth = az;
        }
        DialogResult = true;
    }
}
