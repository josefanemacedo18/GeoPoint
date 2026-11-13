using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using GeoPoint.Core.Geodesy;
using GeoPoint.Core.Model;

namespace GeoPoint.Revit.UI;

public abstract class Notify : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnChanged(name);
        return true;
    }
}

/// <summary>Linha de lote com seleção.</summary>
public sealed class LotRow : Notify
{
    private bool _selected;
    public LotRow(LotRecord lot, double area, string extra = "")
    {
        Lot = lot;
        Area = area;
        Extra = extra;
    }
    public LotRecord Lot { get; }
    public bool Selected { get => _selected; set => Set(ref _selected, value); }
    public string Nome => Lot.DisplayName;
    public string Quadra => Lot.Quadra;
    public string Uso => LotUseInfo.Label(Lot.Uso);
    public double Area { get; }
    public string AreaText => Fmt.N(Area, 2);
    public string Status => Lot.Status == LotStatus.Ativo ? "Ativo" : "Arquivado";
    public string Extra { get; set; }
}

/// <summary>Opção dinâmica para janelas genéricas.</summary>
public sealed class OptionItem
{
    public OptionItem(string key, string label, bool value, string tooltip = "") { Key = key; Label = label; Bool = value; Tooltip = tooltip; }
    public OptionItem(string key, string label, double number, string tooltip = "") { Key = key; Label = label; Number = number; IsNumber = true; Tooltip = tooltip; }
    public string Key { get; }
    public string Label { get; }
    public bool Bool { get; set; }
    public double Number { get; set; }
    public bool IsNumber { get; }
    public string Tooltip { get; }
}

public sealed class ReportRow
{
    public string Nivel { get; init; } = "";
    public string Categoria { get; init; } = "";
    public string Lote { get; init; } = "";
    public string Mensagem { get; init; } = "";
    public string? LotId { get; init; }
    public IReadOnlyList<int>? Tags { get; init; }
}

public static class Fmt
{
    public static readonly CultureInfo Pt = AngleText.PtBr;
    public static string N(double v, int dec) => AngleText.FormatNumber(v, dec, true);
    public static string Raw(double v, int dec = 3) => Math.Round(v, dec).ToString("0.###", Pt);

    public static bool TryNum(string? text, out double v) => AngleText.TryParseNumber(text, out v);

    /// <summary>Lê número de uma caixa de texto; mostra erro e devolve false se inválido.</summary>
    public static bool ReadNum(TextBox box, string label, out double v, double? min = null, double? max = null)
    {
        if (!TryNum(box.Text, out v) || (min is double mi && v < mi) || (max is double ma && v > ma))
        {
            string range = min is not null && max is not null ? $" (entre {min} e {max})" : min is not null ? $" (mínimo {min})" : "";
            MessageBox.Show($"Valor inválido para \"{label}\"{range}.", "GeoPoint", MessageBoxButton.OK, MessageBoxImage.Warning);
            box.Focus();
            box.SelectAll();
            return false;
        }
        return true;
    }

    public static void Msg(string text, string title = "GeoPoint", MessageBoxImage icon = MessageBoxImage.Information) =>
        MessageBox.Show(text, title, MessageBoxButton.OK, icon);
}

public static class UseOptions
{
    public static IReadOnlyList<KeyValuePair<LotUse, string>> All { get; } =
        LotUseInfo.All.Select(u => new KeyValuePair<LotUse, string>(u, LotUseInfo.Label(u))).ToList();
}

public sealed class EdgeChoice
{
    public EdgeChoice(string text, GeoPoint.Core.Geometry.Curve2 curve, bool testada) { Text = text; Curve = curve; Testada = testada; }
    public string Text { get; }
    public GeoPoint.Core.Geometry.Curve2 Curve { get; }
    public bool Testada { get; }
}

/// <summary>Caixa de diálogo simples para digitar um texto.</summary>
public static class Prompt
{
    public static string? Ask(Window owner, string title, string message, string initial)
    {
        var w = new Window
        {
            Title = "GeoPoint – " + title, Owner = owner, Width = 520, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = new System.Windows.Media.FontFamily("Segoe UI"), FontSize = 12,
        };
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var box = new TextBox { Text = initial, Padding = new Thickness(4, 3, 4, 3) };
        panel.Children.Add(box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 4, 10, 4) };
        var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 80, Padding = new Thickness(10, 4, 10, 4) };
        ok.Click += (_, _) => w.DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        w.Content = panel;
        w.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return w.ShowDialog() == true ? box.Text : null;
    }
}

/// <summary>Texto cinza dentro da caixa vazia (mostra o valor herdado dos Dados do Imóvel).</summary>
public static class Watermark
{
    public static void Set(TextBox box, string text)
    {
        box.Tag = text;
        if (box.IsLoaded) Attach(box);
        else box.Loaded += (_, _) => Attach(box);
        box.TextChanged -= Changed;
        box.TextChanged += Changed;
        System.Windows.Documents.AdornerLayer.GetAdornerLayer(box)?.Update(box);
        foreach (var a in System.Windows.Documents.AdornerLayer.GetAdornerLayer(box)?.GetAdorners(box) ?? Array.Empty<System.Windows.Documents.Adorner>()) a.InvalidateVisual();
    }

    private static void Changed(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox b)
            foreach (var a in System.Windows.Documents.AdornerLayer.GetAdornerLayer(b)?.GetAdorners(b) ?? Array.Empty<System.Windows.Documents.Adorner>()) a.InvalidateVisual();
    }

    private static void Attach(TextBox box)
    {
        var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(box);
        if (layer is null) return;
        if (layer.GetAdorners(box)?.OfType<WatermarkAdorner>().Any() == true) return;
        layer.Add(new WatermarkAdorner(box));
    }

    private sealed class WatermarkAdorner : System.Windows.Documents.Adorner
    {
        private readonly TextBox _box;
        public WatermarkAdorner(TextBox box) : base(box) { _box = box; IsHitTestVisible = false; }

        protected override void OnRender(System.Windows.Media.DrawingContext dc)
        {
            if (_box.Text.Length > 0 || _box.Tag is not string t || string.IsNullOrWhiteSpace(t)) return;
            var ft = new System.Windows.Media.FormattedText(t, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new System.Windows.Media.Typeface(_box.FontFamily, FontStyles.Italic, FontWeights.Normal, FontStretches.Normal), _box.FontSize,
                System.Windows.Media.Brushes.Gray, System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = Math.Max(10, _box.ActualWidth - 10), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            dc.DrawText(ft, new Point(_box.Padding.Left + 4, Math.Max(0, (_box.ActualHeight - ft.Height) / 2)));
        }
    }
}
