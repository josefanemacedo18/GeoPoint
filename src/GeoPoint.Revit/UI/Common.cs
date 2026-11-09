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
