using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Operations;

namespace GeoPoint.Revit.UI;

public enum GenDirection { Screen, Pick, Azimuth }

public partial class LotGeneratorWindow : Window
{
    private readonly Dictionary<string, Control> _inputs = new();
    private bool _loading = true;

    public LotGeneratorWindow(string suggestedName, string quadra)
    {
        InitializeComponent();
        Preview.ShowNorth = false;
        Shapes.ItemsSource = LotShapes.All;
        Uso.ItemsSource = UseOptions.All;
        Uso.SelectedValue = LotUse.Lote;
        NameText.Text = suggestedName;
        Quadra.Text = quadra;
        _loading = false;
        Shapes.SelectedIndex = 1; // retângulo
    }

    public LotShapeDef? Shape => Shapes.SelectedItem as LotShapeDef;
    public LotShapeResult? Result { get; private set; }
    public int CountValue { get; private set; } = 1;
    public GenDirection Direction => DirPick.IsChecked == true ? GenDirection.Pick : DirAz.IsChecked == true ? GenDirection.Azimuth : GenDirection.Screen;
    public double AzimuthDeg { get; private set; }
    public bool Detail => DetailLines.IsChecked == true;
    public bool RegisterLots => Register.IsChecked == true;
    public string StartName => NameText.Text.Trim();
    public string QuadraText => Quadra.Text.Trim();
    public LotUse UseValue => Uso.SelectedValue is LotUse u ? u : LotUse.Lote;
    public bool MarkTestada => Testada.IsChecked == true;
    public bool AnnotateNow => Annotate.IsChecked == true;

    private void Shape_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || Shape is null) return;
        ParamsPanel.Children.Clear();
        _inputs.Clear();
        ShapeHint.Text = Shape.Descricao;
        foreach (var p in Shape.Params)
        {
            ParamsPanel.Children.Add(new Label { Content = p.Label, Padding = new Thickness(0, 6, 0, 2) });
            Control c;
            if (p.Choices is not null)
            {
                var cb = new ComboBox { ItemsSource = p.Choices, SelectedIndex = 0 };
                cb.SelectionChanged += (_, _) => Refresh();
                c = cb;
            }
            else
            {
                var tb = new TextBox { Text = p.IsText ? p.DefaultText : Fmt.Raw(p.Default, 3) };
                tb.TextChanged += (_, _) => Refresh();
                c = tb;
            }
            if (p.Hint.Length > 0) c.ToolTip = p.Hint;
            _inputs[p.Key] = c;
            ParamsPanel.Children.Add(c);
            if (p.Hint.Length > 0) ParamsPanel.Children.Add(new TextBlock { Text = p.Hint, Style = (Style)FindResource("Hint"), TextWrapping = TextWrapping.Wrap });
        }
        RepeatPanel.Visibility = Shape.Tileable ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    private void Any_Changed(object sender, TextChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (_loading || Shape is null || Summary is null) return;
        var values = new Dictionary<string, double>();
        var texts = new Dictionary<string, string>();
        var bad = new List<string>();
        foreach (var p in Shape.Params)
        {
            if (!_inputs.TryGetValue(p.Key, out var c)) continue;
            if (c is ComboBox cb) values[p.Key] = Math.Max(0, cb.SelectedIndex);
            else if (p.IsText) texts[p.Key] = ((TextBox)c).Text;
            else if (Fmt.TryNum(((TextBox)c).Text, out double v)) values[p.Key] = v;
            else bad.Add(p.Label);
        }
        CountValue = 1;
        if (Shape.Tileable && (!int.TryParse(Count.Text.Trim(), out int cnt) || cnt < 1 || cnt > 200)) bad.Add("Quantidade lado a lado (1 a 200)");
        else if (Shape.Tileable) CountValue = int.Parse(Count.Text.Trim());
        Result = bad.Count > 0 ? null : LotShapes.Build(Shape.Kind, values, texts);
        var items = new List<PreviewItem>();
        if (Result is { Ok: true } r)
        {
            for (int k = 0; k < CountValue; k++)
            {
                var placed = LotShapes.Place(r.Edges, Vec2.Zero, 0, k * r.FrontWidth);
                var ring = new Ring(placed);
                var col = PreviewCanvas.Palette[k % PreviewCanvas.Palette.Length];
                items.Add(PreviewItem.FromRegion(new Region(ring), col, CountValue > 1 ? $"{k + 1}" : null, true, Color.FromRgb(40, 40, 40), 1.6));
                if (k > 0 && CountValue > 3 && k < CountValue - 1) continue;
                // Medida de cada lado, do lado de fora.
                var center = ring.Bounds.Center;
                for (int i = 0; i < placed.Count; i++)
                {
                    var e = placed[i];
                    var m = e.PointAt(0.5);
                    var nrm = e.TangentAt(0.5).PerpRight;
                    if (nrm.Dot(m - center) < 0) nrm = -nrm;
                    double off = Math.Max(ring.Bounds.Width, ring.Bounds.Height) * 0.045;
                    items.Add(new PreviewItem { Label = $"{r.SideNames[i]}\n{Fmt.N(e.Length, 2)} m", LabelAt = m + nrm * off });
                }
            }
            items.Add(new PreviewItem { Line = new List<Vec2> { new(-r.FrontWidth * 0.08, -r.FrontWidth * 0.18), new(r.FrontWidth * CountValue * 1.08, -r.FrontWidth * 0.18) }, Stroke = Color.FromRgb(150, 150, 150), Dashed = true });
            items.Add(new PreviewItem { Label = "RUA (frente)", LabelAt = new Vec2(r.FrontWidth * CountValue / 2, -r.FrontWidth * 0.3) });
            Summary.Text = $"Área: {Fmt.N(r.Area, 2)} m²   •   Perímetro: {Fmt.N(r.Perimeter, 2)} m" +
                           (CountValue > 1 ? $"\n{CountValue} lotes: área total {Fmt.N(r.Area * CountValue, 2)} m²" : "") +
                           (r.Warnings.Count > 0 ? "\n⚠ " + string.Join(" ", r.Warnings) : "");
            OkButton.IsEnabled = true;
        }
        else
        {
            Summary.Text = bad.Count > 0 ? "Valor inválido: " + string.Join(", ", bad) : "✖ " + string.Join("\n✖ ", Result?.Errors ?? new List<string>());
            OkButton.IsEnabled = false;
        }
        Preview.SetItems(items, 0);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Refresh();
        if (Result is not { Ok: true }) return;
        if (Direction == GenDirection.Azimuth)
        {
            if (!AngleText.TryParseAngle(AzText.Text, out double az)) { Fmt.Msg("Azimute inválido."); return; }
            AzimuthDeg = az;
        }
        if (RegisterLots && string.IsNullOrWhiteSpace(StartName)) { Fmt.Msg("Informe o nome/número do lote."); return; }
        DialogResult = true;
    }
}
