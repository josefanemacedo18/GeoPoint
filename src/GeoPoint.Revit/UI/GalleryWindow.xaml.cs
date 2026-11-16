using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using GeoPoint.Core.Geometry;
using GeoPoint.Core.Model;
using GeoPoint.Core.Sheets;
using GeoPoint.Revit.Infrastructure;

namespace GeoPoint.Revit.UI;

/// <summary>Imagem da galeria na janela.</summary>
public sealed class GalleryItem : Notify
{
    private string _name = "";
    public string Id { get; init; } = "";
    public string Name { get => _name; set => Set(ref _name, value ?? ""); }
    public string Info { get; set; } = "";
    public BitmapSource? Thumb { get; set; }
    public BitmapSource? Full { get; set; }
    /// <summary>Imagem nova (ainda não guardada): os bytes e os dados.</summary>
    public byte[]? NewBytes { get; init; }
    public SatelliteImage? NewMeta { get; init; }
    public string OriginalName { get; init; } = "";
}

public enum GalleryAction { None, Sheet, Fase, Geral, NoImage }

/// <summary>O que mudou na galeria e o que fazer com a imagem escolhida.</summary>
public sealed class GalleryResult
{
    public List<(SatelliteImage Meta, byte[] Jpeg)> Added { get; } = new();
    public List<string> Removed { get; } = new();
    public Dictionary<string, string> Renamed { get; } = new();
    public GalleryAction Action { get; set; }
    public SheetImageChoice? Choice { get; set; }
    public bool AnyGalleryChange => Added.Count > 0 || Removed.Count > 0 || Renamed.Count > 0;
}

/// <summary>
/// Galeria de imagens de satélite do projeto: adicionar (arquivo ou print colado), renomear, remover; escolher a imagem de uma
/// prancha com o enquadramento (arrastar e aproximar) e o contorno do lote por cima; usar só nela, em toda a fase ou como geral.
/// </summary>
public partial class GalleryWindow : Window
{
    private readonly ObservableCollection<GalleryItem> _items = new();
    private readonly ParcelProject _project;
    private readonly Func<string, byte[]?> _load;
    private readonly double _aspect;
    private readonly List<List<Vec2>> _outline;
    private readonly bool _sheetMode;
    private SheetImageChoice _choice;
    private bool _loading = true;

    /// <param name="sheetTitle">Prancha cuja imagem está sendo escolhida ("" = só organizar a galeria).</param>
    /// <param name="current">Escolha atual da prancha (enquadramento) – ou a que ela herda.</param>
    public GalleryWindow(ParcelProject project, Func<string, byte[]?> load, double aspect, string sheetTitle, SheetImageChoice? current,
        List<List<Vec2>> outline, bool locked)
    {
        InitializeComponent();
        _project = project;
        _load = load;
        _aspect = aspect > 0.2 && aspect < 5 ? aspect : 4.0 / 3;
        _outline = outline;
        _sheetMode = sheetTitle.Length > 0;
        _choice = current?.Clone() ?? new SheetImageChoice();
        ContextText.Text = _sheetMode
            ? $"Prancha: {sheetTitle}. Escolha a imagem na lista, enquadre e clique em \"Usar nesta prancha\" (ou para toda a fase). Imagem atual: " +
              (current is null ? "nenhuma." : (LocationImages.Find(project, current.ImageId)?.Nome ?? "—") + ".")
            : "Imagens de satélite do projeto. Para escolher a imagem de uma prancha, clique no espaço da imagem na prévia das Pranchas ou use o comando Imagem de Localização na prancha.";
        UseSheet.Visibility = UseFase.Visibility = NoImage.Visibility = _sheetMode ? Visibility.Visible : Visibility.Collapsed;
        Lock.Visibility = _sheetMode ? Visibility.Visible : Visibility.Collapsed;
        Lock.IsChecked = locked;
        foreach (var img in project.SatelliteImages.OrderByDescending(i => i.Added))
        {
            var it = new GalleryItem { Id = img.Id, Name = img.Nome, OriginalName = img.Nome, Info = InfoOf(img, LocationImages.Uses(project, img.Id)) };
            if (load(img.Id) is byte[] b) { try { it.Full = ImageCodec.Decode(b); it.Thumb = it.Full; } catch { } }
            _items.Add(it);
        }
        List.ItemsSource = _items;
        Outline.IsChecked = _choice.ShowOutline;
        Outline.IsEnabled = OutlineSlider.IsEnabled = outline.Count > 0;
        if (outline.Count == 0) Outline.ToolTip = "Sem lotes nesta prancha para desenhar o contorno.";
        OutlineSlider.Value = _choice.OutlineSize;
        ZoomSlider.Value = _choice.Zoom;
        Editor.Changed += (_, _) => { _loading = true; ZoomSlider.Value = _choice.Zoom; _loading = false; UpdateTexts(); };
        _loading = false;
        List.SelectedItem = _items.FirstOrDefault(i => i.Id == _choice.ImageId) ?? _items.FirstOrDefault();
        ShowSelected();
    }

    public GalleryResult Result { get; } = new();

    private static string InfoOf(SatelliteImage img, int uses) =>
        $"{img.Width}×{img.Height} px · {img.Added:dd/MM/yyyy}" + (uses > 0 ? $" · em uso ({uses})" : "");

    private GalleryItem? Selected => List.SelectedItem as GalleryItem;

    private void ShowSelected()
    {
        var it = Selected;
        if (it is not null && it.Id != _choice.ImageId)
        {
            // Outra imagem: começa centralizada, mantendo o contorno.
            _choice = new SheetImageChoice { ImageId = it.Id, ShowOutline = _choice.ShowOutline, OutlineSize = _choice.OutlineSize, OutlineDX = _choice.OutlineDX, OutlineDY = _choice.OutlineDY };
            _loading = true; ZoomSlider.Value = 1; _loading = false;
        }
        Editor.Set(it?.Full, _aspect, _choice, _outline);
        UpdateTexts();
    }

    private void UpdateTexts()
    {
        ZoomText.Text = $"{_choice.Zoom:0.0}×";
        OutlineText.Text = $"{_choice.OutlineSize * 100:0}%";
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_loading) ShowSelected(); }

    private void Zoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (!_loading) Editor.SetZoom(ZoomSlider.Value); UpdateTexts(); }

    private void Outline_Click(object sender, RoutedEventArgs e) { _choice.ShowOutline = Outline.IsChecked == true; Editor.Refresh(); }

    private void OutlineSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _choice.OutlineSize = OutlineSlider.Value;
        Editor.Refresh();
        UpdateTexts();
    }

    // ------------------------------------------------------------------ galeria

    private void AddImage(BitmapSource src, string name)
    {
        byte[] jpeg;
        try { jpeg = ImageCodec.Normalize(src); }
        catch (Exception ex) { Fmt.Msg("Não foi possível usar a imagem: " + ex.Message, icon: MessageBoxImage.Warning); return; }
        var full = ImageCodec.Decode(jpeg);
        var meta = new SatelliteImage { Nome = name, Width = full.PixelWidth, Height = full.PixelHeight, Bytes = jpeg.Length };
        var it = new GalleryItem { Id = meta.Id, Name = name, OriginalName = name, Info = InfoOf(meta, 0) + " · nova", Full = full, Thumb = full, NewBytes = jpeg, NewMeta = meta };
        _items.Insert(0, it);
        List.SelectedItem = it;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Imagens de satélite (pode escolher várias)", Multiselect = true,
            Filter = "Imagens (PNG, JPG, BMP, TIF)|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|Todos|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        foreach (var f in dlg.FileNames)
        {
            try { AddImage(ImageCodec.FromFile(f), System.IO.Path.GetFileNameWithoutExtension(f)); }
            catch (Exception ex) { Fmt.Msg($"{System.IO.Path.GetFileName(f)}: {ex.Message}", icon: MessageBoxImage.Warning); }
        }
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        BitmapSource? img = null;
        try
        {
            // PNG primeiro (Win+Shift+S, navegadores): o bitmap genérico da área de transferência às vezes vem com o canal alfa zerado.
            if (Clipboard.ContainsData("PNG") && Clipboard.GetData("PNG") is System.IO.MemoryStream png)
                img = BitmapDecoder.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            else if (Clipboard.ContainsImage()) img = Clipboard.GetImage();
            else if (Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Cast<string>().FirstOrDefault() is string f) img = ImageCodec.FromFile(f);
        }
        catch (Exception ex) { Logger.Error("Colar imagem", ex); }
        if (img is null)
        {
            Fmt.Msg("Não há imagem copiada.\n\nTire o print (Print Screen ou Win+Shift+S) ou use \"Copiar imagem\" no navegador e clique em Colar print de novo.",
                icon: MessageBoxImage.Information);
            return;
        }
        AddImage(img, $"Print {DateTime.Now:dd-MM-yyyy HH.mm}");
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } it) return;
        int uses = it.NewMeta is null ? LocationImages.Uses(_project, it.Id) : 0;
        if (uses > 0 && MessageBox.Show(this, $"\"{it.Name}\" está em uso em {uses} prancha(s)/padrão(ões). Remover assim mesmo? (Essas pranchas ficam sem imagem.)",
                "GeoPoint", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _items.Remove(it);
        if (it.NewMeta is null) Result.Removed.Add(it.Id);
        if (_choice.ImageId == it.Id) _choice.ImageId = "";
        List.SelectedItem = _items.FirstOrDefault();
        ShowSelected();
    }

    // ------------------------------------------------------------------ resultado

    private void Collect()
    {
        foreach (var it in _items)
        {
            if (it.NewMeta is not null && it.NewBytes is not null)
            {
                it.NewMeta.Nome = it.Name.Trim().Length > 0 ? it.Name.Trim() : it.OriginalName;
                Result.Added.Add((it.NewMeta, it.NewBytes));
            }
            else if (it.Name.Trim().Length > 0 && it.Name.Trim() != it.OriginalName) Result.Renamed[it.Id] = it.Name.Trim();
        }
        _choice.Locked = Lock.IsChecked == true;
        Result.Choice = _choice.Clone();
    }

    private bool Finish(GalleryAction a)
    {
        if (a is GalleryAction.Sheet or GalleryAction.Fase or GalleryAction.Geral && Selected is null)
        {
            Fmt.Msg("Escolha (ou adicione) uma imagem na lista.", icon: MessageBoxImage.Information);
            return false;
        }
        Collect();
        Result.Action = a;
        DialogResult = true;
        return true;
    }

    private void UseSheet_Click(object sender, RoutedEventArgs e) => Finish(GalleryAction.Sheet);
    private void UseFase_Click(object sender, RoutedEventArgs e) => Finish(GalleryAction.Fase);
    private void UseGeral_Click(object sender, RoutedEventArgs e) => Finish(GalleryAction.Geral);
    private void NoImage_Click(object sender, RoutedEventArgs e) => Finish(GalleryAction.NoImage);
    private void Close_Click(object sender, RoutedEventArgs e) => Finish(GalleryAction.None);
}
